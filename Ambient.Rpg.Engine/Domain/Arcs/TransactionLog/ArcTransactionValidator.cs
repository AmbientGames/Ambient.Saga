using Ambient.Domain;
using Ambient.Domain.Contracts;
using Ambient.Domain.GameLogic.Items;
using Ambient.Rpg.Engine.Domain;
using Ambient.Rpg.Engine.Domain.Trade;
namespace Ambient.Rpg.Engine.Domain.Arcs.TransactionLog;

/// <summary>
/// The container-arc facts the sync endpoint knows about the arc a transaction batch
/// belongs to: its Kind (Market / GeoCache / RemnantLoot / BattleLoot / null for authored
/// quest arcs) and its owner. Lets <see cref="ArcTransactionValidator"/> apply the
/// <see cref="ArcTradeRules"/> table server-side — without it the validator would price
/// free-take arcs like merchants and reject every legitimate take.
/// </summary>
public sealed record ArcTradeContext(string? Kind, string? OwnerAvatarId);

/// <summary>
/// Validates incoming transactions against the current derived ArcState.
/// Used server-side to reject fraudulent or inconsistent transactions before persisting.
/// Each rule mirrors the precondition checks from the corresponding client-side handler.
/// Default-deny: a transaction type is only accepted if it has a legitimate
/// client-side producer — everything else is rejected.
/// </summary>
public static class ArcTransactionValidator
{
    // A single hit can't plausibly exceed this on normalized 0–1 stats (weapon
    // Str×2.5 with stance and vulnerability tops out well below it). Blocks
    // fabricated damage values, not legitimate play.
    private const float MaxPlausibleDamage = 10.0f;

    // Types the client legitimately emits but that need no state precondition.
    // A type absent from this set AND from the switch below is rejected —
    // add it here when a client-side producer ships.
    private static readonly HashSet<ArcTransactionType> AllowedClientTypes = new()
    {
        ArcTransactionType.ArcDiscovered,
        ArcTransactionType.TriggerActivated,
        ArcTransactionType.TriggerCompleted,
        ArcTransactionType.CharacterSpawned,
        ArcTransactionType.BattleStarted,
        ArcTransactionType.BattleEnded,
        ArcTransactionType.AvatarEntered,
        ArcTransactionType.AvatarExited,
        ArcTransactionType.EntityInteracted,
        ArcTransactionType.DialogueStarted,
        ArcTransactionType.DialogueNodeVisited,
        ArcTransactionType.DialogueCompleted,
        ArcTransactionType.TraitAssigned,
        ArcTransactionType.TraitRemoved,
        ArcTransactionType.PartyMemberJoined,
        ArcTransactionType.PartyMemberLeft,
        ArcTransactionType.AffinityGranted,
        ArcTransactionType.EquipmentChanged,
        ArcTransactionType.ConsumableUsed,
        ArcTransactionType.ToolSharpened,
        ArcTransactionType.AvatarTeleported,
        ArcTransactionType.EffectApplied,
    };

