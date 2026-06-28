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
/// Handler for CompleteQuestCommand.
/// Creates QuestCompleted transaction, awards rewards, and updates avatar's quest log.
/// </summary>
internal sealed class CompleteQuestHandler : IRequestHandler<CompleteQuestCommand, SagaCommandResult>
{
    private readonly ISagaInstanceRepository _instanceRepository;
    private readonly ISagaReadModelRepository _readModelRepository;
    private readonly IAvatarUpdateService _avatarUpdateService;
    private readonly IWorld _world;

    public CompleteQuestHandler(
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

    public async Task<SagaCommandResult> Handle(CompleteQuestCommand command, CancellationToken ct)
    {
        try
        {
            // Verify quest exists
            var quest = _world.TryGetQuestByRefName(command.QuestRef);
            if (quest == null)
            {
                return SagaCommandResult.Failure(Guid.Empty, $"Quest '{command.QuestRef}' not found");
            }

            // A CompleteQuest dialogue action can fire from an NPC whose Saga did not issue
            // the quest. Resolve the Saga instance that actually holds the active quest so
            // the QuestCompleted transaction lands on the log that records its lifecycle.
            var instance = await QuestInstanceLocator.ResolveActiveQuestInstanceAsync(
                command.AvatarId, command.QuestRef, command.SagaArcRef, _instanceRepository, _world, ct);

            if (instance == null)
            {
                // Not active anywhere — disambiguate "already done" vs. "never accepted" using
                // the caller-specified saga, since that's the best contextual guess available.
                var hinted = await _instanceRepository.GetOrCreateInstanceAsync(command.AvatarId, command.SagaArcRef, ct);
                if (_world.SagaArcLookup.TryGetValue(command.SagaArcRef, out var hintedTemplate)
                    && _world.SagaTriggersLookup.TryGetValue(command.SagaArcRef, out var hintedTriggers))
                {
                    var hintedState = new SagaStateMachine(hintedTemplate, hintedTriggers, _world).ReplayToNow(hinted);
                    if (hintedState.CompletedQuests.Contains(command.QuestRef))
                    {
                        return SagaCommandResult.Failure(hinted.InstanceId, $"Quest '{quest.DisplayName}' already completed");
                    }
                }
                return SagaCommandResult.Failure(hinted.InstanceId, $"Quest '{quest.DisplayName}' not accepted");
            }

            var resolvedSagaRef = instance.SagaRef;

            // Verify Saga exists (for the resolved instance)
            if (!_world.SagaArcLookup.TryGetValue(resolvedSagaRef, out var sagaTemplate))
            {
                return SagaCommandResult.Failure(instance.InstanceId, $"Saga '{resolvedSagaRef}' not found");
            }

            // Get expanded triggers for state machine
            if (!_world.SagaTriggersLookup.TryGetValue(resolvedSagaRef, out var expandedTriggers))
            {
                return SagaCommandResult.Failure(instance.InstanceId, $"Triggers not found for Saga '{resolvedSagaRef}'");
            }

            // Replay to get current state
            var stateMachine = new SagaStateMachine(sagaTemplate, expandedTriggers, _world);
            var currentState = stateMachine.ReplayToNow(instance);

            // Resolver guaranteed ActiveQuests contains the quest — safe lookup.
            var questState = currentState.ActiveQuests[command.QuestRef];

            // NEW: Check if quest is ready for completion (all stages done)
            // In the new multi-stage system, CurrentStage will be empty when all stages are complete
            if (!command.DialogueDriven && !string.IsNullOrEmpty(questState.CurrentStage))
            {
                return SagaCommandResult.Failure(
                    instance.InstanceId,
                    $"Quest '{quest.DisplayName}' not complete - still on stage '{questState.CurrentStage}'");
            }

            // NEW: Check if quest failed (shouldn't complete a failed quest)
            if (questState.IsFailed)
            {
                return SagaCommandResult.Failure(
                    instance.InstanceId,
                    $"Quest '{quest.DisplayName}' failed - cannot complete");
            }

            // Create QuestCompleted transaction
            var transactionData = new Dictionary<string, string>
            {
                [TransactionDataKeys.QuestRef] = command.QuestRef,
                [TransactionDataKeys.QuestDisplayName] = quest.DisplayName,
                [TransactionDataKeys.QuestReceiverRef] = command.QuestReceiverRef,
                [TransactionDataKeys.SagaArcRef] = resolvedSagaRef,
                [TransactionDataKeys.CompletedAt] = DateTime.UtcNow.ToString("O")
            };

            // NEW: Include branch choice if quest had branches
            if (!string.IsNullOrEmpty(questState.ChosenBranch))
            {
                transactionData[TransactionDataKeys.ChosenBranch] = questState.ChosenBranch;
            }

            var transaction = new SagaTransaction
            {
                TransactionId = Guid.NewGuid(),
                Type = SagaTransactionType.QuestCompleted,
                AvatarId = command.AvatarId.ToString(),
                Status = TransactionStatus.Pending,
                LocalTimestamp = DateTime.UtcNow,
                Data = transactionData
            };

            instance.AddTransaction(transaction);

            // Persist and commit transaction atomically
            var (sequenceNumbers, committed) = await _instanceRepository.AddAndCommitTransactionsAsync(
                instance.InstanceId,
                new List<SagaTransaction> { transaction },
                ct);

            if (!committed)
            {
                return SagaCommandResult.Failure(instance.InstanceId, "Concurrency conflict - transaction rolled back");
            }

            // Invalidate cache for the saga that actually owns the quest
            await _readModelRepository.InvalidateCacheAsync(command.AvatarId, resolvedSagaRef, ct);

            // Distribute quest completion rewards
            if (quest.Rewards != null && quest.Rewards.Length > 0)
            {
                // Award OnSuccess rewards (quest completed successfully)
                QuestRewardDistributor.DistributeRewards(
                    quest.Rewards,
                    QuestRewardCondition.OnSuccess,
                    command.Avatar,
                    _world);

                // If quest had branches, also award OnBranch rewards for the chosen branch
                if (!string.IsNullOrEmpty(questState.ChosenBranch))
                {
                    QuestRewardDistributor.DistributeRewards(
                        quest.Rewards,
                        QuestRewardCondition.OnBranch,
                        command.Avatar,
                        _world,
                        branchRef: questState.ChosenBranch);
                }

                // Persist avatar with new rewards
                await _avatarUpdateService.PersistAvatarAsync(command.Avatar, ct);
            }

            // Check if this quest completion ends the game
            var completionRef = _world.WorldConfiguration?.CompletionQuestRef;
            Dictionary<string, object>? resultData = null;
            if (!string.IsNullOrEmpty(completionRef) && command.QuestRef == completionRef)
            {
                resultData = new Dictionary<string, object> { [TransactionDataKeys.GameComplete] = true, [TransactionDataKeys.CompletionQuestRef] = completionRef };
            }

            return SagaCommandResult.Success(
                instance.InstanceId,
                new List<Guid> { transaction.TransactionId },
                sequenceNumbers.First(),
                resultData);
        }
        catch (Exception ex)
        {
            return SagaCommandResult.Failure(Guid.Empty, $"Error completing quest: {ex.Message}");
        }
    }
}
