namespace Ambient.Domain.Contracts;

/// <summary>
/// Interface for items that can be bought and sold in the trading system.
/// </summary>
public interface ITradeable
{
    /// <summary>
    /// Unique reference name for the item.
    /// </summary>
    string RefName { get; }

    /// <summary>
    /// Human-readable display name for the item.
    /// </summary>
    string DisplayName { get; }

    /// <summary>
    /// Base wholesale price of the item.
    /// </summary>
    int WholesalePrice { get; }

    /// <summary>
    /// Merchant markup multiplier for selling this item to players.
    /// </summary>
    float MerchantMarkupMultiplier { get; }
}
