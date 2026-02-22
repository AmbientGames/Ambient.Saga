using Ambient.Saga.Engine.Application.Commands.Saga;
using Ambient.Saga.Engine.Application.ReadModels;
using Ambient.Saga.Engine.Application.Results.Saga;
using Ambient.Saga.Engine.Contracts.Cqrs;
using Ambient.Saga.Engine.Contracts.Services;
using Ambient.Saga.Engine.Domain.Rpg.Sagas.TransactionLog;
using MediatR;

namespace Ambient.Saga.Engine.Application.Handlers.Saga;

/// <summary>
/// Handler for SharpenToolCommand.
/// Restores tool condition to 100% and deducts currency cost.
/// </summary>
internal sealed class SharpenToolHandler : IRequestHandler<SharpenToolCommand, SagaCommandResult>
{
    private readonly ISagaInstanceRepository _instanceRepository;
    private readonly ISagaReadModelRepository _readModelRepository;
    private readonly IAvatarUpdateService _avatarUpdateService;

    public SharpenToolHandler(
        ISagaInstanceRepository instanceRepository,
        ISagaReadModelRepository readModelRepository,
        IAvatarUpdateService avatarUpdateService)
    {
        _instanceRepository = instanceRepository;
        _readModelRepository = readModelRepository;
        _avatarUpdateService = avatarUpdateService;
    }

    public async Task<SagaCommandResult> Handle(SharpenToolCommand command, CancellationToken ct)
    {
        try
        {
            // Validate tool exists in avatar's inventory
            var toolEntry = command.Avatar.Capabilities?.Tools?
                .FirstOrDefault(t => t.ToolRef == command.ToolRef);

            if (toolEntry == null)
            {
                return SagaCommandResult.Failure(Guid.Empty,
                    $"Avatar does not have tool '{command.ToolRef}'");
            }

            // Validate avatar has enough currency
            if (command.Avatar.Stats == null)
            {
                return SagaCommandResult.Failure(Guid.Empty,
                    "Avatar stats not initialized");
            }

            if (command.Avatar.Stats.Credits < command.Cost)
            {
                return SagaCommandResult.Failure(Guid.Empty,
                    $"Not enough currency. Need {command.Cost}, have {command.Avatar.Stats.Credits:F0}");
            }

            // Validate tool needs sharpening
            if (toolEntry.Condition >= 1f)
            {
                return SagaCommandResult.Failure(Guid.Empty,
                    "Tool is already at full condition");
            }

            // Get or create saga instance for transaction logging
            var instance = await _instanceRepository.GetOrCreateInstanceAsync(
                command.AvatarId, command.SagaArcRef, ct);

            // Store original values for transaction record
            var originalCondition = toolEntry.Condition;
            var originalCredits = command.Avatar.Stats.Credits;

            // Apply changes
            toolEntry.Condition = 1f;
            command.Avatar.Stats.Credits -= command.Cost;

            // Build transaction data
            var transactionData = new Dictionary<string, string>
            {
                ["ToolRef"] = command.ToolRef,
                ["ConditionBefore"] = originalCondition.ToString("F3"),
                ["ConditionAfter"] = "1.000",
                ["Cost"] = command.Cost.ToString(),
                ["CreditsBefore"] = originalCredits.ToString("F0"),
                ["CreditsAfter"] = command.Avatar.Stats.Credits.ToString("F0")
            };

            // Create ToolSharpened transaction
            var transaction = new SagaTransaction
            {
                TransactionId = Guid.NewGuid(),
                Type = SagaTransactionType.ToolSharpened,
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
                // Rollback changes
                toolEntry.Condition = originalCondition;
                command.Avatar.Stats.Credits = originalCredits;

                return SagaCommandResult.Failure(instance.InstanceId,
                    "Concurrency conflict - transaction rolled back");
            }

            // Update transaction status
            transaction.Status = TransactionStatus.Committed;

            // Invalidate cache
            await _readModelRepository.InvalidateCacheAsync(command.AvatarId, command.SagaArcRef, ct);

            // Persist avatar
            try
            {
                await _avatarUpdateService.PersistAvatarAsync(command.Avatar, ct);
            }
            catch (Exception persistEx)
            {
                // Avatar update failed after transaction committed - create compensating transaction
                var reversalTransaction = new SagaTransaction
                {
                    TransactionId = Guid.NewGuid(),
                    Type = SagaTransactionType.TransactionReversed,
                    AvatarId = command.AvatarId.ToString(),
                    Status = TransactionStatus.Pending,
                    LocalTimestamp = DateTime.UtcNow,
                    Data = new Dictionary<string, string>
                    {
                        ["ReversedTransactionId"] = transaction.TransactionId.ToString(),
                        ["Reason"] = $"Avatar persistence failed: {persistEx.Message}",
                        ["OriginalType"] = transaction.Type.ToString()
                    }
                };

                instance.AddTransaction(reversalTransaction);
                await _instanceRepository.AddTransactionsAsync(
                    instance.InstanceId,
                    new List<SagaTransaction> { reversalTransaction },
                    ct);
                await _instanceRepository.CommitTransactionsAsync(
                    instance.InstanceId,
                    new List<Guid> { reversalTransaction.TransactionId },
                    ct);

                return SagaCommandResult.Failure(instance.InstanceId,
                    $"Tool sharpened but avatar update failed: {persistEx.Message}");
            }

            var resultData = new Dictionary<string, object>
            {
                ["ToolRef"] = command.ToolRef,
                ["Cost"] = command.Cost,
                ["NewCondition"] = 1f
            };

            return SagaCommandResult.Success(
                instance.InstanceId,
                new List<Guid> { transaction.TransactionId },
                sequenceNumbers.First(),
                resultData,
                command.Avatar);
        }
        catch (Exception ex)
        {
            return SagaCommandResult.Failure(Guid.Empty, $"Error sharpening tool: {ex.Message}");
        }
    }
}