    /// <summary>
    /// Validates a transaction against the current state.
    /// Returns (true, null) if valid, (false, reason) if invalid.
    /// Pass the world to additionally bound trade prices against the catalog, and the
    /// arc context (kind + owner) so container arcs get their per-kind trade rules.
    /// </summary>
    public static (bool IsValid, string? Reason) Validate(ArcState state, ArcTransaction transaction, IWorld? world = null, ArcTradeContext? arc = null)
    {
        return transaction.Type switch
        {
            // Character lifecycle
            ArcTransactionType.CharacterDamaged => ValidateCharacterDamaged(state, transaction),
            ArcTransactionType.CharacterDefeated => ValidateCharacterDefeated(state, transaction),
            ArcTransactionType.CharacterHealed => ValidateCharacterAlive(state, transaction, "heal"),
            ArcTransactionType.CharacterDespawned => ValidateCharacterExists(state, transaction),

            // Quest lifecycle
            ArcTransactionType.QuestAccepted => ValidateQuestAccepted(state, transaction),
            ArcTransactionType.QuestCompleted => ValidateQuestCompleted(state, transaction),
            ArcTransactionType.QuestAbandoned => ValidateQuestActive(state, transaction),
            ArcTransactionType.QuestStageAdvanced => ValidateQuestStageAdvanced(state, transaction, world),
            ArcTransactionType.QuestBranchChosen => ValidateQuestBranchChosen(state, transaction),
            ArcTransactionType.QuestObjectiveCompleted => ValidateQuestActive(state, transaction),

            // Trading
            ArcTransactionType.ItemTraded => ValidateItemTraded(state, transaction, world, arc),
            ArcTransactionType.ShopPriceSet => ValidateShopPriceSet(transaction, world, arc),

            // Currency ledger marker read by CurrencyCollected quest objectives. Has live
            // client producers (trade + dialogue rewards); default-denying it rejected every
            // priced trade/reward on sync and regressed quest progress.
            ArcTransactionType.CurrencyChanged => ValidateCurrencyChanged(transaction),

            // Combat results are client-reported — bound them to plausible values
            ArcTransactionType.BattleTurnExecuted => ValidateBattleTurn(transaction),

            // Reputation changes come from dialogue actions (incl. spillover) —
            // require a faction and bound the per-transaction amount
            ArcTransactionType.ReputationChanged => ValidateReputationChanged(transaction),

            // Quest tokens are the progression currency — at minimum the token must
            // exist in the world catalog (previously accepted with zero validation)
            ArcTransactionType.QuestTokenAwarded => ValidateQuestTokenAwarded(transaction, world),

            // Arc lifecycle
            ArcTransactionType.ArcCompleted => ValidateArcCompleted(state),

            // Server-generated only: a client-supplied snapshot would replace the
            // entire replayed state and propagate to every peer of the arc
            ArcTransactionType.StateSnapshot => (false, "StateSnapshot is server-generated and not accepted from clients"),

            // Extension claims must at least identify themselves. Unparseable client
            // type names also land here (DtoToDomainTransaction falls back to
            // Extension) with no ExtensionTypeName — rejected.
            ArcTransactionType.Extension => string.IsNullOrEmpty(transaction.ExtensionTypeName)
                ? (false, "Extension transaction missing ExtensionTypeName")
                : (true, null),

            // Everything else: allowed only with a known client-side producer
            _ => AllowedClientTypes.Contains(transaction.Type)
                ? (true, null)
                : (false, $"Transaction type '{transaction.Type}' has no client-side producer and is not accepted")
        };
    }

