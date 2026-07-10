using Ambient.Domain;
using Ambient.Domain.Contracts;
using Ambient.Saga.Engine.Domain;
using Ambient.Saga.Engine.Domain.Rpg.Trade;
namespace Ambient.Saga.Engine.Domain.Rpg.Sagas.TransactionLog;

/// <summary>
/// Validates incoming transactions against the current derived SagaState.
/// Used server-side to reject fraudulent or inconsistent transactions before persisting.
/// Each rule mirrors the precondition checks from the corresponding client-side handler.
/// Default-deny: a transaction type is only accepted if it has a legitimate
/// client-side producer — everything else is rejected.
/// </summary>
public static class SagaTransactionValidator
{
    // A single hit can't plausibly exceed this on normalized 0–1 stats (weapon
    // Str×2.5 with stance and vulnerability tops out well below it). Blocks
    // fabricated damage values, not legitimate play.
    private const float MaxPlausibleDamage = 10.0f;

    // Types the client legitimately emits but that need no state precondition.
    // A type absent from this set AND from the switch below is rejected —
    // add it here when a client-side producer ships.
    private static readonly HashSet<SagaTransactionType> AllowedClientTypes = new()
    {
        SagaTransactionType.SagaDiscovered,
        SagaTransactionType.TriggerActivated,
        SagaTransactionType.TriggerCompleted,
        SagaTransactionType.CharacterSpawned,
        SagaTransactionType.BattleStarted,
        SagaTransactionType.BattleEnded,
        SagaTransactionType.AvatarEntered,
        SagaTransactionType.AvatarExited,
        SagaTransactionType.EntityInteracted,
        SagaTransactionType.DialogueStarted,
        SagaTransactionType.DialogueNodeVisited,
        SagaTransactionType.DialogueCompleted,
        SagaTransactionType.TraitAssigned,
        SagaTransactionType.TraitRemoved,
        SagaTransactionType.PartyMemberJoined,
        SagaTransactionType.PartyMemberLeft,
        SagaTransactionType.AffinityGranted,
        SagaTransactionType.EquipmentChanged,
        SagaTransactionType.ConsumableUsed,
        SagaTransactionType.ToolSharpened,
        SagaTransactionType.AvatarTeleported,
        SagaTransactionType.EffectApplied,
    };

    /// <summary>
    /// Validates a transaction against the current state.
    /// Returns (true, null) if valid, (false, reason) if invalid.
    /// Pass the world to additionally bound trade prices against the catalog.
    /// </summary>
    public static (bool IsValid, string? Reason) Validate(SagaState state, SagaTransaction transaction, IWorld? world = null)
    {
        return transaction.Type switch
        {
            // Character lifecycle
            SagaTransactionType.CharacterDamaged => ValidateCharacterDamaged(state, transaction),
            SagaTransactionType.CharacterDefeated => ValidateCharacterDefeated(state, transaction),
            SagaTransactionType.CharacterHealed => ValidateCharacterAlive(state, transaction, "heal"),
            SagaTransactionType.CharacterDespawned => ValidateCharacterExists(state, transaction),

            // Quest lifecycle
            SagaTransactionType.QuestAccepted => ValidateQuestAccepted(state, transaction),
            SagaTransactionType.QuestCompleted => ValidateQuestCompleted(state, transaction),
            SagaTransactionType.QuestAbandoned => ValidateQuestActive(state, transaction),
            SagaTransactionType.QuestStageAdvanced => ValidateQuestStageAdvanced(state, transaction, world),
            SagaTransactionType.QuestBranchChosen => ValidateQuestBranchChosen(state, transaction),
            SagaTransactionType.QuestObjectiveCompleted => ValidateQuestActive(state, transaction),

            // Trading
            SagaTransactionType.ItemTraded => ValidateItemTraded(state, transaction, world),

            // Currency ledger marker read by CurrencyCollected quest objectives. Has live
            // client producers (trade + dialogue rewards); default-denying it rejected every
            // priced trade/reward on sync and regressed quest progress.
            SagaTransactionType.CurrencyChanged => ValidateCurrencyChanged(transaction),

            // Combat results are client-reported — bound them to plausible values
            SagaTransactionType.BattleTurnExecuted => ValidateBattleTurn(transaction),

            // Reputation changes come from dialogue actions (incl. spillover) —
            // require a faction and bound the per-transaction amount
            SagaTransactionType.ReputationChanged => ValidateReputationChanged(transaction),

            // Quest tokens are the progression currency — at minimum the token must
            // exist in the world catalog (previously accepted with zero validation)
            SagaTransactionType.QuestTokenAwarded => ValidateQuestTokenAwarded(transaction, world),

            // Saga lifecycle
            SagaTransactionType.SagaCompleted => ValidateSagaCompleted(state),

            // Server-generated only: a client-supplied snapshot would replace the
            // entire replayed state and propagate to every peer of the arc
            SagaTransactionType.StateSnapshot => (false, "StateSnapshot is server-generated and not accepted from clients"),

            // Extension claims must at least identify themselves. Unparseable client
            // type names also land here (DtoToDomainTransaction falls back to
            // Extension) with no ExtensionTypeName — rejected.
            SagaTransactionType.Extension => string.IsNullOrEmpty(transaction.ExtensionTypeName)
                ? (false, "Extension transaction missing ExtensionTypeName")
                : (true, null),

            // Everything else: allowed only with a known client-side producer
            _ => AllowedClientTypes.Contains(transaction.Type)
                ? (true, null)
                : (false, $"Transaction type '{transaction.Type}' has no client-side producer and is not accepted")
        };
    }

