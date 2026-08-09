using Ambient.Domain.Contracts;

namespace Ambient.Rpg.Engine.Domain.ValueObjects;

/// <summary>
/// Represents an item available for trade with its calculated price and metadata.
/// </summary>
public class TradeItem
{
    /// <summary>
    /// The actual tradeable item.
    /// </summary>
    public ITradeable Item { get; set; }

    /// <summary>
    /// Calculated price for this transaction (buy or sell).
    /// </summary>
    public int Price { get; set; }

    /// <summary>
    /// Quantity for stackable items (Consumables, Blocks). Null for non-stackable.
    /// </summary>
    public int? Quantity { get; set; }

    /// <summary>
    /// Condition for durable items (Equipment, Tools). Null for non-durable.
    /// </summary>
    public float? Condition { get; set; }

    /// <summary>
    /// The exact inventory ref this row trades. Usually equals <see cref="Item"/>.RefName, but
    /// the provider may resolve several refs to one definition, so the transfer uses this ref.
    /// </summary>
    public string ItemRef { get; set; }

    public TradeItem(ITradeable item, int price, int? quantity = null, float? condition = null,
        string? itemRef = null)
    {
        Item = item;
        Price = price;
        Quantity = quantity;
        Condition = condition;
        ItemRef = itemRef ?? item.RefName;
    }
}
