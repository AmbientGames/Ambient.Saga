using Ambient.Domain;
using Ambient.Domain.Contracts;
using Ambient.Domain.DefinitionExtensions;

namespace Ambient.Saga.Engine.Domain.Rpg.Trade;

/// <summary>
/// Core trading engine that handles all trade logic between two participants (player/merchant, player/structure, etc.).
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
    /// Calculate the buy price for an item when purchasing from a merchant.
    /// </summary>
    /// <param name="item">The item being purchased</param>
    /// <param name="isMerchant">Whether the seller is a merchant</param>
    /// <param name="characterTraits">Optional list of traits for the merchant (e.g., "Friendly", "TradeDiscount")</param>
    public int CalculateBuyPrice(ITradeable item, bool isMerchant, List<string>? characterTraits = null)
    {
        if (!isMerchant) return 0;

        var basePrice = (int)(item.WholesalePrice * item.MerchantMarkupMultiplier);

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
    /// Calculate the sell price for an item when selling to a merchant.
    /// </summary>
    public int CalculateSellPrice(ITradeable item)
    {
        return item.WholesalePrice;
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
                    foreach (var entry in inventory.Equipment)
                    {
                        var equipItem = _world.Gameplay.Equipment?.FirstOrDefault(e => e.RefName == entry.EquipmentRef);
                        if (equipItem != null)
                        {
                            var price = isBuying ? CalculateBuyPrice(equipItem, true, characterTraits) : CalculateSellPrice(equipItem);
                            items.Add(new TradeItemInfo(equipItem, price, quantity: null, condition: entry.Condition));
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
                        if (consumable != null)
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
                    foreach (var entry in inventory.Blocks)
                    {
                        if (entry == null || string.IsNullOrEmpty(entry.BlockRef))
                            continue;

                        var block = _world.BlockProvider.GetBlockByRef(entry.BlockRef);
                        if (block != null)
                        {
                            var price = isBuying ? CalculateBuyPrice(block, true, characterTraits) : CalculateSellPrice(block);
                            items.Add(new TradeItemInfo(block, price, quantity: entry.Quantity, condition: null));
                        }
                    }
                }
                break;

            case "Tools":
                if (inventory.Tools != null)
                {
                    foreach (var entry in inventory.Tools)
                    {
                        var tool = _world.Gameplay.Tools?.FirstOrDefault(t => t.RefName == entry.ToolRef);
                        if (tool != null)
                        {
                            var price = isBuying ? CalculateBuyPrice(tool, true, characterTraits) : CalculateSellPrice(tool);
                            items.Add(new TradeItemInfo(tool, price, quantity: null, condition: entry.Condition));
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
                        if (spell != null)
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
            "Blocks" => inventory.Blocks?.Length ?? 0,
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
            Equipment equipment => TransferEquipment(source, dest, equipment.RefName, item.Condition),
            Consumable consumable => TransferConsumable(source, dest, consumable.RefName, item.Quantity ?? 1),
            IBlock block => TransferBlock(source, dest, block.RefName, item.Quantity ?? 1),
            Tool tool => TransferTool(source, dest, tool.RefName, item.Condition),
            Spell spell => TransferSpell(source, dest, spell.RefName, item.Condition),
            _ => TradeResult.Failed("Unknown item type")
        };
    }

    private TradeResult TransferEquipment(ItemCollection source, ItemCollection dest, string refName, float? condition)
    {
        // Find and remove from source
        var sourceList = source.Equipment?.ToList() ?? new List<EquipmentEntry>();
        var sourceItem = sourceList.FirstOrDefault(s => s.EquipmentRef == refName);
        if (sourceItem == null) return TradeResult.Failed("Item not found in source inventory");

        sourceList.Remove(sourceItem);
        source.Equipment = sourceList.ToArray();

        // Add to destination
        var destList = dest.Equipment?.ToList() ?? new List<EquipmentEntry>();
        destList.Add(new EquipmentEntry { EquipmentRef = refName, Condition = condition ?? sourceItem.Condition });
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

    private TradeResult TransferBlock(ItemCollection source, ItemCollection dest, string refName, int quantity)
    {
        // Find and reduce/remove from source
        var sourceList = source.Blocks?.ToList() ?? new List<BlockEntry>();
        var sourceStack = sourceList.FirstOrDefault(s => s.BlockRef == refName);
        if (sourceStack == null || sourceStack.Quantity < quantity)
            return TradeResult.Failed("Insufficient quantity in source inventory");

        sourceStack.Quantity -= quantity;
        if (sourceStack.Quantity <= 0)
            sourceList.Remove(sourceStack);
        source.Blocks = sourceList.ToArray();

        // Add to destination
        var destList = dest.Blocks?.ToList() ?? new List<BlockEntry>();
        var destStack = destList.FirstOrDefault(s => s.BlockRef == refName);
        if (destStack != null)
            destStack.Quantity += quantity;
        else
            destList.Add(new BlockEntry { BlockRef = refName, Quantity = quantity });
        dest.Blocks = destList.ToArray();

        return TradeResult.Succeeded("Transfer complete");
    }

    private TradeResult TransferTool(ItemCollection source, ItemCollection dest, string refName, float? condition)
    {
        // Find and remove from source
        var sourceList = source.Tools?.ToList() ?? new List<ToolEntry>();
        var sourceItem = sourceList.FirstOrDefault(s => s.ToolRef == refName);
        if (sourceItem == null) return TradeResult.Failed("Item not found in source inventory");

        sourceList.Remove(sourceItem);
        source.Tools = sourceList.ToArray();

        // Add to destination
        var destList = dest.Tools?.ToList() ?? new List<ToolEntry>();
        destList.Add(new ToolEntry { ToolRef = refName, Condition = condition ?? sourceItem.Condition });
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