    private static (bool, string?) ValidateItemTraded(ArcState state, ArcTransaction tx, IWorld? world, ArcTradeContext? arc = null)
    {
        var characterId = tx.GetData<string>(TransactionDataKeys.CharacterInstanceId);
        if (string.IsNullOrEmpty(characterId))
            return (false, "Missing CharacterInstanceId");

        if (!state.Characters.TryGetValue(characterId, out var tradeCharacter))
            return (false, $"Character '{characterId}' not found");

        // Dead characters are not traded with, ever. A battle drop is a BattleLoot
        // remains ARC (per-kind rules below) — the old loot-the-corpse path is gone.
        if (!tradeCharacter.IsAlive)
            return (false, $"Cannot trade with dead character '{characterId}'");

        if (tx.TryGetData<int>(TransactionDataKeys.Quantity, out var quantity) && quantity <= 0)
            return (false, "Trade quantity must be greater than zero");

        // Container-arc rules (mirrors TradeItemHandler; one table in ArcTradeRules):
        // free-take kinds are zero-price in BOTH directions, remains accept no deposits,
        // battle loot is victor(owner)-only, and the arc's owner always moves own stock
        // free (a Market owner restocking). All of these are exempt from the price
        // bounds below — they are zero-price by rule, not by negotiation.
        var isBuying = tx.TryGetData<bool>(TransactionDataKeys.IsBuying, out var buying) && buying;
        var hasPrice = tx.TryGetData<int>(TransactionDataKeys.PricePerItem, out var price);
        var isArcOwner = !string.IsNullOrEmpty(arc?.OwnerAvatarId)
                         && string.Equals(arc!.OwnerAvatarId, tx.AvatarId, StringComparison.OrdinalIgnoreCase);

        if (arc != null && ArcTradeRules.IsFreeTakeKind(arc.Kind))
        {
            if (!hasPrice || price != 0)
                return (false, $"Trades at a {arc.Kind} are free — a priced trade is invalid");

            if (isBuying && !ArcTradeRules.MayTake(arc.Kind, isArcOwner))
                return (false, "Only the victor may take this battle loot");

            if (!isBuying && !ArcTradeRules.AllowsAnyoneDeposits(arc.Kind))
                return (false, "Cannot deposit items into remains");

            return (true, null);
        }

        if (isArcOwner && hasPrice && price == 0)
        {
            // The arc's owner moving stock in/out of their own shop — free by design.
            return (true, null);
        }

        // A LISTED item trades at exactly its listing — the shop owner's per-item price
        // (bread dear in the mountains) replaces the catalog bounds for that item.
        if (arc?.Kind == ArcTradeRules.MarketKind && isBuying)
        {
            var listedRef = tx.GetData<string>(TransactionDataKeys.ItemRef);
            if (!string.IsNullOrEmpty(listedRef) && state.ShopPrices.TryGetValue(listedRef, out var listing))
            {
                return hasPrice && price == listing
                    ? (true, null)
                    : (false, $"'{listedRef}' is listed at {listing} — offered {(hasPrice ? price.ToString() : "no price")}");
            }
        }

        // Degradable batch sales are legal now — crafting produces honest duplicates, and
        // the trade apply removes exactly the sold count. Payout stays bounded by the
        // per-unit price checks below; quantity honesty carries the same bounded exposure
        // consumable sales always had.

        // Bound the client-supplied price against the catalog: the floor is the
        // maximum-discount buy price, the ceiling for sales is the full BaseValue.
        // Uses TradeEngine so pricing lives in exactly one place. The key is
        // required — omitting it was an opt-out from the bound.
        if (world != null)
        {
            if (!tx.TryGetData<int>(TransactionDataKeys.PricePerItem, out var pricePerItem))
                return (false, "ItemTraded requires PricePerItem");

            var itemRef = tx.GetData<string>(TransactionDataKeys.ItemRef);
            var item = ResolveTradeable(world, itemRef);
            if (item == null)
            {
                // Every real tradeable — equipment, consumable, tool, spell, material, block —
                // resolves via ResolveTradeable. A priced BUY of a ref that resolves to nothing
                // was the owner-revenue mint bypass: a bogus ItemRef skipped all price checks
                // while the server still credited the arc owner. Zero-price takes are harmless
                // (owner is credited 0), so only a priced buy is rejected.
                if (isBuying && pricePerItem > 0)
                    return (false, $"'{itemRef}' is not a priced tradeable item");
            }
            else
            {
                if (item.BaseValue == int.MaxValue)
                    return (false, $"'{itemRef}' is not tradeable");

                // The ref may carry a folded-in variety ("RareIngots#1" = gold) — bound against
                // that variety's value, exactly as TradeEngine prices it for the client.
                var variant = ItemRefManager.VariantOf(itemRef);
                var tradeEngine = new TradeEngine(world);
                if (isBuying)
                {
                    var floor = tradeEngine.CalculateBuyPrice(item, isMerchant: true,
                        characterTraits: new List<string> { "Friendly", "TradeDiscount" }, variant: variant);
                    if (pricePerItem < floor)
                        return (false, $"Price {pricePerItem} for '{itemRef}' is below the catalog minimum {floor}");

                    // Ceiling: shops may mark up steeply — cross-map arbitrage (buy in Jiri,
                    // sell in Gorak Shep, a 5+ day walk) is a legitimate playstyle — but not
                    // without bound, or an unbounded buy price mints owner revenue. Cap at 10x
                    // the standard (undiscounted) merchant price.
                    var ceiling = tradeEngine.CalculateBuyPrice(item, isMerchant: true, variant: variant) * MaxShopMarkup;
                    if (pricePerItem > ceiling)
                        return (false, $"Price {pricePerItem} for '{itemRef}' exceeds the {MaxShopMarkup}x markup ceiling {ceiling}");
                }
                else
                {
                    var ceiling = tradeEngine.CalculateSellPrice(item, variant);
                    if (pricePerItem > ceiling)
                        return (false, $"Price {pricePerItem} for '{itemRef}' exceeds the catalog sell price {ceiling}");
                }
            }
        }

        return (true, null);
    }

