using Ambient.Domain.Contracts;

namespace Ambient.SagaEngine.Domain.Rpg.Trade;

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

    public TradeItemInfo(ITradeable item, int price, int? quantity = null, float? condition = null)
    {
        Item = item;
        Price = price;
        Quantity = quantity;
        Condition = condition;
    }
}
