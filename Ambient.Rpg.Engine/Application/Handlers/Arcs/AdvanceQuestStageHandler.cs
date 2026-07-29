using Ambient.Domain;
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
/// Handler for AdvanceQuestStageCommand.
/// Validates all stage objectives are complete and advances to next stage.
/// </summary>
internal sealed class AdvanceQuestStageHandler : IRequestHandler<AdvanceQuestStageCommand, ArcCommandResult>
{
    private readonly IArcInstanceRepository _instanceRepository;
    private readonly IArcReadModelRepository _readModelRepository;
    private readonly IMediator _mediator;
    private readonly IAvatarUpdateService _avatarUpdateService;
    private readonly IWorld _world;

    public AdvanceQuestStageHandler(
        IArcInstanceRepository instanceRepository,
        IArcReadModelRepository readModelRepository,
        IMediator mediator,
        IAvatarUpdateService avatarUpdateService,
        IWorld world)
    {
        _instanceRepository = instanceRepository;
        _readModelRepository = readModelRepository;
        _mediator = mediator;
        _avatarUpdateService = avatarUpdateService;
        _world = world;
    }

    public async Task<ArcCommandResult> Handle(AdvanceQuestStageCommand command, CancellationToken ct)
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

            // Recovery path (R4-30): the final stage-advance sets CurrentStage to "" and then hands
            // off to CompleteQuest. If that nested completion failed, the quest is stranded active
            // with no stage — the stage lookup below would dead-end it forever. Re-attempt the
            // completion so re-invoking this command recovers it instead of failing.
            if (string.IsNullOrEmpty(questState.CurrentStage))
            {
                var retry = await _mediator.Send(new CompleteQuestCommand
                {
                    AvatarId = command.AvatarId,
                    ArcRef = command.ArcRef,
                    QuestRef = command.QuestRef,
                    QuestReceiverRef = questState.QuestGiverRef,
                    Avatar = command.Avatar
                }, ct);

                if (!retry.Successful)
                {
                    return ArcCommandResult.Failure(instance.InstanceId,
                        $"Quest '{quest.DisplayName}' is awaiting completion; retry failed: {retry.ErrorMessage}");
                }

                var recoveredData = new Dictionary<string, object>();
                if (retry.Data.ContainsKey(TransactionDataKeys.GameComplete))
                {
                    recoveredData[TransactionDataKeys.GameComplete] = retry.Data[TransactionDataKeys.GameComplete];
                    if (retry.Data.TryGetValue(TransactionDataKeys.CompletionQuestRef, out var recoveredQuestRef))
                        recoveredData[TransactionDataKeys.CompletionQuestRef] = recoveredQuestRef;
                }
                return ArcCommandResult.Success(instance.InstanceId, new List<Guid>(), instance.Transactions.Count, recoveredData);
            }

            // Find current stage
            var currentStage = quest.Stages?.Stage?.FirstOrDefault(s => s.RefName == questState.CurrentStage);
            if (currentStage == null)
            {
                return ArcCommandResult.Failure(instance.InstanceId, $"Current stage '{questState.CurrentStage}' not found");
            }

            // Validate stage is complete. Objectives can be satisfied cross-arc
            // (a Trail quest's triggers/tokens/dialogue/defeats land in another arc's
            // instance), so re-validate against the avatar's whole cross-arc committed
            // log — otherwise this handler would reject an advance the progression
            // behavior already approved on the same cross-arc evidence.
            var transactions = await CrossArcQuestTransactionLog.BuildAsync(command.AvatarId, _instanceRepository, ct);
            if (!QuestProgressEvaluator.IsStageComplete(quest, currentStage, transactions, _world))
            {
                return ArcCommandResult.Failure(instance.InstanceId, $"Stage '{currentStage.DisplayName}' is not yet complete");
            }

            // Determine next stage
            var nextStageRef = QuestProgressEvaluator.GetNextStage(quest, currentStage, transactions);

            // Create QuestStageAdvanced transaction
            var transaction = new ArcTransaction
            {
                TransactionId = Guid.NewGuid(),
                Type = ArcTransactionType.QuestStageAdvanced,
                AvatarId = command.AvatarId.ToString(),
                Status = TransactionStatus.Pending,
                LocalTimestamp = DateTime.UtcNow,
                Data = new Dictionary<string, string>
                {
                    [TransactionDataKeys.QuestRef] = command.QuestRef,
                    [TransactionDataKeys.FromStage] = questState.CurrentStage,
                    [TransactionDataKeys.NextStage] = nextStageRef ?? string.Empty
                }
            };

