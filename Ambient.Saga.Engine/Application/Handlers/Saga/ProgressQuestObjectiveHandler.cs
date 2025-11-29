using Ambient.Domain.DefinitionExtensions;
using Ambient.Saga.Engine.Application.Commands.Saga;
using Ambient.Saga.Engine.Application.ReadModels;
using Ambient.Saga.Engine.Application.Results.Saga;
using Ambient.Saga.Engine.Contracts.Cqrs;
using Ambient.Saga.Engine.Domain.Rpg.Quests;
using Ambient.Saga.Engine.Domain.Rpg.Sagas.TransactionLog;
using MediatR;

namespace Ambient.Saga.Engine.Application.Handlers.Saga;

/// <summary>
/// Handler for ProgressQuestObjectiveCommand.
/// Checks if objective threshold has been met and creates QuestObjectiveCompleted transaction.
/// </summary>
internal sealed class ProgressQuestObjectiveHandler : IRequestHandler<ProgressQuestObjectiveCommand, SagaCommandResult>
{
    private readonly ISagaInstanceRepository _instanceRepository;
    private readonly ISagaReadModelRepository _readModelRepository;
    private readonly World _world;

    public ProgressQuestObjectiveHandler(
        ISagaInstanceRepository instanceRepository,
        ISagaReadModelRepository readModelRepository,
        World world)
    {
        _instanceRepository = instanceRepository;
        _readModelRepository = readModelRepository;
        _world = world;
    }

    public async Task<SagaCommandResult> Handle(ProgressQuestObjectiveCommand command, CancellationToken ct)
    {
        try
        {
            // Get Saga instance
            var instance = await _instanceRepository.GetOrCreateInstanceAsync(command.AvatarId, command.SagaArcRef, ct);

            // Verify Saga exists
            if (!_world.SagaArcLookup.TryGetValue(command.SagaArcRef, out var sagaTemplate))
            {
                return SagaCommandResult.Failure(instance.InstanceId, $"Saga '{command.SagaArcRef}' not found");
            }

            // Verify quest exists
            var quest = _world.TryGetQuestByRefName(command.QuestRef);
            if (quest == null)
            {
                return SagaCommandResult.Failure(instance.InstanceId, $"Quest '{command.QuestRef}' not found");
            }

            // Get expanded triggers for state machine
            if (!_world.SagaTriggersLookup.TryGetValue(command.SagaArcRef, out var expandedTriggers))
            {
                return SagaCommandResult.Failure(instance.InstanceId, $"Triggers not found for Saga '{command.SagaArcRef}'");
            }

            // Replay to get current state
            var stateMachine = new SagaStateMachine(sagaTemplate, expandedTriggers, _world);
            var currentState = stateMachine.ReplayToNow(instance);

            // Check if quest is active
            if (!currentState.ActiveQuests.TryGetValue(command.QuestRef, out var questState))
            {
                return SagaCommandResult.Failure(instance.InstanceId, $"Quest '{quest.DisplayName}' is not active");
            }

            // Find the stage
            var stage = quest.Stages?.Stage?.FirstOrDefault(s => s.RefName == command.StageRef);
            if (stage == null)
            {
                return SagaCommandResult.Failure(instance.InstanceId, $"Stage '{command.StageRef}' not found in quest");
            }

            // Find the objective
            var objective = stage.Objectives?.Objective?.FirstOrDefault(o => o.RefName == command.ObjectiveRef);
            if (objective == null)
            {
                return SagaCommandResult.Failure(instance.InstanceId, $"Objective '{command.ObjectiveRef}' not found in stage");
            }

            // Check if objective already completed
            if (questState.CompletedObjectives.TryGetValue(command.StageRef, out var completedObjs) &&
                completedObjs.Contains(command.ObjectiveRef))
            {
                return SagaCommandResult.Success(instance.InstanceId, new List<Guid>(), 0);
            }

            // Evaluate current progress from transaction log
            var transactions = instance.GetCommittedTransactions();
            var currentValue = QuestProgressEvaluator.EvaluateObjectiveProgress(quest, stage, objective, transactions, _world);

            // Check if threshold met
            if (currentValue < objective.Threshold)
            {
                // Not yet complete
                return SagaCommandResult.Success(instance.InstanceId, new List<Guid>(), 0);
            }

            // Create QuestObjectiveCompleted transaction
            var transaction = new SagaTransaction
            {
                TransactionId = Guid.NewGuid(),
                Type = SagaTransactionType.QuestObjectiveCompleted,
                AvatarId = command.AvatarId.ToString(),
                Status = TransactionStatus.Pending,
                LocalTimestamp = DateTime.UtcNow,
                Data = new Dictionary<string, string>
                {
                    ["QuestRef"] = command.QuestRef,
                    ["StageRef"] = command.StageRef,
                    ["ObjectiveRef"] = command.ObjectiveRef,
                    ["CurrentValue"] = currentValue.ToString(),
                    ["Threshold"] = objective.Threshold.ToString(),
                    ["DisplayName"] = objective.DisplayName ?? objective.RefName
                }
            };

            instance.AddTransaction(transaction);

            // Persist transaction
            var sequenceNumbers = await _instanceRepository.AddTransactionsAsync(
                instance.InstanceId,
                new List<SagaTransaction> { transaction },
                ct);

            // Commit transaction
            var committed = await _instanceRepository.CommitTransactionsAsync(
                instance.InstanceId,
                new List<Guid> { transaction.TransactionId },
                ct);

            if (!committed)
            {
                return SagaCommandResult.Failure(instance.InstanceId, "Concurrency conflict - transaction rolled back");
            }

            // Invalidate cache
            await _readModelRepository.InvalidateCacheAsync(command.AvatarId, command.SagaArcRef, ct);

            // Return success
            return SagaCommandResult.Success(
                instance.InstanceId,
                new List<Guid> { transaction.TransactionId },
                sequenceNumbers.First());
        }
        catch (Exception ex)
        {
            return SagaCommandResult.Failure(Guid.Empty, $"Error progressing quest objective: {ex.Message}");
        }
    }
}
