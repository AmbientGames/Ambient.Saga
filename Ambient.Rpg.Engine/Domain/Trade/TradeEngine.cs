using Ambient.Domain;
using Ambient.Domain.Contracts;
using Ambient.Domain.GameLogic.Items;

namespace Ambient.Rpg.Engine.Domain.Trade;

/// <summary>
/// Core trading engine that handles all trade logic between two participants (avatar/merchant, avatar/structure, etc.).
/// Framework-agnostic - can be used by WPF, ImGui, or any UI framework.
/// </summary>
public class TradeEngine
{
    private readonly IWorld _world;

    public TradeEngine(IWorld world)
    {
        _world = world;
    }

    /// <summary>
    /// Calculate the buy price for an item when purchasing from a merchant: what the variant
    /// is worth (its BaseValue) x MerchantMarkupMultiplier (what the shop multiplies
    /// value by when selling).
    /// </summary>
    /// <param name="item">The item being purchased</param>
    /// <param name="isMerchant">Whether the seller is a merchant</param>
    /// <param name="characterTraits">Optional list of traits for the merchant (e.g., "Friendly", "TradeDiscount")</param>
    /// <param name="variant">The item variant being priced (a block variety); 0 for plain items</param>
    public int CalculateBuyPrice(ITradeable item, bool isMerchant, List<string>? characterTraits = null, int variant = 0)
    {
        if (!isMerchant) return 0;

        var basePrice = (int)(item.GetBaseValue(variant) * item.MerchantMarkupMultiplier);

        // Apply trait-based discounts
        if (characterTraits != null && characterTraits.Count > 0)
        {
            var discountMultiplier = 1.0;

            // Friendly trait gives 10% discount
            if (characterTraits.Contains("Friendly"))
            {
                discountMultiplier *= 0.9;
            }

            // TradeDiscount trait gives additional 20% discount
            if (characterTraits.Contains("TradeDiscount"))
            {
                discountMultiplier *= 0.8;
            }

            // Both traits together = 28% discount (0.9 * 0.8 = 0.72)
            basePrice = (int)(basePrice * discountMultiplier);
        }

        return basePrice;
    }

    /// <summary>
    /// Calculate the sell price for an item when selling to a merchant: what the variant is
    /// worth — the trade-in price IS the item's value, so a gold ingot's authored
    /// ValueMultiplier raises what the merchant pays, not just what it charges.
    /// </summary>
    public int CalculateSellPrice(ITradeable item, int variant = 0)
    {
        return item.GetBaseValue(variant);
    }

