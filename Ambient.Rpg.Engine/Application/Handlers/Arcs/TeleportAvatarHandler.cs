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
/// Handler for TeleportAvatarCommand.
/// Logs the teleport transaction and deducts currency cost.
/// The actual position update is handled by the UI layer after this command succeeds.
/// </summary>
internal sealed class TeleportAvatarHandler : IRequestHandler<TeleportAvatarCommand, ArcCommandResult>
{
    private readonly IArcInstanceRepository _instanceRepository;
    private readonly IArcReadModelRepository _readModelRepository;
    private readonly IAvatarUpdateService _avatarUpdateService;

    public TeleportAvatarHandler(
        IArcInstanceRepository instanceRepository,
        IArcReadModelRepository readModelRepository,
        IAvatarUpdateService avatarUpdateService)
    {
        _instanceRepository = instanceRepository;
        _readModelRepository = readModelRepository;
        _avatarUpdateService = avatarUpdateService;
    }

    public async Task<ArcCommandResult> Handle(TeleportAvatarCommand command, CancellationToken ct)
    {
        try
        {
            // Validate avatar has enough currency
            if (command.Avatar.Stats == null)
            {
                return ArcCommandResult.Failure(Guid.Empty,
                    "Avatar stats not initialized");
            }

            if (command.Avatar.Stats.Credits < command.Cost)
            {
                return ArcCommandResult.Failure(Guid.Empty,
                    $"Not enough currency. Need {command.Cost}, have {command.Avatar.Stats.Credits:F0}");
            }

            // Get or create arc instance for transaction logging
            var instance = await _instanceRepository.GetOrCreateInstanceAsync(
                command.AvatarId, command.ArcRef, ct);

            // Store original values for transaction record
            var originalCredits = command.Avatar.Stats.Credits;

            // Apply currency deduction
            command.Avatar.Stats.Credits -= command.Cost;

            // Build transaction data
            var transactionData = new Dictionary<string, string>
            {
                [TransactionDataKeys.DestinationLatitude] = command.DestinationLatitude.ToString("F6"),
                [TransactionDataKeys.DestinationLongitude] = command.DestinationLongitude.ToString("F6"),
                [TransactionDataKeys.Cost] = command.Cost.ToString(),
                [TransactionDataKeys.CreditsBefore] = originalCredits.ToString("F0"),
                [TransactionDataKeys.CreditsAfter] = command.Avatar.Stats.Credits.ToString("F0")
            };

            // Create AvatarTeleported transaction
            var transaction = new ArcTransaction
            {
                TransactionId = Guid.NewGuid(),
                Type = ArcTransactionType.AvatarTeleported,
                AvatarId = command.AvatarId.ToString(),
                Status = TransactionStatus.Pending,
                LocalTimestamp = DateTime.UtcNow,
                Data = transactionData
            };

            instance.AddTransaction(transaction);

            // Persist transaction
            var (sequenceNumbers, committed) = await _instanceRepository.AddAndCommitTransactionsAsync(
                instance.InstanceId,
                new List<ArcTransaction> { transaction },
                ct);

            if (!committed)
            {
                // Rollback changes
                command.Avatar.Stats.Credits = originalCredits;

                return ArcCommandResult.Failure(instance.InstanceId,
                    "Concurrency conflict - transaction rolled back");
            }

            // Update transaction status
            transaction.Status = TransactionStatus.Committed;

            // Invalidate cache
            await _readModelRepository.InvalidateCacheAsync(command.AvatarId, command.ArcRef, ct);

            // Persist avatar
            try
            {
                await _avatarUpdateService.PersistAvatarAsync(command.Avatar, ct);
            }
            catch (Exception persistEx)
            {
                // Avatar update failed after transaction committed - create compensating transaction
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

                return ArcCommandResult.Failure(instance.InstanceId,
                    $"Teleport recorded but avatar update failed: {persistEx.Message}");
            }

            var resultData = new Dictionary<string, object>
            {
                [TransactionDataKeys.DestinationLatitude] = command.DestinationLatitude,
                [TransactionDataKeys.DestinationLongitude] = command.DestinationLongitude,
                [TransactionDataKeys.Cost] = command.Cost
            };

            return ArcCommandResult.Success(
                instance.InstanceId,
                new List<Guid> { transaction.TransactionId },
                sequenceNumbers.First(),
                resultData,
                command.Avatar);
        }
        catch (Exception ex)
        {
            return ArcCommandResult.Failure(Guid.Empty, $"Error teleporting: {ex.Message}");
        }
    }
}
