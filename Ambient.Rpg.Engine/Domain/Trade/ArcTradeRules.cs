namespace Ambient.Rpg.Engine.Domain.Trade;

/// <summary>
/// The per-kind trade rules for container arcs placed in the world — one table, applied
/// identically by <c>TradeItemHandler</c> (the local command path) and
/// <c>ArcTransactionValidator</c> (the server sync path). A container arc is a Market shop,
/// a geocache, a death drop (RemnantLoot), or a battle drop (BattleLoot); they all open
/// through the same trade modal and differ ONLY in these rules:
///
///   Market      — visitors pay (catalog price x the arc's multipliers); the owner moves
///                 stock for free. Deposits (stocking) are the owner's zero-price sells.
///   GeoCache    — anyone takes for free; anyone deposits for free. Never any money.
///   RemnantLoot — a death drop: anyone takes for free; nobody deposits.
///   BattleLoot  — a battle drop: only the victor (the arc's owner) takes; nobody deposits.
///
/// Free-take arcs accept ONLY zero-price transactions in both directions — a priced "sell"
/// into a geocache would mint credits from nothing, and a priced "take" is a tampered
/// client. Untradeable items (the int.MaxValue sentinel) are still collectable from
/// free-take arcs: a drop is a gift, not commerce.
/// </summary>
public static class ArcTradeRules
{
    public const string MarketKind = "Market";
    public const string GeoCacheKind = "GeoCache";
    public const string RemnantLootKind = "RemnantLoot";
    public const string BattleLootKind = "BattleLoot";

    /// <summary>
    /// Kinds where taking is free for whoever is allowed to take at all. Everything about
    /// pricing (bounds, markup, credits) is skipped for these; the anti-mint line is that
    /// the arc must actually HOLD the item (stock check against replayed arc state).
    /// </summary>
    public static bool IsFreeTakeKind(string? kind) =>
        kind is GeoCacheKind or RemnantLootKind or BattleLootKind;

    /// <summary>
    /// Kinds that accept deposits from anyone (zero-price sells INTO the arc). Only
    /// geocaches — leaving things is the game. Remains of either kind accept nothing.
    /// </summary>
    public static bool AllowsAnyoneDeposits(string? kind) => kind is GeoCacheKind;

    /// <summary>
    /// Whether a take from a free-take arc is allowed for this trader. BattleLoot is
    /// victor-only (the arc's owner); the other free-take kinds are open to everyone.
    /// </summary>
    public static bool MayTake(string? kind, bool isOwner) =>
        kind != BattleLootKind || isOwner;
}
