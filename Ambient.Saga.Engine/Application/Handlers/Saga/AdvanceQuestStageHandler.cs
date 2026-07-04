using Ambient.Domain;
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
/// Handler for AdvanceQuestStageCommand.
/// Validates all stage objectives are complete and advances to next stage.
/// </summary>
internal sealed class AdvanceQuestStageHandler : IRequestHandler<AdvanceQuestStageCommand, SagaCommandResult>
{
    private readonly ISagaInstanceRepository _instanceRepository;
    private readonly ISagaReadModelRepository _readModelRepository;
    private readonly IMediator _mediator;
    private readonly IAvatarUpdateService _avatarUpdateService;
    private readonly IWorld _world;

    public AdvanceQuestStageHandler(
        ISagaInstanceRepository instanceRepository,
        ISagaReadModelRepository readModelRepository,
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

    public async Task<SagaCommandResult> Handle(AdvanceQuestStageCommand command, CancellationToken ct)
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

            // Find current stage
            var currentStage = quest.Stages?.Stage?.FirstOrDefault(s => s.RefName == questState.CurrentStage);
            if (currentStage == null)
            {
                return SagaCommandResult.Failure(instance.InstanceId, $"Current stage '{questState.CurrentStage}' not found");
            }

            // Validate stage is complete
            var transactions = instance.GetCommittedTransactions();
            if (!QuestProgressEvaluator.IsStageComplete(quest, currentStage, transactions, _world))
            {
                return SagaCommandResult.Failure(instance.InstanceId, $"Stage '{currentStage.DisplayName}' is not yet complete");
            }

            // Determine next stage
            var nextStageRef = QuestProgressEvaluator.GetNextStage(quest, currentStage, transactions);

            // Create QuestStageAdvanced transaction
            var transaction = new SagaTransaction
            {
                TransactionId = Guid.NewGuid(),
                Type = SagaTransactionType.QuestStageAdvanced,
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

            // Reputation stage rewards are transaction-driven (replayed saga state +
            // cross-arc projection, like the dialogue ChangeReputation action) —
            // stage them so they commit atomically with QuestStageAdvanced
            var transactionsToCommit = new List<SagaTransaction> { transaction };
            transactionsToCommit.AddRange(QuestRewardDistributor.CollectRewardTransactions(
                currentStage.Rewards,
                QuestRewardCondition.OnSuccess,
                command.AvatarId.ToString(),
                instance.InstanceId,
                _world));

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
                return SagaCommandResult.Failure(instance.InstanceId, "Concurrency conflict - transaction rolled back");
            }

            // Distribute stage rewards if present
            if (currentStage.Rewards != null && currentStage.Rewards.Length > 0)
            {
                // Award OnSuccess rewards (stage completed successfully)
                QuestRewardDistributor.DistributeRewards(
                    currentStage.Rewards,
                    QuestRewardCondition.OnSuccess,
                    command.Avatar,
                    _world);

                // Persist avatar with new rewards
                await _avatarUpdateService.PersistAvatarAsync(command.Avatar, ct);
            }

            // Invalidate cache
            await _readModelRepository.InvalidateCacheAsync(command.AvatarId, command.SagaArcRef, ct);

            // If nextStage is null, quest is complete - trigger completion
            Dictionary<string, object>? resultData = null;
            if (string.IsNullOrEmpty(nextStageRef))
            {
                var completionResult = await _mediator.Send(new CompleteQuestCommand
                {
                    AvatarId = command.AvatarId,
                    SagaArcRef = command.SagaArcRef,
                    QuestRef = command.QuestRef,
                    QuestReceiverRef = questState.QuestGiverRef, // Turn in to original giver
                    Avatar = command.Avatar
                }, ct);

                if (!completionResult.Successful)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[AdvanceQuestStage] CompleteQuest '{command.QuestRef}' failed: {completionResult.ErrorMessage}");
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
            return SagaCommandResult.Success(
                instance.InstanceId,
                transactionsToCommit.Select(t => t.TransactionId).ToList(),
                sequenceNumbers.First(),
                resultData);
        }
        catch (Exception ex)
        {
            return SagaCommandResult.Failure(Guid.Empty, $"Error advancing quest stage: {ex.Message}");
        }
    }
}
