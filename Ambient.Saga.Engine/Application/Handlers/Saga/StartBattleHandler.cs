using MediatR;
using Ambient.Saga.Engine.Application.ReadModels;
using Ambient.Saga.Engine.Domain.Rpg.Sagas.TransactionLog;
using Ambient.Saga.Engine.Application.Results.Saga;
using Ambient.Saga.Engine.Application.Commands.Saga;
using Ambient.Saga.Engine.Contracts.Cqrs;
using Ambient.Saga.Engine.Domain.Rpg.Battle;
using Ambient.Domain;
using Ambient.Domain.Contracts;
using Ambient.Saga.Engine.Domain;

namespace Ambient.Saga.Engine.Application.Handlers.Saga;

/// <summary>
/// Handler for StartBattleCommand.
/// Creates BattleStarted transaction and executes enemy's opening turn.
/// (Enemy always moves first in this battle system)
/// </summary>
internal sealed class StartBattleHandler : IRequestHandler<StartBattleCommand, SagaCommandResult>
{
    private readonly ISagaInstanceRepository _instanceRepository;
    private readonly ISagaReadModelRepository _readModelRepository;
    private readonly IWorld _world;

    public StartBattleHandler(
        ISagaInstanceRepository instanceRepository,
        ISagaReadModelRepository readModelRepository,
        IWorld world)
    {
        _instanceRepository = instanceRepository;
        _readModelRepository = readModelRepository;
        _world = world;
    }

