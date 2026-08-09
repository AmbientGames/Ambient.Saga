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
    /// What the item is worth in credits — the price a merchant pays the player for it.
    /// </summary>
    int BaseValue { get; }

    /// <summary>
    /// What a shop multiplies the item's value by when selling it: buy price =
    /// <see cref="BaseValue"/> x this; trading in still pays plain BaseValue.
    /// Example: a sword worth 100 with multiplier 2 sells in the shop for 200. Never a way
    /// to express what the item is WORTH — that is <see cref="BaseValue"/>. Normal is
    /// 2 game-wide; location pricing is the shop's decision (the arc's multipliers).
    /// </summary>
    float MerchantMarkupMultiplier { get; }

    /// <summary>
    /// What a specific variant of this item is worth. A block variety can author a
    /// ValueMultiplier (gold ingot 3x the base metal price); worth applies on BOTH sides
    /// of the counter — the merchant pays more for gold AND charges more for it. Plain items
    /// (and varieties without an authored premium) are worth <see cref="BaseValue"/>.
    /// </summary>
    int GetBaseValue(int variant) => BaseValue;

    /// <summary>
    /// Optional variant labels for items that come in named variants (a coloured block, a skinned
    /// weapon), indexed by variant number. Null for the vast majority of items, which have none.
    /// This is just data — the item supplies the labels; IWorld.GetItemDisplayName combines the
    /// label with <see cref="DisplayName"/> to form the full name. There is deliberately no
    /// second name getter here; plain items are read through <see cref="DisplayName"/> as always.
    /// </summary>
    System.Collections.Generic.IReadOnlyList<string>? VariantNames => null;
}
