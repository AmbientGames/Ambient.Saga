using Ambient.Domain.Contracts;
using Ambient.Saga.Engine.Application.Commands.Saga;
using Ambient.Saga.Engine.Application.ReadModels;
using Ambient.Saga.Engine.Application.Results.Saga;
using Ambient.Saga.Engine.Contracts.Cqrs;
using Ambient.Saga.Engine.Contracts.Services;
using Ambient.Saga.Engine.Domain.Rpg.Sagas.TransactionLog;
using MediatR;
using Ambient.Saga.Engine.Domain;

namespace Ambient.Saga.Engine.Application.Handlers.Saga;

/// <summary>
/// Handler for AbandonQuestCommand.
/// Creates QuestAbandoned transaction and removes quest from avatar's active quest log.
/// </summary>
internal sealed class AbandonQuestHandler : IRequestHandler<AbandonQuestCommand, SagaCommandResult>
{
    private readonly ISagaInstanceRepository _instanceRepository;
    private readonly ISagaReadModelRepository _readModelRepository;
    private readonly IAvatarUpdateService _avatarUpdateService;
    private readonly IWorld _world;

    public AbandonQuestHandler(
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

    public async Task<SagaCommandResult> Handle(AbandonQuestCommand command, CancellationToken ct)
    {
        try
        {
            // Verify quest exists
            var quest = _world.TryGetQuestByRefName(command.QuestRef);
            if (quest == null)
            {
                return SagaCommandResult.Failure(Guid.Empty, $"Quest '{command.QuestRef}' not found");
            }

            // Resolve the Saga instance that actually holds the active quest — the avatar
            // may be abandoning a quest from an NPC in a different Saga than the one that
            // issued it.
            var instance = await QuestInstanceLocator.ResolveActiveQuestInstanceAsync(
                command.AvatarId, command.QuestRef, command.SagaArcRef, _instanceRepository, _world, ct);

            if (instance == null)
            {
                var hinted = await _instanceRepository.GetOrCreateInstanceAsync(command.AvatarId, command.SagaArcRef, ct);
                return SagaCommandResult.Failure(hinted.InstanceId, $"Quest '{quest.DisplayName}' is not active - cannot abandon");
            }

            var resolvedSagaRef = instance.SagaRef;

            // Create QuestAbandoned transaction on the owning instance
            var transaction = new SagaTransaction
            {
                TransactionId = Guid.NewGuid(),
                Type = SagaTransactionType.QuestAbandoned,
                AvatarId = command.AvatarId.ToString(),
                Status = TransactionStatus.Pending,
                LocalTimestamp = DateTime.UtcNow,
                Data = new Dictionary<string, string>
                {
                    [TransactionDataKeys.QuestRef] = command.QuestRef,
                    [TransactionDataKeys.QuestDisplayName] = quest.DisplayName,
                    [TransactionDataKeys.SagaArcRef] = resolvedSagaRef
                }
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

            // Quest progress is event-sourced from SagaState - no avatar entity update needed
            // Avatar queries active quests via GetActiveQuestsQuery

            // Return success (pure CQRS - no state data)
            return SagaCommandResult.Success(
                instance.InstanceId,
                new List<Guid> { transaction.TransactionId },
                sequenceNumbers.First());
        }
        catch (Exception ex)
        {
            return SagaCommandResult.Failure(Guid.Empty, $"Error abandoning quest: {ex.Message}");
        }
    }
}
