using MediatR;
using Ambient.Saga.Engine.Application.Commands.Saga;
using Ambient.Saga.Engine.Application.ReadModels;
using Ambient.Saga.Engine.Application.Results.Saga;
using Ambient.Saga.Engine.Contracts.Cqrs;
using Ambient.Saga.Engine.Domain.Rpg.Battle;
using Ambient.Saga.Engine.Domain.Rpg.Sagas.TransactionLog;
using Ambient.Domain;
using Ambient.Domain.Contracts;

namespace Ambient.Saga.Engine.Application.Handlers.Saga;

/// <summary>
/// Handler for SubmitReactionCommand.
/// Processes the player's defensive reaction during the reaction phase.
/// </summary>
internal sealed class SubmitReactionHandler : IRequestHandler<SubmitReactionCommand, SagaCommandResult>
{
    private readonly ISagaInstanceRepository _instanceRepository;
    private readonly ISagaReadModelRepository _readModelRepository;
    private readonly IWorld _world;

    public SubmitReactionHandler(
        ISagaInstanceRepository instanceRepository,
        ISagaReadModelRepository readModelRepository,
        IWorld world)
    {
        _instanceRepository = instanceRepository;
        _readModelRepository = readModelRepository;
        _world = world;
    }

    public async Task<SagaCommandResult> Handle(SubmitReactionCommand command, CancellationToken ct)
    {
        System.Diagnostics.Debug.WriteLine($"[SubmitReaction] Processing reaction {command.Reaction} for battle {command.BattleInstanceId}");

        try
        {
            // Get Saga instance
            var instance = await _instanceRepository.GetOrCreateInstanceAsync(command.AvatarId, command.SagaArcRef, ct);

            // Find BattleStarted transaction
            var battleStartedTx = instance.Transactions
                .FirstOrDefault(t => t.TransactionId == command.BattleInstanceId && t.Type == SagaTransactionType.BattleStarted);

            if (battleStartedTx == null)
            {
                return SagaCommandResult.Failure(instance.InstanceId, $"Battle {command.BattleInstanceId} not found");
            }

            // Check if battle already ended
            var battleEndedTx = instance.Transactions
                .FirstOrDefault(t => t.Type == SagaTransactionType.BattleEnded &&
                                    t.Data.TryGetValue("BattleTransactionId", out var battleId) &&
                                    battleId == command.BattleInstanceId.ToString());

            if (battleEndedTx != null)
            {
                return SagaCommandResult.Failure(instance.InstanceId, "Battle has already ended");
            }

            // Get the turn number
            var turnNumber = GetNextTurnNumber(instance, command.BattleInstanceId);

            // Create transaction for the reaction with full combat data
            var reactionTx = new SagaTransaction
            {
                TransactionId = Guid.NewGuid(),
                Type = SagaTransactionType.BattleTurnExecuted,
                AvatarId = command.AvatarId.ToString(),
                Status = TransactionStatus.Pending,
                LocalTimestamp = DateTime.UtcNow,
                Data = new Dictionary<string, string>
                {
                    ["BattleTransactionId"] = command.BattleInstanceId.ToString(),
                    ["ActionType"] = "Reaction",
                    ["DecisionType"] = ActionType.Defend.ToString(), // Reactions are defensive actions
                    ["ReactionType"] = command.Reaction.ToString(),
                    ["IsPlayerTurn"] = "true",
                    ["TurnNumber"] = turnNumber.ToString(),

                    // Reaction-specific data
                    ["TellRefName"] = command.TellRefName ?? "",
                    ["BaseDamage"] = command.BaseDamage.ToString(),
                    ["DamageDealt"] = command.FinalDamage.ToString("F3"), // Damage TO player
                    ["HealingDone"] = "0",
                    ["CounterDamage"] = (command.CounterDamage ?? 0).ToString("F3"),
                    ["StaminaGained"] = command.StaminaGained.ToString("F3"),
                    ["WasOptimal"] = command.WasOptimal.ToString(),
                    ["TimedOut"] = command.TimedOut.ToString(),

                    // State after reaction
                    ["Target"] = command.Avatar.ArchetypeRef ?? "Player", // Player was target of enemy attack
                    ["TargetHealthAfter"] = command.PlayerHealthAfter.ToString("F3"),
                    ["ActorEnergyAfter"] = command.PlayerEnergyAfter.ToString("F3"),
                    ["EnemyHealthAfter"] = command.EnemyHealthAfter.ToString("F3")
                }
            };

            instance.AddTransaction(reactionTx);

            // Persist and commit
            var sequenceNumbers = await _instanceRepository.AddTransactionsAsync(
                instance.InstanceId,
                new List<SagaTransaction> { reactionTx },
                ct);

            var committed = await _instanceRepository.CommitTransactionsAsync(
                instance.InstanceId,
                new List<Guid> { reactionTx.TransactionId },
                ct);

            if (!committed)
            {
                return SagaCommandResult.Failure(instance.InstanceId, "Concurrency conflict - transaction rolled back");
            }

            // Invalidate cache
            await _readModelRepository.InvalidateCacheAsync(command.AvatarId, command.SagaArcRef, ct);

            System.Diagnostics.Debug.WriteLine($"[SubmitReaction] Reaction {command.Reaction} recorded successfully");

            return SagaCommandResult.Success(
                instance.InstanceId,
                new List<Guid> { reactionTx.TransactionId },
                sequenceNumbers.First());
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SubmitReaction] ERROR: {ex.Message}");
            return SagaCommandResult.Failure(Guid.Empty, $"Error submitting reaction: {ex.Message}");
        }
    }

    private int GetNextTurnNumber(SagaInstance instance, Guid battleInstanceId)
    {
        var turnCount = instance.Transactions
            .Count(t => t.Type == SagaTransactionType.BattleTurnExecuted &&
                       t.Data.TryGetValue("BattleTransactionId", out var battleId) &&
                       battleId == battleInstanceId.ToString());
        return turnCount + 1;
    }
}
