using MediatR;
using Ambient.Domain;
using Ambient.Domain.Contracts;
using Ambient.Saga.Engine.Application.Commands.Saga;
using Ambient.Saga.Engine.Application.ReadModels;
using Ambient.Saga.Engine.Application.Results.Saga;
using Ambient.Saga.Engine.Contracts.Cqrs;
using Ambient.Saga.Engine.Contracts.Services;
using Ambient.Saga.Engine.Domain;
using Ambient.Saga.Engine.Domain.Rpg.Battle;
using Ambient.Saga.Engine.Domain.Rpg.Sagas.TransactionLog;

namespace Ambient.Saga.Engine.Application.Handlers.Saga;

/// <summary>
/// Handler for ApplyBattleDialogueEffectsCommand.
/// Mid-battle dialogue actions (45 ChangeStance, 13 HealSelf/EndBattle/ChangeAffinity
/// uses in shipped content) used to render their text and do nothing — this records
/// their mechanical effect as a battle turn so the running battle and replay honor them.
/// </summary>
internal sealed class ApplyBattleDialogueEffectsHandler : IRequestHandler<ApplyBattleDialogueEffectsCommand, SagaCommandResult>
{
    private readonly ISagaInstanceRepository _instanceRepository;
    private readonly ISagaReadModelRepository _readModelRepository;
    private readonly IAvatarUpdateService _avatarUpdateService;
    private readonly IWorld _world;

    public ApplyBattleDialogueEffectsHandler(
        ISagaInstanceRepository instanceRepository,
        ISagaReadModelRepository readModelRepository,
        IAvatarUpdateService avatarUpdateService,
        IWorld world)
    {
        _instanceRepository = instanceRepository;
        _readModelRepository = readModelRepository;
        _avatarUpdateService = avatarUpdateService;
        _world = world;
    }

