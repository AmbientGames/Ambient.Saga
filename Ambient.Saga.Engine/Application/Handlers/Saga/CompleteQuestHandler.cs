using Ambient.Domain;
using Ambient.Domain.DefinitionExtensions;
using Ambient.Saga.Engine.Application.Commands.Saga;
using Ambient.Saga.Engine.Application.ReadModels;
using Ambient.Saga.Engine.Application.Results.Saga;
using Ambient.Saga.Engine.Contracts.Cqrs;
using Ambient.Saga.Engine.Contracts.Services;
using Ambient.Saga.Engine.Domain.Rpg.Quests;
using Ambient.Saga.Engine.Domain.Rpg.Sagas.TransactionLog;
using MediatR;

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
    private readonly World _world;

    public CompleteQuestHandler(
        ISagaInstanceRepository instanceRepository,
        ISagaReadModelRepository readModelRepository,
        IAvatarUpdateService avatarUpdateService,
        World world)
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

            // Check if quest is accepted and not already completed
            if (!currentState.ActiveQuests.TryGetValue(command.QuestRef, out var questState))
            {
                if (currentState.CompletedQuests.Contains(command.QuestRef))
                {
                    return SagaCommandResult.Failure(instance.InstanceId, $"Quest '{quest.DisplayName}' already completed");
                }
                return SagaCommandResult.Failure(instance.InstanceId, $"Quest '{quest.DisplayName}' not accepted");
            }

            // NEW: Check if quest is ready for completion (all stages done)
            // In the new multi-stage system, CurrentStage will be empty when all stages are complete
            if (!string.IsNullOrEmpty(questState.CurrentStage))
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
                ["QuestRef"] = command.QuestRef,
                ["QuestDisplayName"] = quest.DisplayName,
                ["QuestReceiverRef"] = command.QuestReceiverRef,
                ["SagaArcRef"] = command.SagaArcRef,
                ["CompletedAt"] = DateTime.UtcNow.ToString("O")
            };

            // NEW: Include branch choice if quest had branches
            if (!string.IsNullOrEmpty(questState.ChosenBranch))
            {
                transactionData["ChosenBranch"] = questState.ChosenBranch;
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

            // Return success (pure CQRS - no state data)
            return SagaCommandResult.Success(
                instance.InstanceId,
                new List<Guid> { transaction.TransactionId },
                sequenceNumbers.First());
        }
        catch (Exception ex)
        {
            return SagaCommandResult.Failure(Guid.Empty, $"Error completing quest: {ex.Message}");
        }
    }
}
