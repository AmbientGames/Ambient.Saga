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
/// Handler for SharpenToolCommand.
/// Restores tool condition to 100% and deducts currency cost.
/// </summary>
internal sealed class SharpenToolHandler : IRequestHandler<SharpenToolCommand, ArcCommandResult>
{
    private readonly IArcInstanceRepository _instanceRepository;
    private readonly IArcReadModelRepository _readModelRepository;
    private readonly IAvatarUpdateService _avatarUpdateService;

    public SharpenToolHandler(
        IArcInstanceRepository instanceRepository,
        IArcReadModelRepository readModelRepository,
        IAvatarUpdateService avatarUpdateService)
    {
        _instanceRepository = instanceRepository;
        _readModelRepository = readModelRepository;
        _avatarUpdateService = avatarUpdateService;
    }

    public async Task<ArcCommandResult> Handle(SharpenToolCommand command, CancellationToken ct)
    {
        try
        {
            // Validate tool exists in avatar's inventory
            var toolEntry = command.Avatar.Capabilities?.Tools?
                .FirstOrDefault(t => t.ToolRef == command.ToolRef);

            if (toolEntry == null)
            {
                return ArcCommandResult.Failure(Guid.Empty,
                    $"Avatar does not have tool '{command.ToolRef}'");
            }

            // Server-authoritative price: never trust the client-supplied cost.
            // A negative cost would mint credits via Credits -= Cost; an underpriced
            // one is a forged discount.
            if (command.Cost != Domain.ToolSharpening.CostCredits)
            {
                return ArcCommandResult.Failure(Guid.Empty,
                    $"Invalid sharpen cost {command.Cost} — sharpening costs {Domain.ToolSharpening.CostCredits}");
            }

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

            // Validate tool needs sharpening
            if (toolEntry.Condition >= 1f)
            {
                return ArcCommandResult.Failure(Guid.Empty,
                    "Tool is already at full condition");
            }

            // Get or create arc instance for transaction logging
            var instance = await _instanceRepository.GetOrCreateInstanceAsync(
                command.AvatarId, command.ArcRef, ct);

            // Store original values for transaction record
            var originalCondition = toolEntry.Condition;
            var originalCredits = command.Avatar.Stats.Credits;

            // Apply changes
            toolEntry.Condition = 1f;
            command.Avatar.Stats.Credits -= command.Cost;

            // Build transaction data
            var transactionData = new Dictionary<string, string>
            {
                [TransactionDataKeys.ToolRef] = command.ToolRef,
                [TransactionDataKeys.ConditionBefore] = originalCondition.ToString("F3"),
                [TransactionDataKeys.ConditionAfter] = "1.000",
                [TransactionDataKeys.Cost] = command.Cost.ToString(),
                [TransactionDataKeys.CreditsBefore] = originalCredits.ToString("F0"),
                [TransactionDataKeys.CreditsAfter] = command.Avatar.Stats.Credits.ToString("F0")
            };

            // Create ToolSharpened transaction
            var transaction = new ArcTransaction
            {
                TransactionId = Guid.NewGuid(),
                Type = ArcTransactionType.ToolSharpened,
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
                toolEntry.Condition = originalCondition;
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
                    $"Tool sharpened but avatar update failed: {persistEx.Message}");
            }

            var resultData = new Dictionary<string, object>
            {
                [TransactionDataKeys.ToolRef] = command.ToolRef,
                [TransactionDataKeys.Cost] = command.Cost,
                [TransactionDataKeys.NewCondition] = 1f
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
            return ArcCommandResult.Failure(Guid.Empty, $"Error sharpening tool: {ex.Message}");
        }
    }
}
