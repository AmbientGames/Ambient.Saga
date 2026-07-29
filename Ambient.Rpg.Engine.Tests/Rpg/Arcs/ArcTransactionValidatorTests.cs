using Ambient.Domain;
using Ambient.Domain.Partials;
using Ambient.Rpg.Engine.Domain;
using Ambient.Rpg.Engine.Domain.Arcs.TransactionLog;

namespace Ambient.Rpg.Engine.Tests.Rpg.Arcs;

/// <summary>
/// Tests for the server-side transaction validator. The validator is default-deny:
/// only types with a legitimate client-side producer are accepted, StateSnapshot is
/// server-generated only, and client-supplied trade prices / combat results are
/// bounded against the catalog and plausibility limits.
/// </summary>
public class ArcTransactionValidatorTests
{
    private static readonly Guid MerchantInstanceId = Guid.NewGuid();

    private static ArcState CreateStateWithLivingMerchant()
    {
        var state = new ArcState();
        state.Characters[MerchantInstanceId.ToString()] = new CharacterState
        {
            CharacterInstanceId = MerchantInstanceId,
            CharacterRef = "Merchant",
            IsSpawned = true,
            IsAlive = true
        };
        return state;
    }

    /// <summary>Dead character whose victor is recorded — the victory-loot scenario.</summary>
    private static ArcState CreateStateWithDefeatedCharacter(string victorAvatarId)
    {
        var state = new ArcState();
        state.Characters[MerchantInstanceId.ToString()] = new CharacterState
        {
            CharacterInstanceId = MerchantInstanceId,
            CharacterRef = "Bandit",
            IsSpawned = true,
            IsAlive = false,
            DefeatedByAvatarId = victorAvatarId
        };
        return state;
    }

    private static World CreateWorldWithPricedSword(int baseValue = 100)
    {
        var world = new World();
        world.EquipmentLookup["IronSword"] = new Equipment
        {
            RefName = "IronSword",
            DisplayName = "Iron Sword",
            BaseValue = baseValue
            // MerchantMarkupMultiplier keeps its generated default of 1.5
        };
        return world;
    }

    private static ArcTransaction CreateTransaction(ArcTransactionType type, Dictionary<string, string>? data = null, string? extensionTypeName = null)
        => new()
        {
            TransactionId = Guid.NewGuid(),
            Type = type,
            ExtensionTypeName = extensionTypeName,
            AvatarId = Guid.NewGuid().ToString(),
            LocalTimestamp = DateTime.UtcNow,
            Data = data ?? new Dictionary<string, string>()
        };

    private static Dictionary<string, string> TradeData(int pricePerItem, bool isBuying, int quantity = 1) => new()
    {
        [TransactionDataKeys.CharacterInstanceId] = MerchantInstanceId.ToString(),
        [TransactionDataKeys.ItemRef] = "IronSword",
        [TransactionDataKeys.Quantity] = quantity.ToString(),
        [TransactionDataKeys.IsBuying] = isBuying.ToString(),
        [TransactionDataKeys.PricePerItem] = pricePerItem.ToString()
    };

    [Fact]
    public void Validate_StateSnapshotFromClient_Rejected()
    {
        var (isValid, reason) = ArcTransactionValidator.Validate(
            CreateStateWithLivingMerchant(),
            CreateTransaction(ArcTransactionType.StateSnapshot));

        Assert.False(isValid);
        Assert.Contains("server-generated", reason);
    }

    [Theory]
    [InlineData(ArcTransactionType.CurrencyChanged)]
    public void Validate_TypesWithoutClientProducer_Rejected(ArcTransactionType type)
    {
        var (isValid, _) = ArcTransactionValidator.Validate(
            CreateStateWithLivingMerchant(), CreateTransaction(type));

        Assert.False(isValid);
    }