    public async Task<SagaCommandResult> Handle(ApplyBattleDialogueEffectsCommand command, CancellationToken ct)
    {
        try
        {
            var instance = await _instanceRepository.GetOrCreateInstanceAsync(command.AvatarId, command.SagaArcRef, ct);

            var battleStartedTx = instance.Transactions
                .FirstOrDefault(t => t.TransactionId == command.BattleInstanceId && t.Type == SagaTransactionType.BattleStarted);
            if (battleStartedTx == null)
            {
                return SagaCommandResult.Failure(instance.InstanceId, $"Battle {command.BattleInstanceId} not found");
            }

            var battleEnded = instance.Transactions.Any(t =>
                t.Type == SagaTransactionType.BattleEnded &&
                t.Data.TryGetValue(TransactionDataKeys.BattleTransactionId, out var endedId) &&
                endedId == command.BattleInstanceId.ToString());
            if (battleEnded)
            {
                return SagaCommandResult.Failure(instance.InstanceId, "Battle has already ended");
            }

            var (avatarCombatant, enemyCombatant, _, _, enemyCharacterInstanceId, _) =
                BattleStateReconstructor.Reconstruct(battleStartedTx, instance, _world);

            var newTransactions = new List<SagaTransaction>();

            // Apply the dialogue-driven effects to the enemy
            var healed = 0f;
            if (command.EnemyHealAmount > 0)
            {
                var before = enemyCombatant.Health;
                enemyCombatant.Health = Math.Clamp(enemyCombatant.Health + command.EnemyHealAmount, 0, Combatant.MAX_STAT);
                healed = enemyCombatant.Health - before;
            }

            if (!string.IsNullOrEmpty(command.EnemyStanceRef))
            {
                enemyCombatant.CombatProfile["Stance"] = command.EnemyStanceRef;
            }

            if (!string.IsNullOrEmpty(command.EnemyAffinityRef))
            {
                enemyCombatant.AffinityRef = command.EnemyAffinityRef;
            }

            if (healed > 0 || !string.IsNullOrEmpty(command.EnemyStanceRef) || !string.IsNullOrEmpty(command.EnemyAffinityRef))
            {
                // Record as a battle turn so the reconstruction fold picks up the new
                // health (TargetHealthAfter targets the enemy by name) and the stance/
                // affinity snapshots written by the transaction helper
                var turnNumber = instance.Transactions.Count(t =>
                    t.Type == SagaTransactionType.BattleTurnExecuted &&
                    t.Data.TryGetValue(TransactionDataKeys.BattleTransactionId, out var battleId) &&
                    battleId == command.BattleInstanceId.ToString()) + 1;

                var effectTx = BattleTransactionHelper.CreateBattleTurnExecutedTransaction(
                    command.AvatarId.ToString(),
                    command.BattleInstanceId,
                    turnNumber,
                    enemyCombatant.DisplayName,
                    false, // enemy-side effect
                    ActionType.Defend, // benign decision type; the payload is in the snapshots
                    null,
                    0f,
                    healed,
                    enemyCombatant.DisplayName, // the enemy is its own target (self-heal/stance)
                    enemyCombatant.Health,
                    enemyCombatant.Stamina,
                    enemyCombatant,
                    _world,
                    instance.InstanceId,
                    avatarState: avatarCombatant,
                    enemyState: enemyCombatant);

                instance.AddTransaction(effectTx);
                newTransactions.Add(effectTx);
            }

            // Dialogue-driven battle conclusion (truce, surrender, scripted victory)
            if (!string.IsNullOrEmpty(command.EndBattleResult))
            {
                var endData = new Dictionary<string, string>
                {
                    [TransactionDataKeys.BattleTransactionId] = command.BattleInstanceId.ToString(),
                    [TransactionDataKeys.TotalTurns] = "0",
                    [TransactionDataKeys.Victor] = command.EndBattleResult,
                    [TransactionDataKeys.Reason] = "Dialogue"
                };

                // "Flee"/"Draw"/truce omit AvatarVictory — state reconstruction reads that as Fled
                if (string.Equals(command.EndBattleResult, "Victory", StringComparison.OrdinalIgnoreCase))
                    endData[TransactionDataKeys.AvatarVictory] = bool.TrueString;
                else if (string.Equals(command.EndBattleResult, "Defeat", StringComparison.OrdinalIgnoreCase))
                    endData[TransactionDataKeys.AvatarVictory] = bool.FalseString;

                var endTx = new SagaTransaction
                {
                    TransactionId = Guid.NewGuid(),
                    Type = SagaTransactionType.BattleEnded,
                    AvatarId = command.AvatarId.ToString(),
                    Status = TransactionStatus.Pending,
                    LocalTimestamp = DateTime.UtcNow,
                    Data = endData
                };
                instance.AddTransaction(endTx);
                newTransactions.Add(endTx);

                // A scripted victory still defeats the enemy so quest objectives count
                if (string.Equals(command.EndBattleResult, "Victory", StringComparison.OrdinalIgnoreCase) &&
                    _world.CharactersLookup.TryGetValue(enemyCombatant.RefName, out var enemyCharacter))
                {
                    var defeatData = new Dictionary<string, string>
                    {
                        [TransactionDataKeys.CharacterInstanceId] = enemyCharacterInstanceId.ToString(),
                        [TransactionDataKeys.CharacterRef] = enemyCharacter.RefName,
                        [TransactionDataKeys.VictorAvatarId] = command.AvatarId.ToString(),
                        [TransactionDataKeys.DefeatMethod] = "Dialogue",
                        [TransactionDataKeys.BattleTransactionId] = command.BattleInstanceId.ToString()
                    };

                    var defeatTx = new SagaTransaction
                    {
                        TransactionId = Guid.NewGuid(),
                        Type = SagaTransactionType.CharacterDefeated,
                        AvatarId = command.AvatarId.ToString(),
                        Status = TransactionStatus.Pending,
                        LocalTimestamp = DateTime.UtcNow,
                        Data = defeatData
                    };
                    instance.AddTransaction(defeatTx);
                    newTransactions.Add(defeatTx);
                }
            }

            if (newTransactions.Count == 0)
            {
                return SagaCommandResult.Success(instance.InstanceId, new List<Guid>(), 0);
            }

            var (sequenceNumbers, committed) = await _instanceRepository.AddAndCommitTransactionsAsync(instance.InstanceId, newTransactions, ct);
            if (!committed)
            {
                return SagaCommandResult.Failure(instance.InstanceId, "Concurrency conflict - transactions rolled back");
            }

            await _readModelRepository.InvalidateCacheAsync(command.AvatarId, command.SagaArcRef, ct);

            // Fold the battle's recorded results (damage, energy spend, equipment durability) into
            // the persisted avatar when the battle concluded HERE — a dialogue truce/surrender/
            // scripted victory. Combat-driven conclusions already do this in ExecuteBattleTurnHandler/
            // SubmitReactionHandler; without it a dialogue ending discarded the whole battle, giving
            // the player a free heal after every scripted end.
            if (!string.IsNullOrEmpty(command.EndBattleResult))
            {
                var foldedAvatar = await _avatarUpdateService.UpdateAvatarForBattleAsync(
                    command.Avatar, instance, command.BattleInstanceId, ct);
                await _avatarUpdateService.PersistAvatarAsync(foldedAvatar, ct);
            }

            return SagaCommandResult.Success(
                instance.InstanceId,
                newTransactions.Select(t => t.TransactionId).ToList(),
                sequenceNumbers.First());
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ApplyBattleDialogueEffects] ERROR: {ex.Message}");
            return SagaCommandResult.Failure(Guid.Empty, $"Error applying battle dialogue effects: {ex.Message}");
        }
    }
}
