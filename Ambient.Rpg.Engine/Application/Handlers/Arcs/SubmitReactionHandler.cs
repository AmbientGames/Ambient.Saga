using MediatR;
using Ambient.Rpg.Engine.Application.Commands.Arcs;
using Ambient.Rpg.Engine.Application.ReadModels;
using Ambient.Rpg.Engine.Application.Results.Arcs;
using Ambient.Rpg.Engine.Contracts.Cqrs;
using Ambient.Rpg.Engine.Contracts.Services;
using Ambient.Rpg.Engine.Domain.Battle;
using Ambient.Rpg.Engine.Domain.Arcs.TransactionLog;
using Ambient.Domain;
using Ambient.Domain.Contracts;
using Ambient.Domain.Entities;
using Ambient.Rpg.Engine.Domain;

namespace Ambient.Rpg.Engine.Application.Handlers.Arcs;

/// <summary>
/// Handler for SubmitReactionCommand.
/// Resolves the avatar's defensive reaction against the PERSISTED telegraphed enemy
/// attack: the server reconstructs the battle, replays the pending tell, and computes
/// the outcome itself (damage, counter, stamina). The client-computed numbers on the
/// command are ignored — they were previously persisted verbatim, which both trusted
/// the client and drifted from the actual combat rules.
/// </summary>
internal sealed class SubmitReactionHandler : IRequestHandler<SubmitReactionCommand, ArcCommandResult>
{
    private readonly IArcInstanceRepository _instanceRepository;
    private readonly IArcReadModelRepository _readModelRepository;
    private readonly IAvatarUpdateService _avatarUpdateService;
    private readonly IWorld _world;

    public SubmitReactionHandler(
        IArcInstanceRepository instanceRepository,
        IArcReadModelRepository readModelRepository,
        IAvatarUpdateService avatarUpdateService,
        IWorld world)
    {
        _instanceRepository = instanceRepository;
        _readModelRepository = readModelRepository;
        _avatarUpdateService = avatarUpdateService;
        _world = world;
    }

