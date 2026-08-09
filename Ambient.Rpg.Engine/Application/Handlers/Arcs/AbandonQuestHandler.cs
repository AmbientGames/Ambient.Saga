using Ambient.Domain.Contracts;
using Ambient.Rpg.Engine.Application.Commands.Arcs;
using Ambient.Rpg.Engine.Application.ReadModels;
using Ambient.Rpg.Engine.Application.Results.Arcs;
using Ambient.Rpg.Engine.Contracts.Cqrs;
using Ambient.Rpg.Engine.Contracts.Services;
using Ambient.Rpg.Engine.Domain.Arcs.TransactionLog;
using MediatR;
using Ambient.Rpg.Engine.Domain;

namespace Ambient.Rpg.Engine.Application.Handlers.Arcs;

/// <summary>
/// Handler for AbandonQuestCommand.
/// Creates QuestAbandoned transaction and removes quest from avatar's active quest log.
/// </summary>
internal sealed class AbandonQuestHandler : IRequestHandler<AbandonQuestCommand, ArcCommandResult>
{
    private readonly IArcInstanceRepository _instanceRepository;
    private readonly IArcReadModelRepository _readModelRepository;
    private readonly IAvatarUpdateService _avatarUpdateService;
    private readonly IWorld _world;

    public AbandonQuestHandler(
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

    public async Task<ArcCommandResult> Handle(AbandonQuestCommand command, CancellationToken ct)
    {
        try
        {
            // Verify quest exists
            var quest = _world.TryGetQuestByRefName(command.QuestRef);
            if (quest == null)
            {
                return ArcCommandResult.Failure(Guid.Empty, $"Quest '{command.QuestRef}' not found");
            }

            // Resolve the arc instance that actually holds the active quest — the avatar
            // may be abandoning a quest from an NPC in a different Arc than the one that
            // issued it.
            var instance = await QuestInstanceLocator.ResolveActiveQuestInstanceAsync(
                command.AvatarId, command.QuestRef, command.ArcRef, _instanceRepository, _world, ct);

            if (instance == null)
            {
                var hinted = await _instanceRepository.GetOrCreateInstanceAsync(command.AvatarId, command.ArcRef, ct);
                return ArcCommandResult.Failure(hinted.InstanceId, $"Quest '{quest.DisplayName}' is not active - cannot abandon");
            }

            var resolvedArcRef = instance.ArcRef;

            // Create QuestAbandoned transaction on the owning instance
            var transaction = new ArcTransaction
            {
                TransactionId = Guid.NewGuid(),
                Type = ArcTransactionType.QuestAbandoned,
                AvatarId = command.AvatarId.ToString(),
                Status = TransactionStatus.Pending,
                LocalTimestamp = DateTime.UtcNow,
                Data = new Dictionary<string, string>
                {
                    [TransactionDataKeys.QuestRef] = command.QuestRef,
                    [TransactionDataKeys.QuestDisplayName] = quest.DisplayName,
                    [TransactionDataKeys.ArcRef] = resolvedArcRef
                }
            };

            instance.AddTransaction(transaction);

            // Persist and commit transaction atomically
            var (sequenceNumbers, committed) = await _instanceRepository.AddAndCommitTransactionsAsync(
                instance.InstanceId,
                new List<ArcTransaction> { transaction },
                ct);

            if (!committed)
            {
                return ArcCommandResult.Failure(instance.InstanceId, "Concurrency conflict - transaction rolled back");
            }

            // Invalidate cache for the arc that actually owns the quest
            await _readModelRepository.InvalidateCacheAsync(command.AvatarId, resolvedArcRef, ct);

            // Quest progress is event-sourced from ArcState - no avatar entity update needed
            // Avatar queries active quests via GetActiveQuestsQuery

            // Return success (pure CQRS - no state data)
            return ArcCommandResult.Success(
                instance.InstanceId,
                new List<Guid> { transaction.TransactionId },
                sequenceNumbers.First());
        }
        catch (Exception ex)
        {
            return ArcCommandResult.Failure(Guid.Empty, $"Error abandoning quest: {ex.Message}");
        }
    }
}