    /// <summary>
    /// Get all available items from a participant's inventory for a specific category.
    /// </summary>
    /// <param name="characterTraits">Optional list of character traits affecting pricing</param>
    public List<TradeItemInfo> GetAvailableItems(ItemCollection inventory, string category, bool isBuying, List<string>? characterTraits = null)
    {
        var items = new List<TradeItemInfo>();

        switch (category)
        {
            case "Equipment":
                if (inventory.Equipment != null)
                {
                    // Duplicates (same ref AND condition — crafted extras) group into one
                    // row with a quantity; distinct conditions stay separate rows.
                    foreach (var group in inventory.Equipment.GroupBy(e => (e.EquipmentRef, e.Condition)))
                    {
                        var equipItem = _world.Gameplay.Equipment?.FirstOrDefault(e => e.RefName == group.Key.EquipmentRef);
                        if (equipItem != null && equipItem.BaseValue != int.MaxValue) // int.MaxValue = untradeable sentinel
                        {
                            var price = isBuying ? CalculateBuyPrice(equipItem, true, characterTraits) : CalculateSellPrice(equipItem);
                            var count = group.Count();
                            items.Add(new TradeItemInfo(equipItem, price, quantity: count > 1 ? count : null, condition: group.Key.Condition));
                        }
                    }
                }
                break;

            case "Consumables":
                if (inventory.Consumables != null)
                {
                    foreach (var entry in inventory.Consumables)
                    {
                        var consumable = _world.Gameplay.Consumables?.FirstOrDefault(c => c.RefName == entry.ConsumableRef);
                        if (consumable != null && consumable.BaseValue != int.MaxValue) // int.MaxValue = untradeable sentinel
                        {
                            var price = isBuying ? CalculateBuyPrice(consumable, true, characterTraits) : CalculateSellPrice(consumable);
                            items.Add(new TradeItemInfo(consumable, price, quantity: entry.Quantity, condition: null));
                        }
                    }
                }
                break;

            case "Blocks":
                if (inventory.Blocks != null && _world.BlockProvider != null)
                {
                    // One row per distinct block ref — a ref is opaque here, so two refs are just
                    // two tradeable items. The provider resolves the ref to a block definition for
                    // its price and name.
                    foreach (var group in inventory.Blocks.Where(e => e != null && !string.IsNullOrEmpty(e.BlockRef)).GroupBy(e => e.BlockRef))
                    {
                        // Block quantities are floats (saturation supports partial blocks),
                        // but trade is whole-block only — floor and skip anything under 1.
                        var quantity = (int)group.Sum(e => e.Quantity);
                        if (quantity < 1)
                            continue;

                        var block = _world.BlockProvider.GetBlockByRefName(group.Key);
                        if (block != null && block.BaseValue != int.MaxValue) // int.MaxValue = untradeable sentinel
                        {
                            // The ref carries the variety ("RareIngots#1" = gold); price that
                            // variety, not the base block — gold must not sell like iron.
                            var variant = ItemRefManager.VariantOf(group.Key);
                            var price = isBuying ? CalculateBuyPrice(block, true, characterTraits, variant) : CalculateSellPrice(block, variant);
                            items.Add(new TradeItemInfo(block, price, quantity: quantity, condition: null,
                                itemRef: group.Key));
                        }
                    }
                }
                break;

            case "Tools":
                if (inventory.Tools != null)
                {
                    // Same duplicate-grouping rule as Equipment.
                    foreach (var group in inventory.Tools.GroupBy(t => (t.ToolRef, t.Condition)))
                    {
                        var tool = _world.Gameplay.Tools?.FirstOrDefault(t => t.RefName == group.Key.ToolRef);
                        if (tool != null && tool.BaseValue != int.MaxValue) // int.MaxValue = untradeable sentinel
                        {
                            var price = isBuying ? CalculateBuyPrice(tool, true, characterTraits) : CalculateSellPrice(tool);
                            var count = group.Count();
                            items.Add(new TradeItemInfo(tool, price, quantity: count > 1 ? count : null, condition: group.Key.Condition));
                        }
                    }
                }
                break;

            case "Spells":
                if (inventory.Spells != null)
                {
                    foreach (var entry in inventory.Spells)
                    {
                        var spell = _world.Gameplay.Spells?.FirstOrDefault(s => s.RefName == entry.SpellRef);
                        if (spell != null && spell.BaseValue != int.MaxValue) // int.MaxValue = untradeable sentinel
                        {
                            var price = isBuying ? CalculateBuyPrice(spell, true, characterTraits) : CalculateSellPrice(spell);
                            items.Add(new TradeItemInfo(spell, price, quantity: null, condition: (float)entry.Condition));
                        }
                    }
                }
                break;
        }

        return items;
    }

    /// <summary>
    /// Get count of items in a specific category.
    /// </summary>
    public int GetCategoryItemCount(ItemCollection? inventory, string category)
    {
        if (inventory == null) return 0;

        return category switch
        {
            "Equipment" => inventory.Equipment?.Length ?? 0,
            "Consumables" => inventory.Consumables?.Length ?? 0,
            // Match the floor-and-filter rule applied in GetAvailableItems so the category
            // selector hides Blocks when every entry is a fractional partial block.
            "Blocks" => inventory.Blocks?.Count(b => b != null && (int)b.Quantity >= 1) ?? 0,
            "Tools" => inventory.Tools?.Length ?? 0,
            "Spells" => inventory.Spells?.Length ?? 0,
            _ => 0
        };
    }