    public async Task<SagaCommandResult> Handle(StartBattleCommand command, CancellationToken ct)
    {
        System.Diagnostics.Debug.WriteLine($"[StartBattle] Starting battle for avatar {command.AvatarId} vs character {command.EnemyCharacterInstanceId}");

        try
        {
            // Handle dev saga refs (format: "RealSagaRef__DEV__uniqueid")
            var sagaRefForLookup = command.SagaArcRef;
            var devSuffix = "__DEV__";
            if (command.SagaArcRef.Contains(devSuffix))
            {
                sagaRefForLookup = command.SagaArcRef.Substring(0, command.SagaArcRef.IndexOf(devSuffix));
            }

            // Verify Saga template exists (use stripped ref for lookup)
            if (!_world.SagaArcLookup.ContainsKey(sagaRefForLookup))
            {
                return SagaCommandResult.Failure(Guid.Empty, $"Saga '{sagaRefForLookup}' not found");
            }

            // Get Saga instance
            var instance = await _instanceRepository.GetOrCreateInstanceAsync(command.AvatarId, command.SagaArcRef, ct);

            // Check if an ACTIVE battle already exists for this character. Battles that
            // already have a BattleEnded (victory, defeat, or fled) must not be returned:
            // re-engaging after fleeing used to hand back the ended battle id, making the
            // enemy permanently unfightable ("Battle has already ended" on every turn).
            var endedBattleIds = instance.Transactions
                .Where(t => t.Type == SagaTransactionType.BattleEnded &&
                            t.Data.TryGetValue(TransactionDataKeys.BattleTransactionId, out _))
                .Select(t => t.Data[TransactionDataKeys.BattleTransactionId])
                .ToHashSet();

            var existingBattle = instance.Transactions
                .Where(t => t.Type == SagaTransactionType.BattleStarted &&
                            !endedBattleIds.Contains(t.TransactionId.ToString()))
                .FirstOrDefault(t =>
                    t.Data.TryGetValue(TransactionDataKeys.EnemyCombatantId, out var enemyId) &&
                    enemyId == command.EnemyCharacterInstanceId.ToString());

            if (existingBattle != null)
            {
                System.Diagnostics.Debug.WriteLine("[StartBattle] Battle already started - returning existing battle ID");
                // Return the BattleInstanceId in Data exactly like the new-battle path below.
                // Without it BattleModal.InitializeBattleAsync treats the resume as a failed
                // start, never refreshes state, and shows "Preparing for battle..." forever —
                // permanently bricking any character whose battle was left un-ended (quit mid-fight).
                return SagaCommandResult.Success(
                    instance.InstanceId,
                    new List<Guid> { existingBattle.TransactionId },
                    existingBattle.SequenceNumber,
                    new Dictionary<string, object>
                    {
                        [TransactionDataKeys.BattleInstanceId] = existingBattle.TransactionId
                    });
            }

            // The enemy keeps damage taken in previous battles (e.g. the player fled and
            // came back): fold in its health at the end of its most recent ended battle,
            // read from the absolute per-turn snapshots. The client-supplied combatant
            // is typically built fresh from the template at full health.
            var previousHealth = GetEnemyHealthFromLastEndedBattle(instance, command.EnemyCharacterInstanceId);
            if (previousHealth.HasValue && previousHealth.Value < command.EnemyCombatant.Health)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[StartBattle] Carrying enemy damage from previous battle: health {command.EnemyCombatant.Health:F3} -> {previousHealth.Value:F3}");
                command.EnemyCombatant.Health = previousHealth.Value;
            }

            // Create battle engine with deterministic seed and companions
            var battleEngine = new BattleEngine(
                command.AvatarCombatant,
                command.EnemyCombatant,
                command.EnemyMind,
                _world,
                command.RandomSeed,
                companions: command.CompanionCombatants);

            battleEngine.SetAvatarAffinities(command.AvatarAffinityRefs);
            battleEngine.RegisterTellsFromWorld(_world);

            if (command.CompanionCombatants?.Count > 0)
            {
                System.Diagnostics.Debug.WriteLine($"[StartBattle] Party includes {command.CompanionCombatants.Count} companions: {string.Join(", ", command.CompanionCombatants.Select(c => c.DisplayName))}");
            }

            // Create BattleStarted transaction
            var battleStartedTransaction = BattleTransactionHelper.CreateBattleStartedTransaction(
                command.AvatarId.ToString(),
                command.SagaArcRef,
                Guid.NewGuid(),  // Avatar combatant ID
                command.EnemyCharacterInstanceId,
                command.EnemyCombatant.RefName,
                command.RandomSeed,
                command.AvatarCombatant,
                command.EnemyCombatant,
                command.AvatarAffinityRefs,
                instance.InstanceId,
                companions: command.CompanionCombatants);

            instance.AddTransaction(battleStartedTransaction);

            // Start battle (enemy moves first)
            battleEngine.StartBattle();

            var transactions = new List<SagaTransaction> { battleStartedTransaction };
            var resultData = new Dictionary<string, object>
            {
                [TransactionDataKeys.BattleInstanceId] = battleStartedTransaction.TransactionId
            };

            if (battleEngine.State == BattleState.AwaitingReaction && battleEngine.PendingAttack != null)
            {
                // Enemy's opening attack produced a tell — reaction phase active.
                // Persist the tell as a turn transaction so the pending attack survives
                // command boundaries and save/reload (see ExecuteBattleTurnHandler).
                var pending = battleEngine.PendingAttack;
                resultData[TransactionDataKeys.AwaitingReaction] = true;
                resultData[TransactionDataKeys.TellRefName] = pending.Tell.RefName;
                resultData[TransactionDataKeys.TellText] = pending.Tell.TellText;
                resultData[TransactionDataKeys.ReactionWindowMs] = pending.Tell.ReactionWindowMs;
                resultData[TransactionDataKeys.BaseDamage] = pending.BaseDamage;
                resultData[TransactionDataKeys.OptimalDefense] = pending.Tell.OptimalDefense.ToString();

                var tellTx = BattleTransactionHelper.CreateBattleTurnExecutedTransaction(
                    command.AvatarId.ToString(),
                    battleStartedTransaction.TransactionId,
                    1,  // Turn 1
                    battleEngine.GetEnemy().DisplayName,
                    false,  // Enemy's turn
                    ActionType.Attack,
                    null,
                    0f,     // No damage yet — resolves via SubmitReaction
                    0f,
                    battleEngine.GetAvatar().DisplayName,
                    battleEngine.GetAvatar().Health,
                    battleEngine.GetEnemy().Stamina,
                    battleEngine.GetEnemy(),
                    _world,
                    instance.InstanceId,
                    avatarState: battleEngine.GetAvatar(),
                    enemyState: battleEngine.GetEnemy(),
                    companionStates: battleEngine.GetCompanions());
                tellTx.Data[TransactionDataKeys.ActionType] = "Tell";
                tellTx.Data[TransactionDataKeys.TellRefName] = pending.Tell.RefName;
                tellTx.Data[TransactionDataKeys.BaseDamage] = BattleTransactionHelper.FormatFloat(pending.BaseDamage);
                tellTx.Data[TransactionDataKeys.ReactionWindowMs] = pending.Tell.ReactionWindowMs.ToString();
                instance.AddTransaction(tellTx);
                transactions.Add(tellTx);

                System.Diagnostics.Debug.WriteLine($"[StartBattle] Enemy opened with tell: {pending.Tell.TellText}");
            }
            else
            {
                // Enemy attacked directly (no tells available) — record the turn
                var enemyAction = battleEngine.ActionHistory.FirstOrDefault();
                if (enemyAction != null)
                {
                    var enemyAfterAction = battleEngine.GetEnemy();
                    var enemyTurnTransaction = BattleTransactionHelper.CreateBattleTurnExecutedTransaction(
                        command.AvatarId.ToString(),
                        battleStartedTransaction.TransactionId,
                        1,  // Turn 1
                        enemyAction.ActorName,
                        false,  // Not avatar turn
                        enemyAction.DecisionType,
                        enemyAction.ItemRefName,
                        enemyAction.Damage,
                        enemyAction.Healing,
                        enemyAction.TargetName,
                        enemyAction.TargetHealthAfter,
                        enemyAction.ActorEnergyAfter,
                        enemyAfterAction,
                        _world,
                        instance.InstanceId,
                        avatarState: battleEngine.GetAvatar(),
                        enemyState: battleEngine.GetEnemy(),
                        companionStates: battleEngine.GetCompanions());

                    instance.AddTransaction(enemyTurnTransaction);
                    transactions.Add(enemyTurnTransaction);

                    System.Diagnostics.Debug.WriteLine($"[StartBattle] Enemy opened with {enemyAction.DecisionType}, dealt {enemyAction.Damage:F2} damage");
                }
            }

            // Commit all transactions
            var (sequenceNumbers, committed) = await _instanceRepository.AddAndCommitTransactionsAsync(
                instance.InstanceId,
                transactions,
                ct);

            if (!committed)
            {
                return SagaCommandResult.Failure(instance.InstanceId, "Concurrency conflict - transaction rolled back");
            }

            // Invalidate cache
            await _readModelRepository.InvalidateCacheAsync(command.AvatarId, command.SagaArcRef, ct);

            System.Diagnostics.Debug.WriteLine($"[StartBattle] Battle started successfully with ID {battleStartedTransaction.TransactionId}");

            return SagaCommandResult.Success(
                instance.InstanceId,
                transactions.Select(t => t.TransactionId).ToList(),
                sequenceNumbers.First(),
                resultData);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[StartBattle] ERROR: {ex.Message}");
            return SagaCommandResult.Failure(Guid.Empty, $"Error starting battle: {ex.Message}");
        }
    }

    /// <summary>
    /// The enemy's health at the end of its most recent ended battle against this avatar,
    /// or null when it has never been fought (or the battle predates the absolute
    /// per-turn health snapshots).
    /// </summary>
    private static float? GetEnemyHealthFromLastEndedBattle(SagaInstance instance, Guid enemyCharacterInstanceId)
    {
        var lastEndedBattleId = instance.Transactions
            .Where(t => t.Type == SagaTransactionType.BattleStarted &&
                        t.Data.TryGetValue(TransactionDataKeys.EnemyCombatantId, out var enemyId) &&
                        enemyId == enemyCharacterInstanceId.ToString())
            .Where(started => instance.Transactions.Any(ended =>
                        ended.Type == SagaTransactionType.BattleEnded &&
                        ended.Data.TryGetValue(TransactionDataKeys.BattleTransactionId, out var endedBattleId) &&
                        endedBattleId == started.TransactionId.ToString()))
            .OrderBy(t => t.SequenceNumber)
            .LastOrDefault()?.TransactionId;

        if (lastEndedBattleId == null)
            return null;

        var lastTurnWithSnapshot = instance.Transactions
            .Where(t => t.Type == SagaTransactionType.BattleTurnExecuted &&
                        t.Data.TryGetValue(TransactionDataKeys.BattleTransactionId, out var battleId) &&
                        battleId == lastEndedBattleId.ToString() &&
                        t.Data.ContainsKey(TransactionDataKeys.EnemyHealthAfterTurn))
            .OrderBy(t => t.SequenceNumber)
            .LastOrDefault();

        if (lastTurnWithSnapshot == null)
            return null;

        return BattleTransactionHelper.TryParseFloat(
            lastTurnWithSnapshot.Data[TransactionDataKeys.EnemyHealthAfterTurn], out var health)
            ? health
            : null;
    }
}