            // Stage-level OnObjective rewards (audit H9): granted for every objective
            // of the completed stage that was actually finished. All required
            // objectives are complete by definition here (IsStageComplete passed);
            // OPTIONAL objectives only earn their reward when really done — the two
            // shipped OnObjective rewards (EverestTrail rescue_prisoner,
            // defeat_captain) both target optional objectives.
            var completedObjectiveRefs = new List<string>();
            if (currentStage.Rewards != null && currentStage.Objectives?.Objective != null)
            {
                var objectiveRefsWithRewards = currentStage.Rewards
                    .Where(r => r.Condition == QuestRewardCondition.OnObjective && !string.IsNullOrEmpty(r.ObjectiveRef))
                    .Select(r => r.ObjectiveRef!)
                    .Distinct();

                foreach (var objectiveRef in objectiveRefsWithRewards)
                {
                    var objective = currentStage.Objectives.Objective.FirstOrDefault(o => o.RefName == objectiveRef);
                    if (objective != null &&
                        QuestProgressEvaluator.IsObjectiveComplete(quest, currentStage, objective, transactions, _world))
                    {
                        completedObjectiveRefs.Add(objectiveRef);
                    }
                }
            }

            // Stage-level OnBranch rewards (audit H9): when this stage advances via a
            // chosen branch, the branch's rewards are due now — quest-level OnBranch
            // rewards are CompleteQuestHandler's job.
            string? chosenBranchRef = null;
            if (currentStage.Branches != null)
            {
                chosenBranchRef = QuestProgressEvaluator.ScopeToCurrentAcceptance(quest, transactions)
                    .Where(t => t.Type == ArcTransactionType.QuestBranchChosen &&
                                t.GetData<string>(TransactionDataKeys.QuestRef) == quest.RefName &&
                                t.GetData<string>(TransactionDataKeys.StageRef) == currentStage.RefName)
                    .OrderByDescending(t => t.SequenceNumber)
                    .FirstOrDefault()
                    ?.GetData<string>(TransactionDataKeys.BranchRef);
            }

            // Reputation stage rewards are transaction-driven (replayed arc state +
            // cross-arc projection, like the dialogue ChangeReputation action) —
            // stage them so they commit atomically with QuestStageAdvanced
            var transactionsToCommit = new List<ArcTransaction> { transaction };
            transactionsToCommit.AddRange(QuestRewardDistributor.CollectRewardTransactions(
                currentStage.Rewards,
                QuestRewardCondition.OnSuccess,
                command.AvatarId.ToString(),
                instance.InstanceId,
                _world));
            foreach (var objectiveRef in completedObjectiveRefs)
            {
                transactionsToCommit.AddRange(QuestRewardDistributor.CollectRewardTransactions(
                    currentStage.Rewards,
                    QuestRewardCondition.OnObjective,
                    command.AvatarId.ToString(),
                    instance.InstanceId,
                    _world,
                    objectiveRef: objectiveRef));
            }
            if (!string.IsNullOrEmpty(chosenBranchRef))
            {
                transactionsToCommit.AddRange(QuestRewardDistributor.CollectRewardTransactions(
                    currentStage.Rewards,
                    QuestRewardCondition.OnBranch,
                    command.AvatarId.ToString(),
                    instance.InstanceId,
                    _world,
                    branchRef: chosenBranchRef));
            }

            foreach (var tx in transactionsToCommit)
            {
                instance.AddTransaction(tx);
            }

            // Persist and commit transactions atomically
            var (sequenceNumbers, committed) = await _instanceRepository.AddAndCommitTransactionsAsync(
                instance.InstanceId,
                transactionsToCommit,
                ct);

            if (!committed)
            {
                return ArcCommandResult.Failure(instance.InstanceId, "Concurrency conflict - transaction rolled back");
            }