    /// <summary>
    /// Execute a buy transaction (buyer purchases from seller).
    /// </summary>
    public TradeResult BuyItem(AvatarBase buyer, ItemCollection seller, TradeItemInfo item)
    {
        if (buyer.Stats == null || buyer.Capabilities == null)
            return TradeResult.Failed("Missing buyer data");

        // Check if buyer has enough credits
        if (buyer.Stats.Credits < item.Price)
            return TradeResult.Failed($"Not enough money! Need {item.Price}, have {buyer.Stats.Credits:F0}");

        // Transfer item from seller to buyer
        var transferResult = TransferItem(seller, buyer.Capabilities, item, fromSeller: true);
        if (!transferResult.Success)
            return transferResult;

        // Deduct money from buyer
        buyer.Stats.Credits -= item.Price;

        return TradeResult.Succeeded($"Bought {item.Item.DisplayName} for {item.Price}");
    }

    /// <summary>
    /// Execute a sell transaction (seller sells to buyer).
    /// </summary>
    public TradeResult SellItem(AvatarBase seller, ItemCollection buyer, TradeItemInfo item)
    {
        if (seller.Stats == null || seller.Capabilities == null)
            return TradeResult.Failed("Missing seller data");

        // Transfer item from seller to buyer
        var transferResult = TransferItem(seller.Capabilities, buyer, item, fromSeller: true);
        if (!transferResult.Success)
            return transferResult;

        // Add money to seller
        seller.Stats.Credits += item.Price;

        return TradeResult.Succeeded($"Sold {item.Item.DisplayName} for {item.Price}");
    }

    private TradeResult TransferItem(ItemCollection source, ItemCollection dest, TradeItemInfo item, bool fromSeller)
    {
        return item.Item switch
        {
            Equipment equipment => TransferEquipment(source, dest, equipment.RefName, item.Condition, item.Quantity ?? 1),
            Consumable consumable => TransferConsumable(source, dest, consumable.RefName, item.Quantity ?? 1),
            IBlock => TransferBlock(source, dest, item.ItemRef, item.Quantity ?? 1),
            Tool tool => TransferTool(source, dest, tool.RefName, item.Condition, item.Quantity ?? 1),
            Spell spell => TransferSpell(source, dest, spell.RefName, item.Condition),
            _ => TradeResult.Failed("Unknown item type")
        };
    }

    private TradeResult TransferEquipment(ItemCollection source, ItemCollection dest, string refName, float? condition, int quantity = 1)
    {
        // Move exactly quantity entries — duplicates are honest inventory (crafted extras)
        var sourceList = source.Equipment?.ToList() ?? new List<EquipmentEntry>();
        var destList = dest.Equipment?.ToList() ?? new List<EquipmentEntry>();
        for (var moved = 0; moved < quantity; moved++)
        {
            var sourceItem = sourceList.FirstOrDefault(s => s.EquipmentRef == refName && (condition == null || s.Condition == condition));
            if (sourceItem == null)
            {
                if (moved == 0) return TradeResult.Failed("Item not found in source inventory");
                break;
            }

            sourceList.Remove(sourceItem);
            destList.Add(new EquipmentEntry { EquipmentRef = refName, Condition = condition ?? sourceItem.Condition });
        }

        source.Equipment = sourceList.ToArray();
        dest.Equipment = destList.ToArray();

        return TradeResult.Succeeded("Transfer complete");
    }