    [Fact]
    public void Validate_ReputationChanged_RequiresFactionAndPlausibleAmount()
    {
        // Produced by dialogue ChangeReputation actions (incl. spillover)
        var valid = ArcTransactionValidator.Validate(
            CreateStateWithLivingMerchant(),
            CreateTransaction(ArcTransactionType.ReputationChanged, new Dictionary<string, string>
            {
                [TransactionDataKeys.FactionRef] = "CityGuard",
                [TransactionDataKeys.Amount] = "250"
            }));
        Assert.True(valid.IsValid, valid.Reason);

        var noFaction = ArcTransactionValidator.Validate(
            CreateStateWithLivingMerchant(),
            CreateTransaction(ArcTransactionType.ReputationChanged));
        Assert.False(noFaction.IsValid);

        var minted = ArcTransactionValidator.Validate(
            CreateStateWithLivingMerchant(),
            CreateTransaction(ArcTransactionType.ReputationChanged, new Dictionary<string, string>
            {
                [TransactionDataKeys.FactionRef] = "CityGuard",
                [TransactionDataKeys.Amount] = "999999"
            }));
        Assert.False(minted.IsValid);
    }

    [Theory]
    [InlineData(ArcTransactionType.EquipmentChanged)]
    [InlineData(ArcTransactionType.ArcDiscovered)]
    [InlineData(ArcTransactionType.TriggerActivated)]
    [InlineData(ArcTransactionType.BattleStarted)]
    public void Validate_TypesWithClientProducer_Allowed(ArcTransactionType type)
    {
        var (isValid, reason) = ArcTransactionValidator.Validate(
            CreateStateWithLivingMerchant(), CreateTransaction(type));

        Assert.True(isValid, reason);
    }

    [Fact]
    public void Validate_QuestTokenAwarded_RequiresTokenRef()
    {
        // Quest tokens are the progression currency — a bare award with no token
        // ref (or, with a world, an unknown ref) is a mint attempt
        var (isValid, _) = ArcTransactionValidator.Validate(
            CreateStateWithLivingMerchant(),
            CreateTransaction(ArcTransactionType.QuestTokenAwarded));
        Assert.False(isValid);

        var withRef = ArcTransactionValidator.Validate(
            CreateStateWithLivingMerchant(),
            CreateTransaction(ArcTransactionType.QuestTokenAwarded, new Dictionary<string, string>
            {
                [TransactionDataKeys.QuestTokenRef] = "TOKEN_TEST"
            }));
        Assert.True(withRef.IsValid, withRef.Reason);
    }

    [Fact]
    public void Validate_ExtensionWithoutTypeName_Rejected()
    {
        // Unparseable client type strings also land on Extension with no name
        var (isValid, _) = ArcTransactionValidator.Validate(
            CreateStateWithLivingMerchant(),
            CreateTransaction(ArcTransactionType.Extension));

        Assert.False(isValid);
    }

    [Fact]
    public void Validate_ExtensionWithTypeName_Allowed()
    {
        var (isValid, reason) = ArcTransactionValidator.Validate(
            CreateStateWithLivingMerchant(),
            CreateTransaction(ArcTransactionType.Extension, extensionTypeName: "MiningClaimed"));

        Assert.True(isValid, reason);
    }

    [Fact]
    public void Validate_BuyBelowCatalogFloor_Rejected()
    {
        // BaseValue 100 × markup 1.5 × max discount 0.72 = floor 108
        var (isValid, reason) = ArcTransactionValidator.Validate(
            CreateStateWithLivingMerchant(),
            CreateTransaction(ArcTransactionType.ItemTraded, TradeData(pricePerItem: 0, isBuying: true)),
            CreateWorldWithPricedSword());

        Assert.False(isValid);
        Assert.Contains("below the catalog minimum", reason);
    }

    [Fact]
    public void Validate_BuyAtCatalogPrice_Allowed()
    {
        var (isValid, reason) = ArcTransactionValidator.Validate(
            CreateStateWithLivingMerchant(),
            CreateTransaction(ArcTransactionType.ItemTraded, TradeData(pricePerItem: 150, isBuying: true)),
            CreateWorldWithPricedSword());

        Assert.True(isValid, reason);
    }

    [Fact]
    public void Validate_SellAboveBaseValue_Rejected()
    {
        var (isValid, reason) = ArcTransactionValidator.Validate(
            CreateStateWithLivingMerchant(),
            CreateTransaction(ArcTransactionType.ItemTraded, TradeData(pricePerItem: 5000, isBuying: false)),
            CreateWorldWithPricedSword());

        Assert.False(isValid);
        Assert.Contains("exceeds the catalog sell price", reason);
    }

