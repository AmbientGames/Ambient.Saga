using Ambient.Domain.Contracts;
using Ambient.Saga.Engine.Application.Commands.Saga;
using Ambient.Saga.Engine.Application.ReadModels;
using Ambient.Saga.Engine.Application.Results.Saga;
using Ambient.Saga.Engine.Contracts.Cqrs;
using Ambient.Saga.Engine.Contracts.Services;
using Ambient.Saga.Engine.Domain.Rpg.Quests;
using Ambient.Saga.Engine.Domain.Rpg.Sagas.TransactionLog;
using MediatR;
using Ambient.Saga.Engine.Domain;

namespace Ambient.Saga.Engine.Application.Handlers.Saga;

/// <summary>
/// Handler for ProgressQuestObjectiveCommand.
/// Checks if objective threshold has been met and creates QuestObjectiveCompleted transaction.
/// </summary>
internal sealed class ProgressQuestObjectiveHandler : IRequestHandler<ProgressQuestObjectiveCommand, SagaCommandResult>
{
    private readonly ISagaInstanceRepository _instanceRepository;
    private readonly ISagaReadModelRepository _readModelRepository;
    private readonly IAvatarUpdateService _avatarUpdateService;
    private readonly IWorld _world;

    public ProgressQuestObjectiveHandler(
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

    public async Task<SagaCommandResult> Handle(ProgressQuestObjectiveCommand command, CancellationToken ct)
    {
        try
        {
            // Handle dev saga refs (format: "RealSagaRef__DEV__uniqueid")
            var sagaRefForLookup = command.SagaArcRef;
            var devSuffix = "__DEV__";
            if (command.SagaArcRef.Contains(devSuffix))
            {
                sagaRefForLookup = command.SagaArcRef.Substring(0, command.SagaArcRef.IndexOf(devSuffix));
            }

            // Get Saga instance (use full ref with DEV suffix for unique instance)
            var instance = await _instanceRepository.GetOrCreateInstanceAsync(command.AvatarId, command.SagaArcRef, ct);

            // Verify Saga exists (use stripped ref for template lookup)
            if (!_world.SagaArcLookup.TryGetValue(sagaRefForLookup, out var sagaTemplate))
            {
                return SagaCommandResult.Failure(instance.InstanceId, $"Saga '{sagaRefForLookup}' not found");
            }

            // Verify quest exists
            var quest = _world.TryGetQuestByRefName(command.QuestRef);
            if (quest == null)
            {
                return SagaCommandResult.Failure(instance.InstanceId, $"Quest '{command.QuestRef}' not found");
            }

            // Get expanded triggers for state machine (use stripped ref for lookup)
            if (!_world.SagaTriggersLookup.TryGetValue(sagaRefForLookup, out var expandedTriggers))
            {
                return SagaCommandResult.Failure(instance.InstanceId, $"Triggers not found for Saga '{sagaRefForLookup}'");
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
                    [TransactionDataKeys.QuestRef] = command.QuestRef,
                    [TransactionDataKeys.StageRef] = command.StageRef,
                    [TransactionDataKeys.ObjectiveRef] = command.ObjectiveRef,
                    [TransactionDataKeys.CurrentValue] = currentValue.ToString(),
                    [TransactionDataKeys.Threshold] = objective.Threshold.ToString(),
                    [TransactionDataKeys.DisplayName] = objective.DisplayName ?? objective.RefName
                }
            };

            // Reputation OnObjective rewards are transaction-driven (replayed saga
            // state + cross-arc projection, like the dialogue ChangeReputation action)
            // — stage them so they commit atomically with QuestObjectiveCompleted
            var transactionsToCommit = new List<SagaTransaction> { transaction };
            transactionsToCommit.AddRange(QuestRewardDistributor.CollectRewardTransactions(
                quest.Rewards,
                Ambient.Domain.QuestRewardCondition.OnObjective,
                command.AvatarId.ToString(),
                instance.InstanceId,
                _world,
                objectiveRef: command.ObjectiveRef));

            foreach (var tx in transactionsToCommit)
            {
                instance.AddTransaction(tx);
            }

            // Persist and commit transactions
            var (sequenceNumbers, committed) = await _instanceRepository.AddAndCommitTransactionsAsync(
                instance.InstanceId,
                transactionsToCommit,
                ct);

            if (!committed)
            {
                return SagaCommandResult.Failure(instance.InstanceId, "Concurrency conflict - transaction rolled back");
            }

            // Invalidate cache
            await _readModelRepository.InvalidateCacheAsync(command.AvatarId, command.SagaArcRef, ct);

            // Distribute OnObjective rewards after commit and persist the avatar,
            // matching CompleteQuestHandler/AdvanceQuestStageHandler — without the
            // persist, the reward mutation is discarded on the next avatar reload
            if (quest.Rewards != null && quest.Rewards.Length > 0)
            {
                QuestRewardDistributor.DistributeRewards(
                    quest.Rewards,
                    Ambient.Domain.QuestRewardCondition.OnObjective,
                    command.Avatar,
                    _world,
                    objectiveRef: command.ObjectiveRef);

                await _avatarUpdateService.PersistAvatarAsync(command.Avatar, ct);
            }

            // Return success
            return SagaCommandResult.Success(
                instance.InstanceId,
                transactionsToCommit.Select(t => t.TransactionId).ToList(),
                sequenceNumbers.First());
        }
        catch (Exception ex)
        {
            return SagaCommandResult.Failure(Guid.Empty, $"Error progressing quest objective: {ex.Message}");
        }
    }
}
