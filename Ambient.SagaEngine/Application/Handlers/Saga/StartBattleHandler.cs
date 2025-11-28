using Ambient.Domain.DefinitionExtensions;
using MediatR;
using Ambient.SagaEngine.Application.ReadModels;
using Ambient.SagaEngine.Domain.Rpg.Sagas.TransactionLog;
using Ambient.SagaEngine.Application.Results.Saga;
using Ambient.SagaEngine.Application.Commands.Saga;
using Ambient.SagaEngine.Contracts.Cqrs;
using Ambient.SagaEngine.Domain.Rpg.Battle;

namespace Ambient.SagaEngine.Application.Handlers.Saga;

/// <summary>
/// Handler for StartBattleCommand.
/// Creates BattleStarted transaction and executes enemy's opening turn.
/// (Enemy always moves first in this battle system)
/// </summary>
internal sealed class StartBattleHandler : IRequestHandler<StartBattleCommand, SagaCommandResult>
{
    private readonly ISagaInstanceRepository _instanceRepository;
    private readonly ISagaReadModelRepository _readModelRepository;
    private readonly World _world;

    public StartBattleHandler(
        ISagaInstanceRepository instanceRepository,
        ISagaReadModelRepository readModelRepository,
        World world)
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
            // Verify Saga template exists
            if (!_world.SagaArcLookup.ContainsKey(command.SagaArcRef))
            {
                return SagaCommandResult.Failure(Guid.Empty, $"Saga '{command.SagaArcRef}' not found");
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

            // Create battle engine with deterministic seed
            var battleEngine = new BattleEngine(
                command.PlayerCombatant,
                command.EnemyCombatant,
                command.EnemyMind,
                _world,
                command.RandomSeed);

            battleEngine.SetPlayerAffinities(command.PlayerAffinityRefs);

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

            // Create transaction for enemy's opening turn
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

                System.Diagnostics.Debug.WriteLine($"[StartBattle] Enemy opened with {enemyAction.DecisionType}, dealt {enemyAction.Damage:F2} damage");
            }

            // Persist transactions
            var transactions = new List<SagaTransaction> { battleStartedTransaction };
            if (enemyAction != null)
            {
                transactions.Add(instance.Transactions.Last());
            }

            var sequenceNumbers = await _instanceRepository.AddTransactionsAsync(
                instance.InstanceId,
                transactions,
                ct);

            // Commit all transactions
            var transactionIds = transactions.Select(t => t.TransactionId).ToList();
            var committed = await _instanceRepository.CommitTransactionsAsync(
                instance.InstanceId,
                transactionIds,
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
                transactionIds,
                sequenceNumbers.First(),
                new Dictionary<string, object>
                {
                    ["BattleInstanceId"] = battleStartedTransaction.TransactionId
                });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[StartBattle] ERROR: {ex.Message}");
            return SagaCommandResult.Failure(Guid.Empty, $"Error starting battle: {ex.Message}");
        }
    }
}