    [Fact]
    public void Validate_UntradeableSentinel_Rejected()
    {
        // BaseValue = int.MaxValue means "cannot be traded" (Economy.xsd)
        var (isValid, reason) = ArcTransactionValidator.Validate(
            CreateStateWithLivingMerchant(),
            CreateTransaction(ArcTransactionType.ItemTraded, TradeData(pricePerItem: 100, isBuying: false)),
            CreateWorldWithPricedSword(baseValue: int.MaxValue));

        Assert.False(isValid);
        Assert.Contains("not tradeable", reason);
    }

    [Fact]
    public void Validate_TradeWithoutWorld_SkipsPriceCheckButKeepsAliveCheck()
    {
        // No world supplied: price bounds can't be derived, but state checks still run
        var (isValid, reason) = ArcTransactionValidator.Validate(
            CreateStateWithLivingMerchant(),
            CreateTransaction(ArcTransactionType.ItemTraded, TradeData(pricePerItem: 0, isBuying: true)));

        Assert.True(isValid, reason);

        var (deadValid, _) = ArcTransactionValidator.Validate(
            new ArcState(),
            CreateTransaction(ArcTransactionType.ItemTraded, TradeData(pricePerItem: 150, isBuying: true)));

        Assert.False(deadValid); // merchant not in state
    }

    // ===== Container arcs: per-kind free-take rules (ArcTradeRules) =====

    private static ArcTransaction CreateArcTradeTransaction(string avatarId, int pricePerItem = 0, bool isBuying = true)
    {
        var tx = CreateTransaction(ArcTransactionType.ItemTraded, TradeData(pricePerItem, isBuying));
        tx.AvatarId = avatarId;
        return tx;
    }

    [Fact]
    public void Validate_TradeWithDefeatedCharacter_AlwaysRejected()
    {
        // Battle drops are BattleLoot remains ARCS — the corpse itself is untouchable,
        // victor or not, priced or free.
        var victor = Guid.NewGuid().ToString();

        var (isValid, reason) = ArcTransactionValidator.Validate(
            CreateStateWithDefeatedCharacter(victor),
            CreateArcTradeTransaction(victor),
            CreateWorldWithPricedSword());

        Assert.False(isValid);
        Assert.Contains("dead character", reason);
    }

    [Theory]
    [InlineData("RemnantLoot")]
    [InlineData("GeoCache")]
    public void Validate_FreeTakeArc_ZeroPriceTakeByAnyone_Allowed(string kind)
    {
        // Death remains and geocaches are takeable by ANYONE at price 0 — the catalog
        // floor bound must not apply (this was the bug that made remains unlootable).
        var stranger = Guid.NewGuid().ToString();

        var (isValid, reason) = ArcTransactionValidator.Validate(
            CreateStateWithLivingMerchant(),
            CreateArcTradeTransaction(stranger),
            CreateWorldWithPricedSword(),
            new ArcTradeContext(kind, OwnerAvatarId: Guid.NewGuid().ToString()));

        Assert.True(isValid, reason);
    }

    [Fact]
    public void Validate_BattleLootTake_VictorOnly()
    {
        var victor = Guid.NewGuid().ToString();
        var arc = new ArcTradeContext("BattleLoot", victor);

        var (victorTake, reason) = ArcTransactionValidator.Validate(
            CreateStateWithLivingMerchant(),
            CreateArcTradeTransaction(victor),
            CreateWorldWithPricedSword(), arc);
        Assert.True(victorTake, reason);

        var (strangerTake, _) = ArcTransactionValidator.Validate(
            CreateStateWithLivingMerchant(),
            CreateArcTradeTransaction(Guid.NewGuid().ToString()),
            CreateWorldWithPricedSword(), arc);
        Assert.False(strangerTake);
    }

