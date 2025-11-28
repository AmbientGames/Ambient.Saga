using Ambient.Domain.DefinitionExtensions;
using Ambient.Domain.Entities;
using Ambient.SagaEngine.Application.Commands.Saga;
using Ambient.SagaEngine.Application.ReadModels;
using Ambient.SagaEngine.Application.Results.Saga;
using Ambient.SagaEngine.Contracts.Cqrs;
using Ambient.SagaEngine.Contracts.Services;
using Ambient.SagaEngine.Domain.Rpg.Sagas.TransactionLog;
using MediatR;

namespace Ambient.SagaEngine.Application.Handlers.Saga;

/// <summary>
/// Handler for TradeItemCommand.
/// Creates ItemTraded transaction and updates avatar inventory/credits.
/// </summary>
internal sealed class TradeItemHandler : IRequestHandler<TradeItemCommand, SagaCommandResult>
{
    private readonly ISagaInstanceRepository _instanceRepository;
    private readonly ISagaReadModelRepository _readModelRepository;
    private readonly IAvatarUpdateService _avatarUpdateService;
    private readonly World _world;

    public TradeItemHandler(
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

    public async Task<SagaCommandResult> Handle(TradeItemCommand command, CancellationToken ct)
    {
        try
        {
            // Validate Saga exists
            if (!_world.SagaArcLookup.ContainsKey(command.SagaArcRef))
            {
                return SagaCommandResult.Failure(Guid.Empty, $"Saga '{command.SagaArcRef}' not found");
            }

            // Validate quantity
            if (command.Quantity <= 0)
            {
                return SagaCommandResult.Failure(Guid.Empty, "Quantity must be greater than zero");
            }

            // Validate price
            if (command.PricePerItem < 0)
            {
                return SagaCommandResult.Failure(Guid.Empty, $"Invalid price: {command.PricePerItem} (must be non-negative)");
            }

            // Get Saga instance
            var instance = await _instanceRepository.GetOrCreateInstanceAsync(command.AvatarId, command.SagaArcRef, ct);

            // Get Saga template and expanded triggers for state replay
            if (!_world.SagaArcLookup.TryGetValue(command.SagaArcRef, out var sagaTemplate))
            {
                return SagaCommandResult.Failure(instance.InstanceId, $"Saga template '{command.SagaArcRef}' not found");
            }

            if (!_world.SagaTriggersLookup.TryGetValue(command.SagaArcRef, out var expandedTriggers))
            {
                return SagaCommandResult.Failure(instance.InstanceId, $"Triggers not found for Saga '{command.SagaArcRef}'");
            }

            // Replay to get current state (needed to check if character is alive)
            var stateMachine = new SagaStateMachine(sagaTemplate, expandedTriggers, _world);
            var currentState = stateMachine.ReplayToNow(instance);

            // Validate character exists and is alive
            var characterKey = command.CharacterInstanceId.ToString();
            if (!currentState.Characters.TryGetValue(characterKey, out var character))
            {
                return SagaCommandResult.Failure(instance.InstanceId, $"Character '{command.CharacterInstanceId}' not found");
            }

            if (!character.IsAlive)
            {
                return SagaCommandResult.Failure(instance.InstanceId, "Cannot trade with defeated character");
            }

            var totalPrice = command.PricePerItem * command.Quantity;

            // Validate avatar has sufficient credits for buying
            if (command.IsBuying)
            {
                var avatarCredits = command.Avatar.Stats?.Credits ?? 0;
                if (avatarCredits < totalPrice)
                {
                    return SagaCommandResult.Failure(instance.InstanceId,
                        $"Insufficient credits: need {totalPrice}, have {avatarCredits}");
                }
            }
            else
            {
                // Validate avatar has the item for selling
                // Check if it's a consumable, equipment, tool, spell, or block
                var hasItem = false;
                var itemType = "unknown";

                // Check consumables
                if (command.Avatar.Capabilities?.Consumables != null)
                {
                    var consumable = command.Avatar.Capabilities.Consumables
                        .FirstOrDefault(c => c.ConsumableRef == command.ItemRef);
                    if (consumable != null && consumable.Quantity >= command.Quantity)
                    {
                        hasItem = true;
                        itemType = "consumable";
                    }
                }

                // Check equipment
                if (!hasItem && command.Avatar.Capabilities?.Equipment != null)
                {
                    hasItem = command.Avatar.Capabilities.Equipment
                        .Any(e => e.EquipmentRef == command.ItemRef);
                    if (hasItem) itemType = "equipment";
                }

                // Check tools
                if (!hasItem && command.Avatar.Capabilities?.Tools != null)
                {
                    hasItem = command.Avatar.Capabilities.Tools
                        .Any(t => t.ToolRef == command.ItemRef);
                    if (hasItem) itemType = "tool";
                }

                // Check spells
                if (!hasItem && command.Avatar.Capabilities?.Spells != null)
                {
                    hasItem = command.Avatar.Capabilities.Spells
                        .Any(s => s.SpellRef == command.ItemRef);
                    if (hasItem) itemType = "spell";
                }

                // Check blocks
                if (!hasItem && command.Avatar.Capabilities?.Blocks != null)
                {
                    var block = command.Avatar.Capabilities.Blocks
                        .FirstOrDefault(b => b.BlockRef == command.ItemRef);
                    if (block != null && block.Quantity >= command.Quantity)
                    {
                        hasItem = true;
                        itemType = "block";
                    }
                }

                if (!hasItem)
                {
                    return SagaCommandResult.Failure(instance.InstanceId,
                        $"Avatar does not have '{command.ItemRef}' (quantity: {command.Quantity}) to sell");
                }
            }

            // Create ItemTraded transaction
            var transaction = new SagaTransaction
            {
                TransactionId = Guid.NewGuid(),
                Type = SagaTransactionType.ItemTraded,
                AvatarId = command.AvatarId.ToString(),
                Status = TransactionStatus.Pending,
                LocalTimestamp = DateTime.UtcNow,
                Data = new Dictionary<string, string>
                {
                    ["CharacterInstanceId"] = command.CharacterInstanceId.ToString(),
                    ["ItemRef"] = command.ItemRef,
                    ["Quantity"] = command.Quantity.ToString(),
                    ["IsBuying"] = command.IsBuying.ToString(),
                    ["PricePerItem"] = command.PricePerItem.ToString(),
                    ["TotalPrice"] = totalPrice.ToString()
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

            // Invalidate cache
            await _readModelRepository.InvalidateCacheAsync(command.AvatarId, command.SagaArcRef, ct);

            // Update avatar inventory and credits, then persist
            AvatarEntity? updatedAvatar = null;
            if (command.Avatar is AvatarEntity avatarEntity)
            {
                updatedAvatar = await _avatarUpdateService.UpdateAvatarForTradeAsync(
                    avatarEntity,
                    instance,
                    transaction.TransactionId,
                    ct);

                // CRITICAL FIX: Wrap avatar persistence in try-catch
                // If persistence fails, create compensating transaction
                try
                {
                    await _avatarUpdateService.PersistAvatarAsync(updatedAvatar, ct);
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

                    return SagaCommandResult.Failure(
                        instance.InstanceId,
                        $"Trade committed but avatar update failed: {persistEx.Message}");
                }
            }

            var resultData = new Dictionary<string, object>
            {
                ["ItemRef"] = command.ItemRef,
                ["Quantity"] = command.Quantity,
                ["PricePerItem"] = command.PricePerItem,
                ["TotalPrice"] = totalPrice,
                ["TransactionType"] = command.IsBuying ? "Purchase" : "Sale"
            };

            return SagaCommandResult.Success(
                instance.InstanceId,
                new List<Guid> { transaction.TransactionId },
                sequenceNumbers.First(),
                resultData,
                updatedAvatar);
        }
        catch (Exception ex)
        {
            return SagaCommandResult.Failure(Guid.Empty, $"Error trading item: {ex.Message}");
        }
    }
}