    private static (bool, string?) ValidateItemTraded(SagaState state, SagaTransaction tx, IWorld? world)
    {
        var characterId = tx.GetData<string>(TransactionDataKeys.CharacterInstanceId);
        if (string.IsNullOrEmpty(characterId))
            return (false, "Missing CharacterInstanceId");

        if (!state.Characters.TryGetValue(characterId, out var tradeCharacter))
            return (false, $"Character '{characterId}' not found");

        // VICTORY LOOT exception: the victor of THIS spawn instance may free-take
        // (zero-price buy) the defeated character's remaining Loot. Everything else
        // against a dead character stays invalid — including zero-price SELLS to a
        // corpse (there is no honest producer for depositing into a defeat drop).
        var isVictoryLootTake = false;
        if (!tradeCharacter.IsAlive)
        {
            var buyingFromCorpse = tx.TryGetData<bool>(TransactionDataKeys.IsBuying, out var b) && b;
            var freeOfCharge = tx.TryGetData<int>(TransactionDataKeys.PricePerItem, out var p) && p == 0;
            var isVictor = !string.IsNullOrEmpty(tradeCharacter.DefeatedByAvatarId)
                           && string.Equals(tradeCharacter.DefeatedByAvatarId, tx.AvatarId, StringComparison.OrdinalIgnoreCase);

            if (!(buyingFromCorpse && freeOfCharge && isVictor))
                return (false, $"Cannot trade with dead character '{characterId}' (victory loot is a zero-price take by the victor only)");

            isVictoryLootTake = true;
        }

        if (tx.TryGetData<int>(TransactionDataKeys.Quantity, out var quantity) && quantity <= 0)
            return (false, "Trade quantity must be greater than zero");

        // Degradables (equipment/tools/spells) are unique per inventory entry — a
        // sale of more than one at a time has no honest producer (the UI sends
        // quantity 1) and was a crafted-command payout multiplier
        if (world != null && quantity > 1 &&
            tx.TryGetData<bool>(TransactionDataKeys.IsBuying, out var buyingFlag) && !buyingFlag)
        {
            var soldRef = tx.GetData<string>(TransactionDataKeys.ItemRef);
            if (!string.IsNullOrEmpty(soldRef) &&
                (world.EquipmentLookup.ContainsKey(soldRef) ||
                 world.ToolsLookup.ContainsKey(soldRef) ||
                 world.SpellsLookup.ContainsKey(soldRef)))
            {
                return (false, $"Cannot sell {quantity}x '{soldRef}' — degradable items sell one at a time");
            }
        }

        // Bound the client-supplied price against the catalog: the floor is the
        // maximum-discount buy price, the ceiling for sales is full wholesale.
        // Uses TradeEngine so pricing lives in exactly one place. The key is
        // required — omitting it was an opt-out from the bound.
        // Victory-loot takes are exempt: they are zero-price BY RULE (enforced above),
        // so the merchant buy floor does not apply.
        if (world != null && !isVictoryLootTake)
        {
            if (!tx.TryGetData<int>(TransactionDataKeys.PricePerItem, out var pricePerItem))
                return (false, "ItemTraded requires PricePerItem");

            var itemRef = tx.GetData<string>(TransactionDataKeys.ItemRef);
            var item = ResolveTradeable(world, itemRef);
            var isBuying = tx.TryGetData<bool>(TransactionDataKeys.IsBuying, out var buying) && buying;
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
                if (item.WholesalePrice == int.MaxValue)
                    return (false, $"'{itemRef}' is not tradeable");

                var tradeEngine = new TradeEngine(world);
                if (isBuying)
                {
                    var floor = tradeEngine.CalculateBuyPrice(item, isMerchant: true,
                        characterTraits: new List<string> { "Friendly", "TradeDiscount" });
                    if (pricePerItem < floor)
                        return (false, $"Price {pricePerItem} for '{itemRef}' is below the catalog minimum {floor}");

                    // Ceiling: shops may mark up steeply — cross-map arbitrage (buy in Jiri,
                    // sell in Gorak Shep, a 5+ day walk) is a legitimate playstyle — but not
                    // without bound, or an unbounded buy price mints owner revenue. Cap at 10x
                    // the standard (undiscounted) merchant price.
                    var ceiling = tradeEngine.CalculateBuyPrice(item, isMerchant: true) * MaxShopMarkup;
                    if (pricePerItem > ceiling)
                        return (false, $"Price {pricePerItem} for '{itemRef}' exceeds the {MaxShopMarkup}x markup ceiling {ceiling}");
                }
                else
                {
                    var ceiling = tradeEngine.CalculateSellPrice(item);
                    if (pricePerItem > ceiling)
                        return (false, $"Price {pricePerItem} for '{itemRef}' exceeds the catalog sell price {ceiling}");
                }
            }
        }

