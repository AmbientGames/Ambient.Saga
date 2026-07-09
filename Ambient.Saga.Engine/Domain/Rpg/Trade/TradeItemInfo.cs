using Ambient.Domain.Contracts;

namespace Ambient.Saga.Engine.Domain.Rpg.Trade;

/// <summary>
/// Represents an item available for trade with its calculated price and metadata.
/// Framework-agnostic version of TradeItem (no ObservableCollection dependencies).
/// </summary>
public class TradeItemInfo
{
    /// <summary>
    /// The actual tradeable item.
    /// </summary>
    public ITradeable Item { get; }

    /// <summary>
    /// Calculated price for this transaction (buy or sell).
    /// </summary>
    public int Price { get; }

    /// <summary>
    /// Quantity for stackable items (Consumables, Blocks). Null for non-stackable.
    /// </summary>
    public int? Quantity { get; }

    /// <summary>
    /// Condition for durable items (Equipment, Tools, Spells). Null for non-durable.
    /// </summary>
    public float? Condition { get; }

    /// <summary>
    /// The trade identity. For blocks this is the combined ref (variation folded in), so each
    /// variation trades as a distinct item; equals <see cref="Item"/>.RefName for everything else.
    /// </summary>
    public string ItemRef { get; }

    /// <summary>
    /// The label to show. Variation-specific for blocks (e.g. the colour name); equals
    /// <see cref="Item"/>.DisplayName otherwise.
    /// </summary>
    public string DisplayName { get; }

    public TradeItemInfo(ITradeable item, int price, int? quantity = null, float? condition = null,
        string? itemRef = null, string? displayName = null)
    {
        Item = item;
        Price = price;
        Quantity = quantity;
        Condition = condition;
        ItemRef = itemRef ?? item.RefName;
        DisplayName = displayName ?? item.DisplayName;
    }
}