    [Fact]
    public void Validate_FreeTakeArc_PricedTrade_Rejected()
    {
        // A priced trade at a free-take arc is a tampered client — a priced "deposit"
        // into a geocache would mint credits from nothing.
        var (pricedTake, _) = ArcTransactionValidator.Validate(
            CreateStateWithLivingMerchant(),
            CreateArcTradeTransaction(Guid.NewGuid().ToString(), pricePerItem: 150),
            CreateWorldWithPricedSword(),
            new ArcTradeContext("RemnantLoot", Guid.NewGuid().ToString()));
        Assert.False(pricedTake);

        var (pricedDeposit, _) = ArcTransactionValidator.Validate(
            CreateStateWithLivingMerchant(),
            CreateArcTradeTransaction(Guid.NewGuid().ToString(), pricePerItem: 1, isBuying: false),
            CreateWorldWithPricedSword(),
            new ArcTradeContext("GeoCache", Guid.NewGuid().ToString()));
        Assert.False(pricedDeposit);
    }

    [Fact]
    public void Validate_Deposits_RemainsRejected_GeoCacheAllowed()
    {
        var stranger = Guid.NewGuid().ToString();

        var (intoRemains, _) = ArcTransactionValidator.Validate(
            CreateStateWithLivingMerchant(),
            CreateArcTradeTransaction(stranger, pricePerItem: 0, isBuying: false),
            CreateWorldWithPricedSword(),
            new ArcTradeContext("RemnantLoot", Guid.NewGuid().ToString()));
        Assert.False(intoRemains);

        var (intoGeoCache, reason) = ArcTransactionValidator.Validate(
            CreateStateWithLivingMerchant(),
            CreateArcTradeTransaction(stranger, pricePerItem: 0, isBuying: false),
            CreateWorldWithPricedSword(),
            new ArcTradeContext("GeoCache", Guid.NewGuid().ToString()));
        Assert.True(intoGeoCache, reason);
    }

    [Fact]
    public void Validate_ShopPriceSet_OwnerOnly_MarketOnly_Bounded()
    {
        var owner = Guid.NewGuid().ToString();
        var market = new ArcTradeContext("Market", owner);

        ArcTransaction PriceTx(string avatarId, int price)
        {
            var tx = CreateTransaction(ArcTransactionType.ShopPriceSet, new Dictionary<string, string>
            {
                [TransactionDataKeys.ItemRef] = "IronSword",
                [TransactionDataKeys.PricePerItem] = price.ToString()
            });
            tx.AvatarId = avatarId;
            return tx;
        }

        var (ownerSet, reason) = ArcTransactionValidator.Validate(
            CreateStateWithLivingMerchant(), PriceTx(owner, 700), CreateWorldWithPricedSword(), market);
        Assert.True(ownerSet, reason);

        var (strangerSet, _) = ArcTransactionValidator.Validate(
            CreateStateWithLivingMerchant(), PriceTx(Guid.NewGuid().ToString(), 700), CreateWorldWithPricedSword(), market);
        Assert.False(strangerSet);

        var (onRemains, _) = ArcTransactionValidator.Validate(
            CreateStateWithLivingMerchant(), PriceTx(owner, 700), CreateWorldWithPricedSword(),
            new ArcTradeContext("RemnantLoot", owner));
        Assert.False(onRemains);

        var (mintAttempt, _) = ArcTransactionValidator.Validate(
            CreateStateWithLivingMerchant(), PriceTx(owner, 999999), CreateWorldWithPricedSword(), market);
        Assert.False(mintAttempt);
    }

    [Fact]
    public void Validate_ListedItemTrade_MustMatchTheListing()
    {
        // A listed item trades at exactly its listing — replacing the catalog bounds.
        var owner = Guid.NewGuid().ToString();
        var market = new ArcTradeContext("Market", owner);
        var state = CreateStateWithLivingMerchant();
        state.ShopPrices["IronSword"] = 700;

        ArcTransaction BuyTx(int price)
        {
            var tx = CreateTransaction(ArcTransactionType.ItemTraded, new Dictionary<string, string>
            {
                [TransactionDataKeys.CharacterInstanceId] = MerchantInstanceId.ToString(),
                [TransactionDataKeys.ItemRef] = "IronSword",
                [TransactionDataKeys.IsBuying] = "True",
                [TransactionDataKeys.PricePerItem] = price.ToString()
            });
            tx.AvatarId = Guid.NewGuid().ToString();
            return tx;
        }

        var (atListing, reason) = ArcTransactionValidator.Validate(state, BuyTx(700), CreateWorldWithPricedSword(), market);
        Assert.True(atListing, reason);

        var (atCatalog, _) = ArcTransactionValidator.Validate(state, BuyTx(150), CreateWorldWithPricedSword(), market);
        Assert.False(atCatalog);
    }