    /// <summary>
    /// Listing prices are the shop OWNER's knob on their own Market arc — nobody else,
    /// no other kind. Bounded by the same 10x ceiling that bounds shop trades, so a
    /// listing can't become a mint through the owner-revenue credit path.
    /// </summary>
    private static (bool, string?) ValidateShopPriceSet(ArcTransaction tx, IWorld? world, ArcTradeContext? arc)
    {
        if (arc != null)
        {
            if (arc.Kind != ArcTradeRules.MarketKind)
                return (false, "Listing prices exist only on Market arcs");

            if (string.IsNullOrEmpty(arc.OwnerAvatarId)
                || !string.Equals(arc.OwnerAvatarId, tx.AvatarId, StringComparison.OrdinalIgnoreCase))
                return (false, "Only the shop's owner may set listing prices");
        }

        var itemRef = tx.GetData<string>(TransactionDataKeys.ItemRef);
        if (string.IsNullOrEmpty(itemRef))
            return (false, "ShopPriceSet requires an ItemRef");

        if (!tx.TryGetData<int>(TransactionDataKeys.PricePerItem, out var price) || price < 0)
            return (false, "ShopPriceSet requires a non-negative PricePerItem (0 clears the listing)");

        if (world != null && price > 0)
        {
            var item = ResolveTradeable(world, itemRef);
            if (item != null && item.BaseValue != int.MaxValue)
            {
                var variant = ItemRefManager.VariantOf(itemRef);
                var ceiling = new TradeEngine(world).CalculateBuyPrice(item, isMerchant: true, variant: variant) * MaxShopMarkup;
                if (price > ceiling)
                    return (false, $"Listing {price} for '{itemRef}' exceeds the {MaxShopMarkup}x markup ceiling {ceiling}");
            }
        }

        return (true, null);
    }

    private static (bool, string?) ValidateCurrencyChanged(ArcTransaction tx)
    {
        // The authoritative balance moves via /api/avatar/credits/apply (bounded there);
        // this transaction is only the quest-progress ledger marker. Require the Amount key.
        if (!tx.TryGetData<int>(TransactionDataKeys.Amount, out _))
            return (false, "CurrencyChanged requires an Amount");
        return (true, null);
    }

    private static ITradeable? ResolveTradeable(IWorld world, string? itemRef)
        => string.IsNullOrEmpty(itemRef) ? null : world.TryGetTradeableByRefName(itemRef);

    // Shops may charge up to this multiple of the standard merchant price. Deliberately high
    // (10x): cross-map arbitrage is a legitimate playstyle — buying in one town and selling in
    // another can be a 5+ day in-world walk (e.g. Jiri → Gorak Shep). This is a ceiling only,
    // to bound the owner-revenue credit path against an unbounded-price mint.
    private const int MaxShopMarkup = 10;

    // The largest single-award any dialogue action plausibly grants; one WoW-style
    // level spans 3000-21000 points, so a single node granting more than this is a mint
    private const int MaxPlausibleReputationChange = 5000;

    private static (bool, string?) ValidateQuestTokenAwarded(ArcTransaction tx, IWorld? world)
    {
        var tokenRef = tx.GetData<string>(TransactionDataKeys.QuestTokenRef);
        if (string.IsNullOrEmpty(tokenRef))
            return (false, "QuestTokenAwarded requires QuestTokenRef");

        if (world != null && !world.QuestTokensLookup.ContainsKey(tokenRef))
            return (false, $"Quest token '{tokenRef}' does not exist in this world");

        return (true, null);
    }

