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
/// Handler for CompleteArcCommand.
/// Creates ArcCompleted transaction.
/// </summary>
internal sealed class CompleteArcHandler : IRequestHandler<CompleteArcCommand, ArcCommandResult>
{
    private readonly IArcInstanceRepository _instanceRepository;
    private readonly IArcReadModelRepository _readModelRepository;
    private readonly IWorld _world;

    public CompleteArcHandler(
        IArcInstanceRepository instanceRepository,
        IArcReadModelRepository readModelRepository,
        IWorld world)
    {
        _instanceRepository = instanceRepository;
        _readModelRepository = readModelRepository;
        _world = world;
    }

    public async Task<ArcCommandResult> Handle(CompleteArcCommand command, CancellationToken ct)
    {
        try
        {
            // Get Arc instance
            var instance = await _instanceRepository.GetOrCreateInstanceAsync(command.AvatarId, command.ArcRef, ct);

            // Verify Arc exists
            if (!_world.ArcLookup.TryGetValue(command.ArcRef, out var arcTemplate))
            {
                return ArcCommandResult.Failure(instance.InstanceId, $"Arc '{command.ArcRef}' not found");
            }

            // Create ArcCompleted transaction
            var transaction = new ArcTransaction
            {
                TransactionId = Guid.NewGuid(),
                Type = ArcTransactionType.ArcCompleted,
                AvatarId = command.AvatarId.ToString(),
                Status = TransactionStatus.Pending,
                LocalTimestamp = DateTime.UtcNow,
                Data = new Dictionary<string, string>
                {
                    [TransactionDataKeys.ArcRef] = command.ArcRef,
                    [TransactionDataKeys.CompletionMethod] = command.CompletionMethod ?? "Manual completion"
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
                [TransactionDataKeys.ArcRef] = command.ArcRef,
                [TransactionDataKeys.Completed] = true
            };

            return ArcCommandResult.Success(
                instance.InstanceId,
                new List<Guid> { transaction.TransactionId },
                sequenceNumbers.First(),
                resultData);
        }
        catch (Exception ex)
        {
            return ArcCommandResult.Failure(Guid.Empty, $"Error completing Arc: {ex.Message}");
        }
    }
}
