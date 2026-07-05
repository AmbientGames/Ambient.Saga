using Ambient.Domain;
using Ambient.Domain.Partials;
using Ambient.Saga.Engine.Domain;
using Ambient.Saga.Engine.Domain.Rpg.Sagas.TransactionLog;

namespace Ambient.Saga.Engine.Tests.Rpg.Sagas;

/// <summary>
/// Tests for the server-side transaction validator. The validator is default-deny:
/// only types with a legitimate client-side producer are accepted, StateSnapshot is
/// server-generated only, and client-supplied trade prices / combat results are
/// bounded against the catalog and plausibility limits.
/// </summary>
public class SagaTransactionValidatorTests
{
    private static readonly Guid MerchantInstanceId = Guid.NewGuid();

    private static SagaState CreateStateWithLivingMerchant()
    {
        var state = new SagaState();
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
    private static SagaState CreateStateWithDefeatedCharacter(string victorAvatarId)
    {
        var state = new SagaState();
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

    private static World CreateWorldWithPricedSword(int wholesalePrice = 100)
    {
        var world = new World();
        world.EquipmentLookup["IronSword"] = new Equipment
        {
            RefName = "IronSword",
            DisplayName = "Iron Sword",
            WholesalePrice = wholesalePrice
            // MerchantMarkupMultiplier keeps its generated default of 1.5
        };
        return world;
    }

    private static SagaTransaction CreateTransaction(SagaTransactionType type, Dictionary<string, string>? data = null, string? extensionTypeName = null)
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
        var (isValid, reason) = SagaTransactionValidator.Validate(
            CreateStateWithLivingMerchant(),
            CreateTransaction(SagaTransactionType.StateSnapshot));

        Assert.False(isValid);
        Assert.Contains("server-generated", reason);
    }

    [Theory]
    [InlineData(SagaTransactionType.CurrencyChanged)]
    public void Validate_TypesWithoutClientProducer_Rejected(SagaTransactionType type)
    {
        var (isValid, _) = SagaTransactionValidator.Validate(
            CreateStateWithLivingMerchant(), CreateTransaction(type));

        Assert.False(isValid);
    }

    [Fact]
    public void Validate_ReputationChanged_RequiresFactionAndPlausibleAmount()
    {
        // Produced by dialogue ChangeReputation actions (incl. spillover)
        var valid = SagaTransactionValidator.Validate(
            CreateStateWithLivingMerchant(),
            CreateTransaction(SagaTransactionType.ReputationChanged, new Dictionary<string, string>
            {
                [TransactionDataKeys.FactionRef] = "CityGuard",
                [TransactionDataKeys.Amount] = "250"
            }));
        Assert.True(valid.IsValid, valid.Reason);

        var noFaction = SagaTransactionValidator.Validate(
            CreateStateWithLivingMerchant(),
            CreateTransaction(SagaTransactionType.ReputationChanged));
        Assert.False(noFaction.IsValid);

        var minted = SagaTransactionValidator.Validate(
            CreateStateWithLivingMerchant(),
            CreateTransaction(SagaTransactionType.ReputationChanged, new Dictionary<string, string>
            {
                [TransactionDataKeys.FactionRef] = "CityGuard",
                [TransactionDataKeys.Amount] = "999999"
            }));
        Assert.False(minted.IsValid);
    }

    [Theory]
    [InlineData(SagaTransactionType.EquipmentChanged)]
    [InlineData(SagaTransactionType.SagaDiscovered)]
    [InlineData(SagaTransactionType.TriggerActivated)]
    [InlineData(SagaTransactionType.BattleStarted)]
    public void Validate_TypesWithClientProducer_Allowed(SagaTransactionType type)
    {
        var (isValid, reason) = SagaTransactionValidator.Validate(
            CreateStateWithLivingMerchant(), CreateTransaction(type));

        Assert.True(isValid, reason);
    }

    [Fact]
    public void Validate_QuestTokenAwarded_RequiresTokenRef()
    {
        // Quest tokens are the progression currency — a bare award with no token
        // ref (or, with a world, an unknown ref) is a mint attempt
        var (isValid, _) = SagaTransactionValidator.Validate(
            CreateStateWithLivingMerchant(),
            CreateTransaction(SagaTransactionType.QuestTokenAwarded));
        Assert.False(isValid);

        var withRef = SagaTransactionValidator.Validate(
            CreateStateWithLivingMerchant(),
            CreateTransaction(SagaTransactionType.QuestTokenAwarded, new Dictionary<string, string>
            {
                [TransactionDataKeys.QuestTokenRef] = "TOKEN_TEST"
            }));
        Assert.True(withRef.IsValid, withRef.Reason);
    }

    [Fact]
    public void Validate_ExtensionWithoutTypeName_Rejected()
    {
        // Unparseable client type strings also land on Extension with no name
        var (isValid, _) = SagaTransactionValidator.Validate(
            CreateStateWithLivingMerchant(),
            CreateTransaction(SagaTransactionType.Extension));

        Assert.False(isValid);
    }

    [Fact]
    public void Validate_ExtensionWithTypeName_Allowed()
    {
        var (isValid, reason) = SagaTransactionValidator.Validate(
            CreateStateWithLivingMerchant(),
            CreateTransaction(SagaTransactionType.Extension, extensionTypeName: "MiningClaimed"));

        Assert.True(isValid, reason);
    }

    [Fact]
    public void Validate_BuyBelowCatalogFloor_Rejected()
    {
        // Wholesale 100 × markup 1.5 × max discount 0.72 = floor 108
        var (isValid, reason) = SagaTransactionValidator.Validate(
            CreateStateWithLivingMerchant(),
            CreateTransaction(SagaTransactionType.ItemTraded, TradeData(pricePerItem: 0, isBuying: true)),
            CreateWorldWithPricedSword());

        Assert.False(isValid);
        Assert.Contains("below the catalog minimum", reason);
    }

    [Fact]
    public void Validate_BuyAtCatalogPrice_Allowed()
    {
        var (isValid, reason) = SagaTransactionValidator.Validate(
            CreateStateWithLivingMerchant(),
            CreateTransaction(SagaTransactionType.ItemTraded, TradeData(pricePerItem: 150, isBuying: true)),
            CreateWorldWithPricedSword());

        Assert.True(isValid, reason);
    }

    [Fact]
    public void Validate_SellAboveWholesale_Rejected()
    {
        var (isValid, reason) = SagaTransactionValidator.Validate(
            CreateStateWithLivingMerchant(),
            CreateTransaction(SagaTransactionType.ItemTraded, TradeData(pricePerItem: 5000, isBuying: false)),
            CreateWorldWithPricedSword());

        Assert.False(isValid);
        Assert.Contains("exceeds the catalog sell price", reason);
    }

    [Fact]
    public void Validate_UntradeableSentinel_Rejected()
    {
        // WholesalePrice = int.MaxValue means "cannot be traded" (Economy.xsd)
        var (isValid, reason) = SagaTransactionValidator.Validate(
            CreateStateWithLivingMerchant(),
            CreateTransaction(SagaTransactionType.ItemTraded, TradeData(pricePerItem: 100, isBuying: false)),
            CreateWorldWithPricedSword(wholesalePrice: int.MaxValue));

        Assert.False(isValid);
        Assert.Contains("not tradeable", reason);
    }

    [Fact]
    public void Validate_TradeWithoutWorld_SkipsPriceCheckButKeepsAliveCheck()
    {
        // No world supplied: price bounds can't be derived, but state checks still run
        var (isValid, reason) = SagaTransactionValidator.Validate(
            CreateStateWithLivingMerchant(),
            CreateTransaction(SagaTransactionType.ItemTraded, TradeData(pricePerItem: 0, isBuying: true)));

        Assert.True(isValid, reason);

        var (deadValid, _) = SagaTransactionValidator.Validate(
            new SagaState(),
            CreateTransaction(SagaTransactionType.ItemTraded, TradeData(pricePerItem: 150, isBuying: true)));

        Assert.False(deadValid); // merchant not in state
    }

    // ===== Victory loot: the victor free-take exception =====

    private static SagaTransaction CreateVictoryTakeTransaction(string avatarId, int pricePerItem = 0, bool isBuying = true)
    {
        var tx = CreateTransaction(SagaTransactionType.ItemTraded, TradeData(pricePerItem, isBuying));
        tx.AvatarId = avatarId;
        return tx;
    }

    [Fact]
    public void Validate_VictorFreeTakeFromDefeatedCharacter_Allowed()
    {
        var victor = Guid.NewGuid().ToString();

        var (isValid, reason) = SagaTransactionValidator.Validate(
            CreateStateWithDefeatedCharacter(victor),
            CreateVictoryTakeTransaction(victor),
            CreateWorldWithPricedSword()); // world present: floor bound must NOT apply to the free take

        Assert.True(isValid, reason);
    }

    [Fact]
    public void Validate_NonVictorFreeTakeFromDefeatedCharacter_Rejected()
    {
        var (isValid, reason) = SagaTransactionValidator.Validate(
            CreateStateWithDefeatedCharacter(victorAvatarId: Guid.NewGuid().ToString()),
            CreateVictoryTakeTransaction(avatarId: Guid.NewGuid().ToString()),
            CreateWorldWithPricedSword());

        Assert.False(isValid);
        Assert.Contains("dead character", reason);
    }

    [Fact]
    public void Validate_ZeroPriceSellToDefeatedCharacter_Rejected()
    {
        // Even the victor cannot deposit INTO a corpse — free-take is buy-only
        var victor = Guid.NewGuid().ToString();

        var (isValid, _) = SagaTransactionValidator.Validate(
            CreateStateWithDefeatedCharacter(victor),
            CreateVictoryTakeTransaction(victor, pricePerItem: 0, isBuying: false),
            CreateWorldWithPricedSword());

        Assert.False(isValid);
    }

    [Fact]
    public void Validate_PricedBuyFromDefeatedCharacter_Rejected()
    {
        // Victory loot is zero-price BY RULE — a priced trade with a corpse stays invalid
        var victor = Guid.NewGuid().ToString();

        var (isValid, _) = SagaTransactionValidator.Validate(
            CreateStateWithDefeatedCharacter(victor),
            CreateVictoryTakeTransaction(victor, pricePerItem: 150),
            CreateWorldWithPricedSword());

        Assert.False(isValid);
    }

    [Fact]
    public void Validate_FreeTakeFromAliveCharacter_StillRejectedByPriceFloor()
    {
        // The victor exception applies to DEFEATED characters only: a zero-price buy
        // from a living merchant keeps hitting the catalog floor bound
        var (isValid, reason) = SagaTransactionValidator.Validate(
            CreateStateWithLivingMerchant(),
            CreateTransaction(SagaTransactionType.ItemTraded, TradeData(pricePerItem: 0, isBuying: true)),
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

        var (isValid, reason) = SagaTransactionValidator.Validate(
            CreateStateWithLivingMerchant(),
            CreateTransaction(SagaTransactionType.BattleTurnExecuted, data));

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

        var (isValid, _) = SagaTransactionValidator.Validate(
            CreateStateWithLivingMerchant(),
            CreateTransaction(SagaTransactionType.BattleTurnExecuted, data));

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

        var (isValid, reason) = SagaTransactionValidator.Validate(
            CreateStateWithLivingMerchant(),
            CreateTransaction(SagaTransactionType.BattleTurnExecuted, data));

        Assert.True(isValid, reason);
    }
}
