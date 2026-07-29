using Ambient.Domain;
using Ambient.Domain.Contracts;
using Ambient.Domain.Entities;
using Ambient.Domain.GameLogic.Gameplay.Avatar;
using Ambient.Domain.GameLogic.Items;
using Ambient.Rpg.Engine.Application.Commands.Arcs;
using Ambient.Rpg.Engine.Application.ReadModels;
using Ambient.Rpg.Engine.Application.Results.Arcs;
using Ambient.Rpg.Engine.Contracts.Cqrs;
using Ambient.Rpg.Engine.Contracts.Services;
using Ambient.Rpg.Engine.Domain.Arcs.TransactionLog;
using Ambient.Rpg.Engine.Domain.Trade;
using MediatR;
using Ambient.Rpg.Engine.Domain;

namespace Ambient.Rpg.Engine.Application.Handlers.Arcs;

/// <summary>
/// Handler for TradeItemCommand.
/// Creates ItemTraded transaction and updates avatar inventory/credits.
/// </summary>
internal sealed class TradeItemHandler : IRequestHandler<TradeItemCommand, ArcCommandResult>
{
    private readonly IArcInstanceRepository _instanceRepository;
    private readonly IArcReadModelRepository _readModelRepository;
    private readonly IAvatarUpdateService _avatarUpdateService;
    private readonly IWorld _world;

