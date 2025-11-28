using Ambient.Domain;
using Ambient.Domain.DefinitionExtensions;
using Ambient.SagaEngine.Application.Commands.Saga;
using Ambient.SagaEngine.Application.ReadModels;
using Ambient.SagaEngine.Application.Results.Saga;
using Ambient.SagaEngine.Contracts.Cqrs;
using Ambient.SagaEngine.Contracts.Services;
using Ambient.SagaEngine.Domain.Rpg.Quests;
using Ambient.SagaEngine.Domain.Rpg.Sagas.TransactionLog;
using MediatR;

namespace Ambient.SagaEngine.Application.Handlers.Saga;

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
    private readonly World _world;

    public AdvanceQuestStageHandler(
        ISagaInstanceRepository instanceRepository,
        ISagaReadModelRepository readModelRepository,
        IMediator mediator,
        IAvatarUpdateService avatarUpdateService,
        World world)
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
                    ["QuestRef"] = command.QuestRef,
                    ["FromStage"] = questState.CurrentStage,
                    ["NextStage"] = nextStageRef ?? string.Empty
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
            if (string.IsNullOrEmpty(nextStageRef))
            {
                await _mediator.Send(new CompleteQuestCommand
                {
                    AvatarId = command.AvatarId,
                    SagaArcRef = command.SagaArcRef,
                    QuestRef = command.QuestRef,
                    QuestReceiverRef = questState.QuestGiverRef, // Turn in to original giver
                    Avatar = command.Avatar
                }, ct);
            }

            // Return success
            return SagaCommandResult.Success(
                instance.InstanceId,
                new List<Guid> { transaction.TransactionId },
                sequenceNumbers.First());
        }
        catch (Exception ex)
        {
            return SagaCommandResult.Failure(Guid.Empty, $"Error advancing quest stage: {ex.Message}");
        }
    }
}