        return (true, null);
    }

    private static (bool, string?) ValidateCurrencyChanged(SagaTransaction tx)
    {
        // The authoritative balance moves via /api/avatar/credits/apply (bounded there);
        // this transaction is only the quest-progress ledger marker. Require the Amount key.
        if (!tx.TryGetData<int>(TransactionDataKeys.Amount, out _))
            return (false, "CurrencyChanged requires an Amount");
        return (true, null);
    }

    private static ITradeable? ResolveTradeable(IWorld world, string? itemRef)
    {
        if (string.IsNullOrEmpty(itemRef))
            return null;

        if (world.EquipmentLookup.TryGetValue(itemRef, out var equipment)) return equipment;
        if (world.ConsumablesLookup.TryGetValue(itemRef, out var consumable)) return consumable;
        if (world.ToolsLookup.TryGetValue(itemRef, out var tool)) return tool;
        if (world.SpellsLookup.TryGetValue(itemRef, out var spell)) return spell;
        if (world.BuildingMaterialsLookup.TryGetValue(itemRef, out var material)) return material;
        return world.BlockProvider?.GetBlockByRefName(itemRef);
    }

    // Shops may charge up to this multiple of the standard merchant price. Deliberately high
    // (10x): cross-map arbitrage is a legitimate playstyle — buying in one town and selling in
    // another can be a 5+ day in-world walk (e.g. Jiri → Gorak Shep). This is a ceiling only,
    // to bound the owner-revenue credit path against an unbounded-price mint.
    private const int MaxShopMarkup = 10;

    // The largest single-award any dialogue action plausibly grants; one WoW-style
    // level spans 3000-21000 points, so a single node granting more than this is a mint
    private const int MaxPlausibleReputationChange = 5000;

    private static (bool, string?) ValidateQuestTokenAwarded(SagaTransaction tx, IWorld? world)
    {
        var tokenRef = tx.GetData<string>(TransactionDataKeys.QuestTokenRef);
        if (string.IsNullOrEmpty(tokenRef))
            return (false, "QuestTokenAwarded requires QuestTokenRef");

        if (world != null && !world.QuestTokensLookup.ContainsKey(tokenRef))
            return (false, $"Quest token '{tokenRef}' does not exist in this world");

        return (true, null);
    }

    private static (bool, string?) ValidateReputationChanged(SagaTransaction tx)
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

    private static (bool, string?) ValidateBattleTurn(SagaTransaction tx)
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

    private static (bool, string?) ValidateCharacterDamaged(SagaState state, SagaTransaction tx)
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

    private static (bool, string?) ValidateCharacterDefeated(SagaState state, SagaTransaction tx)
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

    private static (bool, string?) ValidateCharacterAlive(SagaState state, SagaTransaction tx, string action)
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

    private static (bool, string?) ValidateCharacterExists(SagaState state, SagaTransaction tx)
    {
        var id = tx.GetData<string>(TransactionDataKeys.CharacterInstanceId);
        if (string.IsNullOrEmpty(id))
            return (false, "Missing CharacterInstanceId");

        if (!state.Characters.ContainsKey(id))
            return (false, $"Character '{id}' not found");

        return (true, null);
    }

    private static (bool, string?) ValidateQuestAccepted(SagaState state, SagaTransaction tx)
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

    private static (bool, string?) ValidateQuestCompleted(SagaState state, SagaTransaction tx)
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

    private static (bool, string?) ValidateQuestActive(SagaState state, SagaTransaction tx)
    {
        var questRef = tx.GetData<string>(TransactionDataKeys.QuestRef);
        if (string.IsNullOrEmpty(questRef))
            return (false, "Missing QuestRef");

        if (!state.ActiveQuests.ContainsKey(questRef))
            return (false, $"Quest '{questRef}' not active");

        return (true, null);
    }

    private static (bool, string?) ValidateQuestStageAdvanced(SagaState state, SagaTransaction tx, IWorld? world)
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

    private static (bool, string?) ValidateQuestBranchChosen(SagaState state, SagaTransaction tx)
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

    private static (bool, string?) ValidateSagaCompleted(SagaState state)
    {
        if (state.Status == SagaStatus.Completed)
            return (false, "Saga already completed");

        if (state.Status == SagaStatus.Undiscovered)
            return (false, "Saga not yet discovered");

        return (true, null);
    }
}
