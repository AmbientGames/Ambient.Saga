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
    /// Merchant markup multiplier for selling this item to avatars.
    /// </summary>
    float MerchantMarkupMultiplier { get; }

    /// <summary>
    /// Optional variant labels for items that come in named variants (a coloured block, a skinned
    /// weapon), indexed by variant number. Null for the vast majority of items, which have none.
    /// This is just data — the item supplies the labels; IWorld.GetItemDisplayName combines the
    /// label with <see cref="DisplayName"/> to form the full name. There is deliberately no
    /// second name getter here; plain items are read through <see cref="DisplayName"/> as always.
    /// </summary>
    System.Collections.Generic.IReadOnlyList<string>? VariantNames => null;
}
