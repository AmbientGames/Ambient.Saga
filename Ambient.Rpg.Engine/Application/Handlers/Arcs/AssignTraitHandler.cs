using MediatR;
using Ambient.Rpg.Engine.Application.ReadModels;
using Ambient.Rpg.Engine.Domain.Arcs.TransactionLog;
using Ambient.Rpg.Engine.Application.Results.Arcs;
using Ambient.Rpg.Engine.Contracts.Cqrs;
using Ambient.Rpg.Engine.Application.Commands.Arcs;
using Ambient.Domain.Contracts;
using Ambient.Rpg.Engine.Domain;

namespace Ambient.Rpg.Engine.Application.Handlers.Arcs;

/// <summary>
/// Handler for AssignTraitCommand.
/// Creates TraitAssigned transaction.
/// </summary>
internal sealed class AssignTraitHandler : IRequestHandler<AssignTraitCommand, ArcCommandResult>
{
    private readonly IArcInstanceRepository _instanceRepository;
    private readonly IArcReadModelRepository _readModelRepository;
    private readonly IWorld _world;

    public AssignTraitHandler(
        IArcInstanceRepository instanceRepository,
        IArcReadModelRepository readModelRepository,
        IWorld world)
    {
        _instanceRepository = instanceRepository;
        _readModelRepository = readModelRepository;
        _world = world;
    }

    public async Task<ArcCommandResult> Handle(AssignTraitCommand command, CancellationToken ct)
    {
        try
        {
            // Get Arc instance
            var instance = await _instanceRepository.GetOrCreateInstanceAsync(command.AvatarId, command.ArcRef, ct);

            // Create TraitAssigned transaction
            var transaction = new ArcTransaction
            {
                TransactionId = Guid.NewGuid(),
                Type = ArcTransactionType.TraitAssigned,
                AvatarId = command.AvatarId.ToString(),
                Status = TransactionStatus.Pending,
                LocalTimestamp = DateTime.UtcNow,
                Data = new Dictionary<string, string>
                {
                    [TransactionDataKeys.CharacterRef] = command.CharacterRef,
                    [TransactionDataKeys.TraitType] = command.TraitType,
                    [TransactionDataKeys.Reason] = command.Reason ?? "Manual assignment"
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

            // Invalidate cache
            await _readModelRepository.InvalidateCacheAsync(command.AvatarId, command.ArcRef, ct);

            var resultData = new Dictionary<string, object>
            {
                [TransactionDataKeys.TraitType] = command.TraitType,
                [TransactionDataKeys.CharacterRef] = command.CharacterRef
            };

            return ArcCommandResult.Success(
                instance.InstanceId,
                new List<Guid> { transaction.TransactionId },
                sequenceNumbers.First(),
                resultData);
        }
        catch (Exception ex)
        {
            return ArcCommandResult.Failure(Guid.Empty, $"Error assigning trait: {ex.Message}");
        }
    }
}