            // Distribute stage rewards if present
            if (currentStage.Rewards != null && currentStage.Rewards.Length > 0)
            {
                // Snapshot before distributing: if persistence fails, the in-memory
                // avatar must be restored, or a later periodic save would persist
                // rewards the ledger says were reversed (audit M1 — the
                // reverse-on-persist-failure compensation, same pattern as trade).
                var rewardSnapshot = QuestRewardDistributor.CaptureRewardSnapshot(command.Avatar);

                // Award OnSuccess rewards (stage completed successfully)
                QuestRewardDistributor.DistributeRewards(
                    currentStage.Rewards,
                    QuestRewardCondition.OnSuccess,
                    command.Avatar,
                    _world);

                // Award stage-level OnObjective rewards for completed objectives (H9)
                foreach (var objectiveRef in completedObjectiveRefs)
                {
                    QuestRewardDistributor.DistributeRewards(
                        currentStage.Rewards,
                        QuestRewardCondition.OnObjective,
                        command.Avatar,
                        _world,
                        objectiveRef: objectiveRef);
                }

                // Award stage-level OnBranch rewards for the chosen branch (H9)
                if (!string.IsNullOrEmpty(chosenBranchRef))
                {
                    QuestRewardDistributor.DistributeRewards(
                        currentStage.Rewards,
                        QuestRewardCondition.OnBranch,
                        command.Avatar,
                        _world,
                        branchRef: chosenBranchRef);
                }

                // Persist avatar with new rewards; on failure, compensate: restore the
                // in-memory avatar and record the reversal against the stage advance.
                try
                {
                    await _avatarUpdateService.PersistAvatarAsync(command.Avatar, ct);
                }
                catch (Exception persistEx)
                {
                    QuestRewardDistributor.RestoreRewardSnapshot(command.Avatar, rewardSnapshot);

                    var reversalTransaction = new ArcTransaction
                    {
                        TransactionId = Guid.NewGuid(),
                        Type = ArcTransactionType.TransactionReversed,
                        AvatarId = command.AvatarId.ToString(),
                        Status = TransactionStatus.Pending,
                        LocalTimestamp = DateTime.UtcNow,
                        Data = new Dictionary<string, string>
                        {
                            [TransactionDataKeys.ReversedTransactionId] = transaction.TransactionId.ToString(),
                            [TransactionDataKeys.Reason] = $"Avatar persistence failed: {persistEx.Message}",
                            [TransactionDataKeys.OriginalType] = transaction.Type.ToString()
                        }
                    };

                    instance.AddTransaction(reversalTransaction);
                    await _instanceRepository.AddAndCommitTransactionsAsync(
                        instance.InstanceId,
                        new List<ArcTransaction> { reversalTransaction },
                        ct);

                    await _readModelRepository.InvalidateCacheAsync(command.AvatarId, command.ArcRef, ct);

                    return ArcCommandResult.Failure(instance.InstanceId,
                        $"Stage advanced but reward persistence failed: {persistEx.Message}");
                }
            }

            // Invalidate cache
            await _readModelRepository.InvalidateCacheAsync(command.AvatarId, command.ArcRef, ct);

            // If nextStage is null, quest is complete - trigger completion
            Dictionary<string, object>? resultData = null;
            if (string.IsNullOrEmpty(nextStageRef))
            {
                var completionResult = await _mediator.Send(new CompleteQuestCommand
                {
                    AvatarId = command.AvatarId,
                    ArcRef = command.ArcRef,
                    QuestRef = command.QuestRef,
                    QuestReceiverRef = questState.QuestGiverRef, // Turn in to original giver
                    Avatar = command.Avatar
                }, ct);

                if (!completionResult.Successful)
                {
                    // The final stage advanced and committed, but completion failed — the quest is
                    // now active with an empty stage. Surface it so the caller doesn't read a clean
                    // success; re-invoking this command recovers it via the path above (R4-30).
                    System.Diagnostics.Debug.WriteLine(
                        $"[AdvanceQuestStage] CompleteQuest '{command.QuestRef}' failed: {completionResult.ErrorMessage}");
                    resultData = new Dictionary<string, object>
                    {
                        [TransactionDataKeys.QuestEventErrors] = new List<string>
                        {
                            $"CompleteQuest '{command.QuestRef}' failed: {completionResult.ErrorMessage}"
                        }
                    };
                }
                // Propagate the game-complete signal — the stage advance is the only
                // result the caller sees, so the nested completion must not swallow it
                else if (completionResult.Data.ContainsKey(TransactionDataKeys.GameComplete))
                {
                    resultData = new Dictionary<string, object>
                    {
                        [TransactionDataKeys.GameComplete] = completionResult.Data[TransactionDataKeys.GameComplete]
                    };
                    if (completionResult.Data.TryGetValue(TransactionDataKeys.CompletionQuestRef, out var completionQuestRef))
                    {
                        resultData[TransactionDataKeys.CompletionQuestRef] = completionQuestRef;
                    }
                }
            }

            // Return success
            return ArcCommandResult.Success(
                instance.InstanceId,
                transactionsToCommit.Select(t => t.TransactionId).ToList(),
                sequenceNumbers.First(),
                resultData);
        }
        catch (Exception ex)
        {
            return ArcCommandResult.Failure(Guid.Empty, $"Error advancing quest stage: {ex.Message}");
        }
    }
}