    [Fact]
    public void Validate_MarketOwnerZeroPriceRestock_Allowed()
    {
        // A shop owner moving their own stock (zero-price take/deposit) syncs as valid;
        // without the owner rule the catalog floor rejected every restock at sync.
        var owner = Guid.NewGuid().ToString();
        var arc = new ArcTradeContext("Market", owner);

        var (withdraw, reason) = ArcTransactionValidator.Validate(
            CreateStateWithLivingMerchant(),
            CreateArcTradeTransaction(owner),
            CreateWorldWithPricedSword(), arc);
        Assert.True(withdraw, reason);

        var (stock, reason2) = ArcTransactionValidator.Validate(
            CreateStateWithLivingMerchant(),
            CreateArcTradeTransaction(owner, pricePerItem: 0, isBuying: false),
            CreateWorldWithPricedSword(), arc);
        Assert.True(stock, reason2);

        // A visiting stranger still hits the normal price bounds.
        var (visitor, _) = ArcTransactionValidator.Validate(
            CreateStateWithLivingMerchant(),
            CreateArcTradeTransaction(Guid.NewGuid().ToString(), pricePerItem: 0),
            CreateWorldWithPricedSword(), arc);
        Assert.False(visitor);
    }

    [Fact]
    public void Validate_FreeTakeFromAliveCharacter_StillRejectedByPriceFloor()
    {
        // The victor exception applies to DEFEATED characters only: a zero-price buy
        // from a living merchant keeps hitting the catalog floor bound
        var (isValid, reason) = ArcTransactionValidator.Validate(
            CreateStateWithLivingMerchant(),
            CreateTransaction(ArcTransactionType.ItemTraded, TradeData(pricePerItem: 0, isBuying: true)),
            CreateWorldWithPricedSword());

        Assert.False(isValid);
        Assert.Contains("below the catalog minimum", reason);
    }

    [Fact]
    public void Validate_ReactionDamageExceedingTelegraphedBase_Rejected()
    {
        var data = new Dictionary<string, string>
        {
            [TransactionDataKeys.ActionType] = "Reaction",
            [TransactionDataKeys.BaseDamage] = "0.3",
            [TransactionDataKeys.DamageDealt] = "0.9",
            [TransactionDataKeys.HealingDone] = "0"
        };

        var (isValid, reason) = ArcTransactionValidator.Validate(
            CreateStateWithLivingMerchant(),
            CreateTransaction(ArcTransactionType.BattleTurnExecuted, data));

        Assert.False(isValid);
        Assert.Contains("exceeds the telegraphed base", reason);
    }

    [Fact]
    public void Validate_ImplausibleDamage_Rejected()
    {
        var data = new Dictionary<string, string>
        {
            [TransactionDataKeys.DamageDealt] = "99999"
        };

        var (isValid, _) = ArcTransactionValidator.Validate(
            CreateStateWithLivingMerchant(),
            CreateTransaction(ArcTransactionType.BattleTurnExecuted, data));

        Assert.False(isValid);
    }

    [Fact]
    public void Validate_PlausibleBattleTurn_Allowed()
    {
        var data = new Dictionary<string, string>
        {
            [TransactionDataKeys.ActionType] = "Reaction",
            [TransactionDataKeys.BaseDamage] = "0.3",
            [TransactionDataKeys.DamageDealt] = "0.15",
            [TransactionDataKeys.CounterDamage] = "0.12",
            [TransactionDataKeys.HealingDone] = "0"
        };

        var (isValid, reason) = ArcTransactionValidator.Validate(
            CreateStateWithLivingMerchant(),
            CreateTransaction(ArcTransactionType.BattleTurnExecuted, data));

        Assert.True(isValid, reason);
    }
}
