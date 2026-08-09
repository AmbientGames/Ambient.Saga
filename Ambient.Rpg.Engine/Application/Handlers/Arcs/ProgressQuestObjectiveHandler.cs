using Ambient.Domain.Contracts;
using Ambient.Rpg.Engine.Application.Commands.Arcs;
using Ambient.Rpg.Engine.Application.ReadModels;
using Ambient.Rpg.Engine.Application.Results.Arcs;
using Ambient.Rpg.Engine.Contracts.Cqrs;
using Ambient.Rpg.Engine.Contracts.Services;
using Ambient.Rpg.Engine.Domain.Quests;
using Ambient.Rpg.Engine.Domain.Arcs.TransactionLog;
using MediatR;
using Ambient.Rpg.Engine.Domain;

namespace Ambient.Rpg.Engine.Application.Handlers.Arcs;

/// <summary>
/// Handler for ProgressQuestObjectiveCommand.
/// Checks if objective threshold has been met and creates QuestObjectiveCompleted transaction.
/// </summary>
internal sealed class ProgressQuestObjectiveHandler : IRequestHandler<ProgressQuestObjectiveCommand, ArcCommandResult>
{
    private readonly IArcInstanceRepository _instanceRepository;
    private readonly IArcReadModelRepository _readModelRepository;
    private readonly IAvatarUpdateService _avatarUpdateService;
    private readonly IWorld _world;

    public ProgressQuestObjectiveHandler(
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

    public async Task<ArcCommandResult> Handle(ProgressQuestObjectiveCommand command, CancellationToken ct)
    {
        try
        {
            // Handle dev arc refs (format: "RealArcRef__DEV__uniqueid")
            var arcRefForLookup = command.ArcRef;
            var devSuffix = "__DEV__";
            if (command.ArcRef.Contains(devSuffix))
            {
                arcRefForLookup = command.ArcRef.Substring(0, command.ArcRef.IndexOf(devSuffix));
            }

            // Get Arc instance (use full ref with DEV suffix for unique instance)
            var instance = await _instanceRepository.GetOrCreateInstanceAsync(command.AvatarId, command.ArcRef, ct);

            // Verify Arc exists (use stripped ref for template lookup)
            if (!_world.ArcLookup.TryGetValue(arcRefForLookup, out var arcTemplate))
            {
                return ArcCommandResult.Failure(instance.InstanceId, $"Arc '{arcRefForLookup}' not found");
            }

            // Verify quest exists
            var quest = _world.TryGetQuestByRefName(command.QuestRef);
            if (quest == null)
            {
                return ArcCommandResult.Failure(instance.InstanceId, $"Quest '{command.QuestRef}' not found");
            }

            // Get expanded triggers for state machine (use stripped ref for lookup)
            if (!_world.ArcTriggersLookup.TryGetValue(arcRefForLookup, out var expandedTriggers))
            {
                return ArcCommandResult.Failure(instance.InstanceId, $"Triggers not found for Arc '{arcRefForLookup}'");
            }

            // Replay to get current state
            var stateMachine = new ArcStateMachine(arcTemplate, expandedTriggers, _world);
            var currentState = stateMachine.ReplayToNow(instance);

            // Check if quest is active
            if (!currentState.ActiveQuests.TryGetValue(command.QuestRef, out var questState))
            {
                return ArcCommandResult.Failure(instance.InstanceId, $"Quest '{quest.DisplayName}' is not active");
            }

            // Find the stage
            var stage = quest.Stages?.Stage?.FirstOrDefault(s => s.RefName == command.StageRef);
            if (stage == null)
            {
                return ArcCommandResult.Failure(instance.InstanceId, $"Stage '{command.StageRef}' not found in quest");
            }

            // Find the objective
            var objective = stage.Objectives?.Objective?.FirstOrDefault(o => o.RefName == command.ObjectiveRef);
            if (objective == null)
            {
                return ArcCommandResult.Failure(instance.InstanceId, $"Objective '{command.ObjectiveRef}' not found in stage");
            }

            // Check if objective already completed
            if (questState.CompletedObjectives.TryGetValue(command.StageRef, out var completedObjs) &&
                completedObjs.Contains(command.ObjectiveRef))
            {
                return ArcCommandResult.Success(instance.InstanceId, new List<Guid>(), 0);
            }

            // Evaluate current progress from the transaction log. Objectives can be
            // satisfied cross-arc (the satisfying transaction may live in a different
            // arc's instance than the one that owns the quest), so evaluate against
            // the avatar's whole cross-arc committed log (see CrossArcQuestTransactionLog).
            var transactions = await CrossArcQuestTransactionLog.BuildAsync(command.AvatarId, _instanceRepository, ct);
            var currentValue = QuestProgressEvaluator.EvaluateObjectiveProgress(quest, stage, objective, transactions, _world);

            // Check if threshold met
            if (currentValue < objective.Threshold)
            {
                // Not yet complete
                return ArcCommandResult.Success(instance.InstanceId, new List<Guid>(), 0);
            }

            // Create QuestObjectiveCompleted transaction
            var transaction = new ArcTransaction
            {
                TransactionId = Guid.NewGuid(),
                Type = ArcTransactionType.QuestObjectiveCompleted,
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

            // Reputation OnObjective rewards are transaction-driven (replayed arc
            // state + cross-arc projection, like the dialogue ChangeReputation action)
            // — stage them so they commit atomically with QuestObjectiveCompleted
            var transactionsToCommit = new List<ArcTransaction> { transaction };
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
                return ArcCommandResult.Failure(instance.InstanceId, "Concurrency conflict - transaction rolled back");
            }

            // Invalidate cache
            await _readModelRepository.InvalidateCacheAsync(command.AvatarId, command.ArcRef, ct);

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
            return ArcCommandResult.Success(
                instance.InstanceId,
                transactionsToCommit.Select(t => t.TransactionId).ToList(),
                sequenceNumbers.First());
        }
        catch (Exception ex)
        {
            return ArcCommandResult.Failure(Guid.Empty, $"Error progressing quest objective: {ex.Message}");
        }
    }
}
