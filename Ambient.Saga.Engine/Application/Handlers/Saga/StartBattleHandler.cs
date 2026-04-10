using MediatR;
using Ambient.Saga.Engine.Application.ReadModels;
using Ambient.Saga.Engine.Domain.Rpg.Sagas.TransactionLog;
using Ambient.Saga.Engine.Application.Results.Saga;
using Ambient.Saga.Engine.Application.Commands.Saga;
using Ambient.Saga.Engine.Contracts.Cqrs;
using Ambient.Saga.Engine.Domain.Rpg.Battle;
using Ambient.Domain.Contracts;

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

            // Check if battle already started for this character
            var existingBattle = instance.Transactions
                .Where(t => t.Type == SagaTransactionType.BattleStarted)
                .FirstOrDefault(t =>
                    t.Data.TryGetValue("EnemyCombatantId", out var enemyId) &&
                    enemyId == command.EnemyCharacterInstanceId.ToString());

            if (existingBattle != null)
            {
                System.Diagnostics.Debug.WriteLine("[StartBattle] Battle already started - returning existing battle ID");
                return SagaCommandResult.Success(
                    instance.InstanceId,
                    new List<Guid> { existingBattle.TransactionId },
                    existingBattle.SequenceNumber);
            }

            // Create battle engine with deterministic seed and companions
            var battleEngine = new BattleEngine(
                command.PlayerCombatant,
                command.EnemyCombatant,
                command.EnemyMind,
                _world,
                command.RandomSeed,
                companions: command.CompanionCombatants);

            battleEngine.SetAvatarAffinities(command.PlayerAffinityRefs);
            battleEngine.RegisterTellsFromWorld(_world);

            if (command.CompanionCombatants?.Count > 0)
            {
                System.Diagnostics.Debug.WriteLine($"[StartBattle] Party includes {command.CompanionCombatants.Count} companions: {string.Join(", ", command.CompanionCombatants.Select(c => c.DisplayName))}");
            }

            // Create BattleStarted transaction
            var battleStartedTransaction = BattleTransactionHelper.CreateBattleStartedTransaction(
                command.AvatarId.ToString(),
                command.SagaArcRef,
                Guid.NewGuid(),  // Player combatant ID
                command.EnemyCharacterInstanceId,
                command.EnemyCombatant.RefName,
                command.RandomSeed,
                command.PlayerCombatant,
                command.EnemyCombatant,
                command.PlayerAffinityRefs,
                instance.InstanceId);

            instance.AddTransaction(battleStartedTransaction);

            // Start battle (enemy moves first)
            battleEngine.StartBattle();

            var transactions = new List<SagaTransaction> { battleStartedTransaction };
            var resultData = new Dictionary<string, object>
            {
                ["BattleInstanceId"] = battleStartedTransaction.TransactionId
            };

            if (battleEngine.State == BattleState.AwaitingReaction && battleEngine.PendingAttack != null)
            {
                // Enemy's opening attack produced a tell — reaction phase active
                var pending = battleEngine.PendingAttack;
                resultData["AwaitingReaction"] = true;
                resultData["TellRefName"] = pending.Tell.RefName;
                resultData["TellText"] = pending.Tell.TellText;
                resultData["ReactionWindowMs"] = pending.Tell.ReactionWindowMs;
                resultData["BaseDamage"] = pending.BaseDamage;
                resultData["OptimalDefense"] = pending.Tell.OptimalDefense.ToString();

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
                        false,  // Not player turn
                        enemyAction.DecisionType,
                        enemyAction.ItemRefName,
                        enemyAction.Damage,
                        enemyAction.Healing,
                        enemyAction.TargetName,
                        enemyAction.TargetHealthAfter,
                        enemyAction.ActorEnergyAfter,
                        enemyAfterAction,
                        _world,
                        instance.InstanceId);

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
}