    private static (bool, string?) ValidateReputationChanged(ArcTransaction tx)
    {
        var factionRef = tx.GetData<string>(TransactionDataKeys.FactionRef);
        if (string.IsNullOrEmpty(factionRef))
            return (false, "ReputationChanged requires FactionRef");

        if (!tx.TryGetData<int>(TransactionDataKeys.Amount, out var amount))
            return (false, "ReputationChanged requires Amount");
        if (Math.Abs(amount) > MaxPlausibleReputationChange)
            return (false, $"Implausible reputation change {amount}");

        return (true, null);
    }

    private static (bool, string?) ValidateBattleTurn(ArcTransaction tx)
    {
        // Required keys: every legitimate producer (BattleTransactionHelper,
        // SubmitReactionHandler) writes them — a bound that only fires when the
        // client volunteers the key is opt-out for an attacker
        if (!tx.TryGetData<float>(TransactionDataKeys.DamageDealt, out var damage))
            return (false, "BattleTurnExecuted requires DamageDealt");
        if (damage < 0 || damage > MaxPlausibleDamage)
            return (false, $"Implausible damage value {damage}");

        if (!tx.TryGetData<float>(TransactionDataKeys.HealingDone, out var healing))
            return (false, "BattleTurnExecuted requires HealingDone");
        if (healing < 0 || healing > MaxPlausibleDamage)
            return (false, $"Implausible healing value {healing}");

        // Reaction outcomes never exceed the telegraphed attack: damage multipliers
        // are ≤ 1.0 and counters are fractions of the base damage
        var isReaction = tx.GetData<string>(TransactionDataKeys.ActionType) == "Reaction";
        if (isReaction)
        {
            if (!tx.TryGetData<float>(TransactionDataKeys.BaseDamage, out var baseDamage))
                return (false, "Reaction requires BaseDamage");

            if (baseDamage < 0 || baseDamage > MaxPlausibleDamage)
                return (false, $"Implausible base damage value {baseDamage}");

            if (damage > baseDamage + 0.001f)
                return (false, $"Reaction damage {damage} exceeds the telegraphed base {baseDamage}");

            if (tx.TryGetData<float>(TransactionDataKeys.CounterDamage, out var counter) &&
                counter > baseDamage + 0.001f)
                return (false, $"Counter damage {counter} exceeds the telegraphed base {baseDamage}");
        }

        return (true, null);
    }

    private static (bool, string?) ValidateCharacterDamaged(ArcState state, ArcTransaction tx)
    {
        var id = tx.GetData<string>(TransactionDataKeys.CharacterInstanceId);
        if (string.IsNullOrEmpty(id))
            return (false, "Missing CharacterInstanceId");

        if (!state.Characters.TryGetValue(id, out var character))
            return (false, $"Character '{id}' not found");

        if (!character.IsAlive)
            return (false, $"Cannot damage dead character '{id}'");

        return (true, null);
    }

    private static (bool, string?) ValidateCharacterDefeated(ArcState state, ArcTransaction tx)
    {
        var id = tx.GetData<string>(TransactionDataKeys.CharacterInstanceId);
        if (string.IsNullOrEmpty(id))
            return (false, "Missing CharacterInstanceId");

        if (!state.Characters.TryGetValue(id, out var character))
            return (false, $"Character '{id}' not found");

        if (!character.IsAlive)
            return (false, $"Character '{id}' already defeated");

        return (true, null);
    }

    private static (bool, string?) ValidateCharacterAlive(ArcState state, ArcTransaction tx, string action)
    {
        var id = tx.GetData<string>(TransactionDataKeys.CharacterInstanceId);
        if (string.IsNullOrEmpty(id))
            return (false, "Missing CharacterInstanceId");

        if (!state.Characters.TryGetValue(id, out var character))
            return (false, $"Character '{id}' not found");

        if (!character.IsAlive)
            return (false, $"Cannot {action} dead character '{id}'");

        return (true, null);
    }