    public async Task<ArcCommandResult> Handle(SubmitReactionCommand command, CancellationToken ct)
    {
        System.Diagnostics.Debug.WriteLine($"[SubmitReaction] Processing reaction {command.Reaction} for battle {command.BattleInstanceId}");

        try
        {
            // Get Arc instance
            var instance = await _instanceRepository.GetOrCreateInstanceAsync(command.AvatarId, command.ArcRef, ct);

            // Find BattleStarted transaction
            var battleStartedTx = instance.Transactions
                .FirstOrDefault(t => t.TransactionId == command.BattleInstanceId && t.Type == ArcTransactionType.BattleStarted);

            if (battleStartedTx == null)
            {
                return ArcCommandResult.Failure(instance.InstanceId, $"Battle {command.BattleInstanceId} not found");
            }

            // Check if battle already ended
            var battleEndedTx = instance.Transactions
                .FirstOrDefault(t => t.Type == ArcTransactionType.BattleEnded &&
                                    t.Data.TryGetValue(TransactionDataKeys.BattleTransactionId, out var battleId) &&
                                    battleId == command.BattleInstanceId.ToString());

            if (battleEndedTx != null)
            {
                return ArcCommandResult.Failure(instance.InstanceId, "Battle has already ended");
            }

            // The tell being reacted to must exist in the log — it is persisted when the
            // enemy telegraphs (StartBattle / ExecuteBattleTurn)
            var tellTx = ReactionTransactionFactory.FindUnresolvedTell(instance, command.BattleInstanceId);
            if (tellTx == null)
            {
                return ArcCommandResult.Failure(instance.InstanceId, "No pending enemy attack to react to");
            }

            // Reconstruct the battle as of the tell (the tell transaction's snapshots
            // include the enemy's start-of-turn status-effect tick)
            var (avatarCombatant, enemyCombatant, randomSeed, avatarAffinityRefs, enemyCharacterInstanceId, companions) =
                BattleStateReconstructor.Reconstruct(battleStartedTx, instance, _world);

            var enemyCharacterRef = battleStartedTx.Data[TransactionDataKeys.EnemyCharacterRef];
            var enemyCharacter = _world.GetCharacterByRefName(enemyCharacterRef);
            if (enemyCharacter == null)
            {
                return ArcCommandResult.Failure(instance.InstanceId, $"Enemy character '{enemyCharacterRef}' not found");
            }

            if (command.Avatar?.Capabilities != null)
            {
                avatarCombatant.Capabilities = command.Avatar.Capabilities;
            }
            if (command.Avatar != null)
            {
                avatarCombatant.ArchetypeBias = command.Avatar.ArchetypeBias;
            }

            var executedTurnCount = instance.Transactions
                .Count(t => t.Type == ArcTransactionType.BattleTurnExecuted &&
                            t.Data.TryGetValue(TransactionDataKeys.BattleTransactionId, out var countBattleId) &&
                            countBattleId == command.BattleInstanceId.ToString());

            var battleEngine = new BattleEngine(avatarCombatant, enemyCombatant, new CombatAI(_world, randomSeed), _world, randomSeed,
                companions: companions);
            battleEngine.SetAvatarAffinities(avatarAffinityRefs);
            battleEngine.ResumeBattle(executedTurnCount);
            battleEngine.RegisterTellsFromWorld(_world);

            // Rebuild the pending attack from the persisted tell and resolve it with the
            // SERVER's combat rules. A lapsed reaction window (plus grace) forces None.
            var tellRefName = tellTx.Data[TransactionDataKeys.TellRefName];
            BattleTransactionHelper.TryParseFloat(tellTx.Data[TransactionDataKeys.BaseDamage], out var baseDamage);

            var tellTarget = BattleTransactionHelper.ResolveTellTarget(
                tellTx, battleEngine.GetAvatar(), battleEngine.GetCompanions());
            if (!battleEngine.BeginAttackWithTell(battleEngine.GetEnemy(), tellTarget, tellRefName, baseDamage))
            {
                return ArcCommandResult.Failure(instance.InstanceId, $"Attack tell '{tellRefName}' not found in world content");
            }

            var serverExpired = ReactionTransactionFactory.IsExpired(tellTx, DateTime.UtcNow);
            var chosenReaction = serverExpired || command.TimedOut ? AvatarDefenseType.None : command.Reaction;

            var result = battleEngine.ResolveReaction(chosenReaction);
            if (result == null)
            {
                return ArcCommandResult.Failure(instance.InstanceId, "Failed to resolve reaction");
            }

            var turnNumber = executedTurnCount + 1;
            var reactionTx = ReactionTransactionFactory.Create(
                command.AvatarId, command.BattleInstanceId, turnNumber,
                result, tellRefName, baseDamage, battleEngine, instance.InstanceId);
            if (serverExpired || command.TimedOut)
            {
                reactionTx.Data[TransactionDataKeys.TimedOut] = bool.TrueString;
            }

            instance.AddTransaction(reactionTx);
            var newTransactions = new List<ArcTransaction> { reactionTx };

            // A reaction can END the battle: the un-reacted/lethal hit kills the avatar,
            // or the counter-attack kills the enemy. This used to leave the battle
            // "active" against a 0-HP combatant with no victory/defeat processing.
            if (battleEngine.State == BattleState.Victory ||
                battleEngine.State == BattleState.Defeat ||
                battleEngine.State == BattleState.Fled)
            {
                System.Diagnostics.Debug.WriteLine($"[SubmitReaction] Battle ended on reaction: {battleEngine.State}");
                BattleEndTransactionFactory.Create(
                    command.AvatarId,
                    command.Avatar,
                    command.BattleInstanceId,
                    instance,
                    battleEngine,
                    turnNumber,
                    enemyCharacterInstanceId,
                    enemyCharacter,
                    newTransactions);
            }

            // Persist and commit
            var (sequenceNumbers, committed) = await _instanceRepository.AddAndCommitTransactionsAsync(
                instance.InstanceId,
                newTransactions,
                ct);

            if (!committed)
            {
                return ArcCommandResult.Failure(instance.InstanceId, "Concurrency conflict - transaction rolled back");
            }

            // Invalidate cache
            await _readModelRepository.InvalidateCacheAsync(command.AvatarId, command.ArcRef, ct);

            // Fold the concluded battle into the persisted avatar (same as ExecuteBattleTurn)
            AvatarEntity? updatedAvatar = null;
            if (battleEngine.State != BattleState.AvatarTurn &&
                battleEngine.State != BattleState.EnemyTurn &&
                command.Avatar is AvatarEntity avatarEntity)
            {
                updatedAvatar = await _avatarUpdateService.UpdateAvatarForBattleAsync(
                    avatarEntity,
                    instance,
                    command.BattleInstanceId,
                    ct);

                await _avatarUpdateService.PersistAvatarAsync(updatedAvatar, ct);
            }

            System.Diagnostics.Debug.WriteLine($"[SubmitReaction] Reaction {result.ChosenReaction} resolved server-side (damage {result.FinalDamage:F3}, counter {result.CounterDamage ?? 0f:F3})");

            // Return the server-computed outcome so the UI can display it
            return ArcCommandResult.Success(
                instance.InstanceId,
                newTransactions.Select(t => t.TransactionId).ToList(),
                sequenceNumbers.First(),
                new Dictionary<string, object>
                {
                    [TransactionDataKeys.ReactionType] = result.ChosenReaction.ToString(),
                    [TransactionDataKeys.DamageDealt] = result.FinalDamage,
                    [TransactionDataKeys.CounterDamage] = result.CounterDamage ?? 0f,
                    [TransactionDataKeys.StaminaGained] = result.StaminaGained,
                    [TransactionDataKeys.WasOptimal] = result.WasOptimal,
                    [TransactionDataKeys.TimedOut] = result.TimedOut || serverExpired,
                    [TransactionDataKeys.BattleOutcome] = battleEngine.State.ToString()
                },
                updatedAvatar);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SubmitReaction] ERROR: {ex.Message}");
            return ArcCommandResult.Failure(Guid.Empty, $"Error submitting reaction: {ex.Message}");
        }
    }
}