    private TradeResult TransferConsumable(ItemCollection source, ItemCollection dest, string refName, int quantity)
    {
        // Find and reduce/remove from source
        var sourceList = source.Consumables?.ToList() ?? new List<ConsumableEntry>();
        var sourceStack = sourceList.FirstOrDefault(s => s.ConsumableRef == refName);
        if (sourceStack == null || sourceStack.Quantity < quantity)
            return TradeResult.Failed("Insufficient quantity in source inventory");

        sourceStack.Quantity -= quantity;
        if (sourceStack.Quantity <= 0)
            sourceList.Remove(sourceStack);
        source.Consumables = sourceList.ToArray();

        // Add to destination
        var destList = dest.Consumables?.ToList() ?? new List<ConsumableEntry>();
        var destStack = destList.FirstOrDefault(s => s.ConsumableRef == refName);
        if (destStack != null)
            destStack.Quantity += quantity;
        else
            destList.Add(new ConsumableEntry { ConsumableRef = refName, Quantity = quantity });
        dest.Consumables = destList.ToArray();

        return TradeResult.Succeeded("Transfer complete");
    }

    private TradeResult TransferBlock(ItemCollection source, ItemCollection dest, string blockRef, int quantity)
    {
        // A block ref is the whole stack identity, so a transfer moves exactly that stack.
        var sourceList = source.Blocks?.ToList() ?? new List<BlockEntry>();
        var available = sourceList.Where(s => s.BlockRef == blockRef).Sum(s => s.Quantity);
        if (available < quantity)
            return TradeResult.Failed("Insufficient quantity in source inventory");

        var destList = dest.Blocks?.ToList() ?? new List<BlockEntry>();
        float remaining = quantity;
        foreach (var sourceStack in sourceList.Where(s => s.BlockRef == blockRef).ToList())
        {
            if (remaining <= 0)
                break;

            var take = Math.Min(sourceStack.Quantity, remaining);
            sourceStack.Quantity -= take;
            remaining -= take;
            if (sourceStack.Quantity <= 0)
                sourceList.Remove(sourceStack);

            var destStack = destList.FirstOrDefault(s => s.BlockRef == blockRef);
            if (destStack != null)
                destStack.Quantity += take;
            else
                destList.Add(new BlockEntry { BlockRef = blockRef, Quantity = take });
        }

        source.Blocks = sourceList.ToArray();
        dest.Blocks = destList.ToArray();

        return TradeResult.Succeeded("Transfer complete");
    }

    private TradeResult TransferTool(ItemCollection source, ItemCollection dest, string refName, float? condition, int quantity = 1)
    {
        // Move exactly quantity entries — duplicates are honest inventory (crafted extras)
        var sourceList = source.Tools?.ToList() ?? new List<ToolEntry>();
        var destList = dest.Tools?.ToList() ?? new List<ToolEntry>();
        for (var moved = 0; moved < quantity; moved++)
        {
            var sourceItem = sourceList.FirstOrDefault(s => s.ToolRef == refName && (condition == null || s.Condition == condition));
            if (sourceItem == null)
            {
                if (moved == 0) return TradeResult.Failed("Item not found in source inventory");
                break;
            }

            sourceList.Remove(sourceItem);
            destList.Add(new ToolEntry { ToolRef = refName, Condition = condition ?? sourceItem.Condition });
        }

        source.Tools = sourceList.ToArray();
        dest.Tools = destList.ToArray();

        return TradeResult.Succeeded("Transfer complete");
    }

    private TradeResult TransferSpell(ItemCollection source, ItemCollection dest, string refName, float? condition)
    {
        // Find and remove from source
        var sourceList = source.Spells?.ToList() ?? new List<SpellEntry>();
        var sourceItem = sourceList.FirstOrDefault(s => s.SpellRef == refName);
        if (sourceItem == null) return TradeResult.Failed("Item not found in source inventory");

        sourceList.Remove(sourceItem);
        source.Spells = sourceList.ToArray();

        // Add to destination
        var destList = dest.Spells?.ToList() ?? new List<SpellEntry>();
        destList.Add(new SpellEntry { SpellRef = refName, Condition = condition ?? sourceItem.Condition });
        dest.Spells = destList.ToArray();

        return TradeResult.Succeeded("Transfer complete");
    }
}