    public TradeItemHandler(
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

    public async Task<ArcCommandResult> Handle(TradeItemCommand command, CancellationToken ct)
    {
        try
        {
            // Handle dev arc refs (format: "RealArcRef__DEV__uniqueid")
            var arcRefForLookup = command.ArcRef;
            var devSuffix = "__DEV__";
            if (command.ArcRef.Contains(devSuffix))
            {
                arcRefForLookup = command.ArcRef.Substring(0, command.ArcRef.IndexOf(devSuffix));
            }

            // Validate Arc exists (use stripped ref for template lookup)
            if (!_world.ArcLookup.ContainsKey(arcRefForLookup))
            {
                return ArcCommandResult.Failure(Guid.Empty, $"Arc '{arcRefForLookup}' not found");
            }

            // Validate quantity
            if (command.Quantity <= 0)
            {
                return ArcCommandResult.Failure(Guid.Empty, "Quantity must be greater than zero");
            }

            // Validate price
            if (command.PricePerItem < 0)
            {
                return ArcCommandResult.Failure(Guid.Empty, $"Invalid price: {command.PricePerItem} (must be non-negative)");
            }

            // Get Arc instance (use full ref with DEV suffix for unique instance)
            var instance = await _instanceRepository.GetOrCreateInstanceAsync(command.AvatarId, command.ArcRef, ct);

            // Get Arc template and expanded triggers for state replay (use stripped ref for template lookup)
            if (!_world.ArcLookup.TryGetValue(arcRefForLookup, out var arcTemplate))
            {
                return ArcCommandResult.Failure(instance.InstanceId, $"Arc template '{arcRefForLookup}' not found");
            }

            if (!_world.ArcTriggersLookup.TryGetValue(arcRefForLookup, out var expandedTriggers))
            {
                return ArcCommandResult.Failure(instance.InstanceId, $"Triggers not found for Arc '{arcRefForLookup}'");
            }

            // Replay to get current state (needed to check if character is alive)
            var stateMachine = new ArcStateMachine(arcTemplate, expandedTriggers, _world);
            var currentState = stateMachine.ReplayToNow(instance);

            // Validate character exists and is alive
            var characterKey = command.CharacterInstanceId.ToString();
            if (!currentState.Characters.TryGetValue(characterKey, out var character))
            {
                return ArcCommandResult.Failure(instance.InstanceId, $"Character '{command.CharacterInstanceId}' not found");
            }

            // Dead characters are not traded with, ever. A battle drop is a BattleLoot
            // remains ARC (see ArcTradeRules) — the old loot-the-corpse path is gone.
            if (!character.IsAlive)
            {
                return ArcCommandResult.Failure(instance.InstanceId, "Cannot trade with defeated character");
            }

            // Owner trades for free (depositing/withdrawing from own shopkeeper).
            // Non-owners pay/receive catalog price scaled by the arc's per-direction
            // multiplier: MarkupMultiplier when buying from the arc (shop's selling
            // price), BuybackMultiplier when selling to it (shop's buyback price).
            // Both default to 1.0 — authored arcs carry no premium unless explicitly set.
            var isOwner = !string.IsNullOrEmpty(arcTemplate.OwnerAvatarId)
                          && command.AvatarId.ToString() == arcTemplate.OwnerAvatarId;

            // Container-arc rules (one table, mirrored by ArcTransactionValidator):
            // free-take kinds are zero-price in BOTH directions — a priced take is a
            // tampered client, a priced deposit would mint credits. Remains accept no
            // deposits at all; battle loot is takeable by its victor (the owner) only.
            var arcKind = arcTemplate.Kind;
            var isFreeTakeArc = ArcTradeRules.IsFreeTakeKind(arcKind);
            if (isFreeTakeArc)
            {
                if (command.PricePerItem != 0)
                {
                    return ArcCommandResult.Failure(instance.InstanceId,
                        $"Trades at a {arcKind} are free — price {command.PricePerItem} is invalid");
                }

                if (command.IsBuying && !ArcTradeRules.MayTake(arcKind, isOwner))
                {
                    return ArcCommandResult.Failure(instance.InstanceId,
                        "Only the victor may take this battle loot");
                }

                if (!command.IsBuying && !ArcTradeRules.AllowsAnyoneDeposits(arcKind))
                {
                    return ArcCommandResult.Failure(instance.InstanceId,
                        "Cannot deposit items into remains");
                }
            }

            var basePrice = command.PricePerItem * command.Quantity;
            int totalPrice;
            if (isOwner || isFreeTakeArc)
                totalPrice = 0;
            else if (command.IsBuying)
                totalPrice = (int)Math.Round(basePrice * arcTemplate.MarkupMultiplier);
            else
                totalPrice = (int)Math.Round(basePrice * arcTemplate.BuybackMultiplier);

            // A Market LISTING overrides catalog pricing: the visitor pays EXACTLY the
            // owner's listed price — no arc multiplier on top (the multiplier is for
            // unlisted stock). Mirrored by ArcTransactionValidator at sync.
            var isListedSale = false;
            if (!isOwner && command.IsBuying && arcKind == ArcTradeRules.MarketKind
                && currentState.ShopPrices.TryGetValue(command.ItemRef, out var listedPrice))
            {
                if (command.PricePerItem != listedPrice)
                {
                    return ArcCommandResult.Failure(instance.InstanceId,
                        $"'{command.ItemRef}' is listed at {listedPrice}, not {command.PricePerItem}");
                }

                isListedSale = true;
                totalPrice = listedPrice * command.Quantity;
            }

            // BaseValue=int.MaxValue is the "cannot be traded" sentinel
            // (Economy.xsd). Selling one used to pay ~2.1 BILLION credits.
            // Free-take arcs are exempt: a drop is a gift, not commerce —
            // untradeable items are still collectable from remains and geocaches.
            var catalogItem = ResolveTradeable(command.ItemRef);
            if (catalogItem != null && catalogItem.BaseValue == int.MaxValue && !isFreeTakeArc)
            {
                return ArcCommandResult.Failure(instance.InstanceId,
                    $"'{command.ItemRef}' cannot be traded");
            }

            if (command.IsBuying)
            {
                // Merchants only sell what they actually stock — check the replayed
                // inventory (degradables replay as one entry per unit, so count entries)
                var stock = character.CurrentInventory;
                var inStock =
                    (stock?.Consumables?.FirstOrDefault(c => c.ConsumableRef == command.ItemRef)?.Quantity ?? 0) >= command.Quantity ||
                    (stock?.BuildingMaterials?.FirstOrDefault(m => m.BuildingMaterialRef == command.ItemRef)?.Quantity ?? 0) >= command.Quantity ||
                    (stock?.Blocks?.FirstOrDefault(b => b.BlockRef == command.ItemRef)?.Quantity ?? 0) >= command.Quantity ||
                    (stock?.Equipment?.Count(e => e.EquipmentRef == command.ItemRef) ?? 0) >= command.Quantity ||
                    (stock?.Tools?.Count(t => t.ToolRef == command.ItemRef) ?? 0) >= command.Quantity ||
                    (stock?.Spells?.Count(s => s.SpellRef == command.ItemRef) ?? 0) >= command.Quantity;
                if (!inStock)
                {
                    return ArcCommandResult.Failure(instance.InstanceId,
                        $"'{command.ItemRef}' is not in stock (quantity: {command.Quantity})");
                }

                // Spells are knowledge — knowing one twice is meaningless, so a second copy
                // is still rejected. Equipment and tools are things: duplicates are honest
                // inventory (crafting produces extras), so buying more is allowed.
                if (_world.SpellsLookup.ContainsKey(command.ItemRef) &&
                    (command.Avatar.Capabilities?.Spells?.Any(s => s.SpellRef == command.ItemRef) ?? false))
                {
                    return ArcCommandResult.Failure(instance.InstanceId,
                        $"Already own '{command.ItemRef}'");
                }
            }

            // Buy-side validation: only non-owners pay and must have the credits to do so.
            // Owners take from their own arc for free, no avatar-inventory check needed —
            // the item lives on the arc's character (arc state), not the avatar.
            if (command.IsBuying && !isOwner)
            {
                var avatarCredits = command.Avatar.Stats?.Credits ?? 0;
                if (avatarCredits < totalPrice)
                {
                    return ArcCommandResult.Failure(instance.InstanceId,
                        $"Insufficient credits: need {totalPrice}, have {avatarCredits}");
                }

                // Carry weight check
                var archetypeRef = command.Avatar.ArchetypeRef;
                if (string.IsNullOrEmpty(archetypeRef) || !_world.AvatarArchetypesLookup.TryGetValue(archetypeRef, out var archetype))
                {
                    return ArcCommandResult.Failure(instance.InstanceId, "Avatar has no valid archetype");
                }

                var categoryWeight = DetermineItemCategoryWeight(command.ItemRef, command.Avatar.Capabilities);
                var additionalWeight = categoryWeight * command.Quantity;
                if (CarryWeightCalculator.WouldExceedCapacity(command.Avatar.Capabilities, archetype, _world.WorldConfiguration, additionalWeight))
                {
                    return ArcCommandResult.Failure(instance.InstanceId, "Too heavy to carry");
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

                // Check equipment/tools/spells — degradables are one inventory entry per
                // ITEM, so a sale of N requires owning N entries of the ref. The sale
                // removes exactly N (crafted extras are honest duplicates); a Quantity
                // beyond the owned count is still rejected below.
                if (!hasItem && command.Avatar.Capabilities?.Equipment != null)
                {
                    hasItem = command.Avatar.Capabilities.Equipment
                        .Count(e => e.EquipmentRef == command.ItemRef) >= command.Quantity;
                    if (hasItem) itemType = "equipment";
                }

                if (!hasItem && command.Avatar.Capabilities?.Tools != null)
                {
                    hasItem = command.Avatar.Capabilities.Tools
                        .Count(t => t.ToolRef == command.ItemRef) >= command.Quantity;
                    if (hasItem) itemType = "tool";
                }

                if (!hasItem && command.Avatar.Capabilities?.Spells != null)
                {
                    hasItem = command.Avatar.Capabilities.Spells
                        .Count(s => s.SpellRef == command.ItemRef) >= command.Quantity;
                    if (hasItem) itemType = "spell";
                }

                // Check blocks — the ref is the exact stack identity
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
                    return ArcCommandResult.Failure(instance.InstanceId,
                        $"Avatar does not have '{command.ItemRef}' (quantity: {command.Quantity}) to sell");
                }
            }

            // Server-side price check: PricePerItem is client-supplied, so recompute
            // the canonical catalog price with the same formula the trade UI uses
            // (TradeEngine + the merchant's replayed traits) and reject mismatches —
            // otherwise a tampered client buys at 0 or sells at any price it likes.
            // Owner trades are free (totalPrice forced to 0 above), items without
            // a catalog entry have no server price to compare against, and free-take
            // arcs are zero-price BY RULE (enforced above).
            if (!isOwner && catalogItem != null && !isFreeTakeArc && !isListedSale)
            {
                var tradeEngine = new TradeEngine(_world);
                currentState.CharacterTraits.TryGetValue(character.CharacterRef, out var merchantTraits);
                // The ref may carry a folded-in variety ("RareIngots#1" = gold) — price that
                // variety, exactly as the trade UI does, or gold trades at iron's price here.
                var variant = ItemRefManager.VariantOf(command.ItemRef);
                var expectedPrice = command.IsBuying
                    ? tradeEngine.CalculateBuyPrice(catalogItem, isMerchant: true, merchantTraits, variant)
                    : tradeEngine.CalculateSellPrice(catalogItem, variant);

                // ±1 credit tolerance absorbs client/server integer-rounding differences
                if (Math.Abs(command.PricePerItem - expectedPrice) > 1)
                {
                    return ArcCommandResult.Failure(instance.InstanceId,
                        $"Price mismatch for '{command.ItemRef}': client offered {command.PricePerItem}, server price is {expectedPrice}");
                }
            }

            // Create ItemTraded transaction
            var transaction = new ArcTransaction
            {
                TransactionId = Guid.NewGuid(),
                Type = ArcTransactionType.ItemTraded,
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

            var tradeTransactions = new List<ArcTransaction> { transaction };

            // Record the credit movement — CurrencyCollected quest objectives read
            // CurrencyChanged transactions (selling = positive gain, buying = cost)
            if (totalPrice != 0)
            {
                var currencyTx = new ArcTransaction
                {
                    TransactionId = Guid.NewGuid(),
                    Type = ArcTransactionType.CurrencyChanged,
                    AvatarId = command.AvatarId.ToString(),
                    Status = TransactionStatus.Pending,
                    LocalTimestamp = DateTime.UtcNow,
                    Data = new Dictionary<string, string>
                    {
                        [TransactionDataKeys.Amount] = (command.IsBuying ? -totalPrice : totalPrice).ToString(),
                        [TransactionDataKeys.Reason] = "Trade",
                        [TransactionDataKeys.ItemRef] = command.ItemRef
                    }
                };
                instance.AddTransaction(currencyTx);
                tradeTransactions.Add(currencyTx);
            }

            // Persist transaction
            var (sequenceNumbers, committed) = await _instanceRepository.AddAndCommitTransactionsAsync(
                instance.InstanceId,
                tradeTransactions,
                ct);

            if (!committed)
            {
                return ArcCommandResult.Failure(instance.InstanceId, "Concurrency conflict - transaction rolled back");
            }

            // Update in-memory transaction status so GetCommittedTransactions() finds it
            transaction.Status = TransactionStatus.Committed;

            // Invalidate cache
            await _readModelRepository.InvalidateCacheAsync(command.AvatarId, command.ArcRef, ct);

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

                    return ArcCommandResult.Failure(
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
            if (command.IsBuying && !isOwner && !string.IsNullOrEmpty(arcTemplate.OwnerAvatarId) && totalPrice > 0)
            {
                resultData[TransactionDataKeys.OwnerAvatarId] = arcTemplate.OwnerAvatarId;
                resultData[TransactionDataKeys.OwnerRevenue] = totalPrice;
            }

            return ArcCommandResult.Success(
                instance.InstanceId,
                new List<Guid> { transaction.TransactionId },
                sequenceNumbers.First(),
                resultData,
                updatedAvatar);
        }
        catch (Exception ex)
        {
            return ArcCommandResult.Failure(Guid.Empty, $"Error trading item: {ex.Message}");
        }
    }

    /// <summary>Resolves an item ref to its catalog entry across the tradeable families.</summary>
    private ITradeable? ResolveTradeable(string itemRef)
        => string.IsNullOrEmpty(itemRef) ? null : _world.TryGetTradeableByRefName(itemRef);

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