    private static (bool, string?) ValidateCharacterExists(ArcState state, ArcTransaction tx)
    {
        var id = tx.GetData<string>(TransactionDataKeys.CharacterInstanceId);
        if (string.IsNullOrEmpty(id))
            return (false, "Missing CharacterInstanceId");

        if (!state.Characters.ContainsKey(id))
            return (false, $"Character '{id}' not found");

        return (true, null);
    }

    private static (bool, string?) ValidateQuestAccepted(ArcState state, ArcTransaction tx)
    {
        var questRef = tx.GetData<string>(TransactionDataKeys.QuestRef);
        if (string.IsNullOrEmpty(questRef))
            return (false, "Missing QuestRef");

        if (state.ActiveQuests.ContainsKey(questRef))
            return (false, $"Quest '{questRef}' already accepted");

        if (state.CompletedQuests.Contains(questRef))
            return (false, $"Quest '{questRef}' already completed");

        return (true, null);
    }

    private static (bool, string?) ValidateQuestCompleted(ArcState state, ArcTransaction tx)
    {
        var questRef = tx.GetData<string>(TransactionDataKeys.QuestRef);
        if (string.IsNullOrEmpty(questRef))
            return (false, "Missing QuestRef");

        if (state.CompletedQuests.Contains(questRef))
            return (false, $"Quest '{questRef}' already completed");

        if (!state.ActiveQuests.ContainsKey(questRef))
            return (false, $"Quest '{questRef}' not active");

        return (true, null);
    }

    private static (bool, string?) ValidateQuestActive(ArcState state, ArcTransaction tx)
    {
        var questRef = tx.GetData<string>(TransactionDataKeys.QuestRef);
        if (string.IsNullOrEmpty(questRef))
            return (false, "Missing QuestRef");

        if (!state.ActiveQuests.ContainsKey(questRef))
            return (false, $"Quest '{questRef}' not active");

        return (true, null);
    }

    private static (bool, string?) ValidateQuestStageAdvanced(ArcState state, ArcTransaction tx, IWorld? world)
    {
        var activeCheck = ValidateQuestActive(state, tx);
        if (!activeCheck.Item1)
            return activeCheck;

        // The state machine sets CurrentStage to whatever the transaction says and an
        // empty CurrentStage doubles as the ready-to-complete signal — so a crafted
        // NextStage could mark any quest completable. Require a real stage.
        if (world != null &&
            tx.TryGetData<string>(TransactionDataKeys.NextStage, out var nextStage) &&
            !string.IsNullOrEmpty(nextStage))
        {
            var questRef = tx.GetData<string>(TransactionDataKeys.QuestRef);
            if (!string.IsNullOrEmpty(questRef))
            {
                var quest = world.TryGetQuestByRefName(questRef);
                var stageExists = quest?.Stages?.Stage?.Any(s => s.RefName == nextStage) ?? true;
                if (!stageExists)
                    return (false, $"Quest '{questRef}' has no stage '{nextStage}'");
            }
        }

        return (true, null);
    }

    private static (bool, string?) ValidateQuestBranchChosen(ArcState state, ArcTransaction tx)
    {
        var questRef = tx.GetData<string>(TransactionDataKeys.QuestRef);
        if (string.IsNullOrEmpty(questRef))
            return (false, "Missing QuestRef");

        if (!state.ActiveQuests.TryGetValue(questRef, out var questState))
            return (false, $"Quest '{questRef}' not active");

        if (!string.IsNullOrEmpty(questState.ChosenBranch))
            return (false, $"Quest '{questRef}' already has branch '{questState.ChosenBranch}' chosen");

        return (true, null);
    }

    private static (bool, string?) ValidateArcCompleted(ArcState state)
    {
        if (state.Status == ArcStatus.Completed)
            return (false, "Arc already completed");

        if (state.Status == ArcStatus.Undiscovered)
            return (false, "Arc not yet discovered");

        return (true, null);
    }
}
