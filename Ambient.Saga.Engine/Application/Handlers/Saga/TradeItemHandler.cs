using Ambient.Domain;
using Ambient.Domain.Contracts;
using Ambient.Domain.Entities;
using Ambient.Domain.GameLogic.Gameplay.Avatar;
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
/// Handler for TradeItemCommand.
/// Creates ItemTraded transaction and updates avatar inventory/credits.
/// </summary>
internal sealed class TradeItemHandler : IRequestHandler<TradeItemCommand, SagaCommandResult>
{
    private readonly ISagaInstanceRepository _instanceRepository;
    private readonly ISagaReadModelRepository _readModelRepository;
    private readonly IAvatarUpdateService _avatarUpdateService;
    private readonly IWorld _world;

    public TradeItemHandler(
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

    public async Task<SagaCommandResult> Handle(TradeItemCommand command, CancellationToken ct)
    {
        try
        {
            // Handle dev saga refs (format: "RealSagaRef__DEV__uniqueid")
            var sagaRefForLookup = command.SagaArcRef;
            var devSuffix = "__DEV__";
            if (command.SagaArcRef.Contains(devSuffix))
            {
                sagaRefForLookup = command.SagaArcRef.Substring(0, command.SagaArcRef.IndexOf(devSuffix));
            }

            // Validate Saga exists (use stripped ref for template lookup)
            if (!_world.SagaArcLookup.ContainsKey(sagaRefForLookup))
            {
                return SagaCommandResult.Failure(Guid.Empty, $"Saga '{sagaRefForLookup}' not found");
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

            // Get Saga instance (use full ref with DEV suffix for unique instance)
            var instance = await _instanceRepository.GetOrCreateInstanceAsync(command.AvatarId, command.SagaArcRef, ct);

            // Get Saga template and expanded triggers for state replay (use stripped ref for template lookup)
            if (!_world.SagaArcLookup.TryGetValue(sagaRefForLookup, out var sagaTemplate))
            {
                return SagaCommandResult.Failure(instance.InstanceId, $"Saga template '{sagaRefForLookup}' not found");
            }

            if (!_world.SagaTriggersLookup.TryGetValue(sagaRefForLookup, out var expandedTriggers))
            {
                return SagaCommandResult.Failure(instance.InstanceId, $"Triggers not found for Saga '{sagaRefForLookup}'");
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

            // Owner trades for free (depositing/withdrawing from own shopkeeper).
            // Non-owners pay/receive catalog price scaled by the arc's per-direction
            // multiplier: MarkupMultiplier when buying from the arc (shop's selling
            // price), BuybackMultiplier when selling to it (shop's buyback price).
            // Both default to 1.0 — authored arcs carry no premium unless explicitly set.
            var isOwner = !string.IsNullOrEmpty(sagaTemplate.OwnerAvatarId)
                          && command.AvatarId.ToString() == sagaTemplate.OwnerAvatarId;
            var basePrice = command.PricePerItem * command.Quantity;
            int totalPrice;
            if (isOwner)
                totalPrice = 0;
            else if (command.IsBuying)
                totalPrice = (int)Math.Round(basePrice * sagaTemplate.MarkupMultiplier);
            else
                totalPrice = (int)Math.Round(basePrice * sagaTemplate.BuybackMultiplier);

            // Buy-side validation: only non-owners pay and must have the credits to do so.
            // Owners take from their own arc for free, no avatar-inventory check needed —
            // the item lives on the arc's character (saga state), not the avatar.
            if (command.IsBuying && !isOwner)
            {
                var avatarCredits = command.Avatar.Stats?.Credits ?? 0;
                if (avatarCredits < totalPrice)
                {
                    return SagaCommandResult.Failure(instance.InstanceId,
                        $"Insufficient credits: need {totalPrice}, have {avatarCredits}");
                }

                // Carry weight check
                var archetypeRef = command.Avatar.ArchetypeRef;
                if (string.IsNullOrEmpty(archetypeRef) || !_world.AvatarArchetypesLookup.TryGetValue(archetypeRef, out var archetype))
                {
                    return SagaCommandResult.Failure(instance.InstanceId, "Avatar has no valid archetype");
                }

                var categoryWeight = DetermineItemCategoryWeight(command.ItemRef, command.Avatar.Capabilities);
                var additionalWeight = categoryWeight * command.Quantity;
                if (CarryWeightCalculator.WouldExceedCapacity(command.Avatar.Capabilities, archetype, _world.WorldConfiguration, additionalWeight))
                {
                    return SagaCommandResult.Failure(instance.InstanceId, "Too heavy to carry");
                }
            }
            else if (!command.IsBuying)
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
                    [TransactionDataKeys.CharacterInstanceId] = command.CharacterInstanceId.ToString(),
                    [TransactionDataKeys.ItemRef] = command.ItemRef,
                    [TransactionDataKeys.Quantity] = command.Quantity.ToString(),
                    [TransactionDataKeys.IsBuying] = command.IsBuying.ToString(),
                    [TransactionDataKeys.PricePerItem] = command.PricePerItem.ToString(),
                    [TransactionDataKeys.TotalPrice] = totalPrice.ToString()
                }
            };

            instance.AddTransaction(transaction);

            // Persist transaction
            var (sequenceNumbers, committed) = await _instanceRepository.AddAndCommitTransactionsAsync(
                instance.InstanceId,
                new List<SagaTransaction> { transaction },
                ct);

            if (!committed)
            {
                return SagaCommandResult.Failure(instance.InstanceId, "Concurrency conflict - transaction rolled back");
            }

            // Update in-memory transaction status so GetCommittedTransactions() finds it
            transaction.Status = TransactionStatus.Committed;

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
                            [TransactionDataKeys.ReversedTransactionId] = transaction.TransactionId.ToString(),
                            [TransactionDataKeys.Reason] = $"Avatar persistence failed: {persistEx.Message}",
                            [TransactionDataKeys.OriginalType] = transaction.Type.ToString()
                        }
                    };

                    instance.AddTransaction(reversalTransaction);
                    await _instanceRepository.AddAndCommitTransactionsAsync(
                        instance.InstanceId,
                        new List<SagaTransaction> { reversalTransaction },
                        ct);

                    return SagaCommandResult.Failure(
                        instance.InstanceId,
                        $"Trade committed but avatar update failed: {persistEx.Message}");
                }
            }

            var resultData = new Dictionary<string, object>
            {
                [TransactionDataKeys.ItemRef] = command.ItemRef,
                [TransactionDataKeys.Quantity] = command.Quantity,
                [TransactionDataKeys.PricePerItem] = command.PricePerItem,
                [TransactionDataKeys.TotalPrice] = totalPrice,
                [TransactionDataKeys.TransactionType] = command.IsBuying ? "Purchase" : "Sale"
            };

            // If a non-owner bought from an owned arc, signal that the owner should receive revenue
            if (command.IsBuying && !isOwner && !string.IsNullOrEmpty(sagaTemplate.OwnerAvatarId) && totalPrice > 0)
            {
                resultData[TransactionDataKeys.OwnerAvatarId] = sagaTemplate.OwnerAvatarId;
                resultData[TransactionDataKeys.OwnerRevenue] = totalPrice;
            }

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

    private float DetermineItemCategoryWeight(string itemRef, ItemCollection? capabilities)
    {
        var config = _world.WorldConfiguration;

        if (capabilities?.Consumables?.Any(c => c.ConsumableRef == itemRef) == true)
            return config.ConsumableWeight;
        if (capabilities?.Equipment?.Any(e => e.EquipmentRef == itemRef) == true)
            return config.EquipmentWeight;
        if (capabilities?.Tools?.Any(t => t.ToolRef == itemRef) == true)
            return config.ToolWeight;
        if (capabilities?.Spells?.Any(s => s.SpellRef == itemRef) == true)
            return config.SpellWeight;
        if (capabilities?.Blocks?.Any(b => b.BlockRef == itemRef) == true)
            return config.BlockWeight;
        if (capabilities?.BuildingMaterials?.Any(m => m.BuildingMaterialRef == itemRef) == true)
            return config.BuildingMaterialWeight;

        // Check world catalogs for buying new items
        if (_world.ConsumablesLookup.ContainsKey(itemRef)) return config.ConsumableWeight;
        if (_world.EquipmentLookup.ContainsKey(itemRef)) return config.EquipmentWeight;
        if (_world.ToolsLookup.ContainsKey(itemRef)) return config.ToolWeight;
        if (_world.SpellsLookup.ContainsKey(itemRef)) return config.SpellWeight;
        if (_world.BuildingMaterialsLookup.ContainsKey(itemRef)) return config.BuildingMaterialWeight;

        // Default to block weight (most common item type for voxel game)
        return config.BlockWeight;
    }
}
