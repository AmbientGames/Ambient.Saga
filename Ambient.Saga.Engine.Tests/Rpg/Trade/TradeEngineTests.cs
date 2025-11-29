using Ambient.Domain;
using Ambient.Domain.DefinitionExtensions;
using Ambient.Saga.Engine.Domain.Rpg.Trade;

namespace Ambient.Saga.Engine.Tests.Rpg.Trade;

public class TradeEngineTests
{
    private readonly World _world;
    private readonly TradeEngine _engine;

    public TradeEngineTests()
    {
        _world = CreateTestWorld();
        _engine = new TradeEngine(_world);
    }

    private World CreateTestWorld()
    {
        var world = new World();
        world.WorldTemplate = new WorldTemplate();
        world.WorldTemplate.Gameplay = new GameplayComponents();

        // Create test equipment
        world.Gameplay.Equipment = new[]
        {
            new Equipment
            {
                RefName = "iron_sword",
                DisplayName = "Iron Sword",
                WholesalePrice = 100,
                MerchantMarkupMultiplier = 1.5f
            },
            new Equipment
            {
                RefName = "steel_armor",
                DisplayName = "Steel Armor",
                WholesalePrice = 200,
                MerchantMarkupMultiplier = 2.0f
            }
        };

        // Create test consumables
        world.Gameplay.Consumables = new[]
        {
            new Consumable
            {
                RefName = "health_potion",
                DisplayName = "Health Potion",
                WholesalePrice = 50,
                MerchantMarkupMultiplier = 1.2f
            }
        };

        //// Create test blocks
        //world.DerivedBlockList = new[]
        //{
        //    new DerivedBlock
        //    {
        //        RefName = "stone_block",
        //        DisplayName = "Stone Block",
        //        WholesalePrice = 10,
        //        MerchantMarkupMultiplier = 1.1f
        //    }
        //};

        // Create test tools
        world.Gameplay.Tools = new[]
        {
            new Tool
            {
                RefName = "pickaxe",
                DisplayName = "Pickaxe",
                WholesalePrice = 75,
                MerchantMarkupMultiplier = 1.3f
            }
        };

        // Create test spells
        world.Gameplay.Spells = new[]
        {
            new Spell
            {
                RefName = "fireball",
                DisplayName = "Fireball",
                WholesalePrice = 150,
                MerchantMarkupMultiplier = 1.8f
            }
        };

        return world;
    }

    private AvatarBase CreateTestAvatar(float credits = 1000f)
    {
        var avatar = new AvatarBase();
        avatar.Stats = new CharacterStats();
        avatar.Stats.Credits = credits;
        avatar.Stats.Health = 100;
        avatar.Capabilities = new ItemCollection();
        avatar.Capabilities.Equipment = Array.Empty<EquipmentEntry>();
        avatar.Capabilities.Consumables = Array.Empty<ConsumableEntry>();
        avatar.Capabilities.Blocks = Array.Empty<BlockEntry>();
        avatar.Capabilities.Tools = Array.Empty<ToolEntry>();
        avatar.Capabilities.Spells = Array.Empty<SpellEntry>();
        return avatar;
    }

    private ItemCollection CreateMerchantInventory()
    {
        var inventory = new ItemCollection();
        inventory.Equipment = new[]
        {
            new EquipmentEntry { EquipmentRef = "iron_sword", Condition = 1.0f },
            new EquipmentEntry { EquipmentRef = "steel_armor", Condition = 0.8f }
        };
        inventory.Consumables = new[]
        {
            new ConsumableEntry { ConsumableRef = "health_potion", Quantity = 10 }
        };
        inventory.Blocks = new[]
        {
            new BlockEntry { BlockRef = "stone_block", Quantity = 100 }
        };
        inventory.Tools = new[]
        {
            new ToolEntry { ToolRef = "pickaxe", Condition = 1.0f }
        };
        inventory.Spells = new[]
        {
            new SpellEntry { SpellRef = "fireball", Condition = 1.0f }
        };
        return inventory;
    }

    #region Price Calculation Tests

    [Fact]
    public void CalculateBuyPrice_WithMerchant_AppliesMarkup()
    {
        var equipment = _world.Gameplay.Equipment[0]; // Iron Sword: 100 * 1.5 = 150

        var price = _engine.CalculateBuyPrice(equipment, isMerchant: true);

        Assert.Equal(150, price);
    }

    [Fact]
    public void CalculateBuyPrice_WithoutMerchant_ReturnsZero()
    {
        var equipment = _world.Gameplay.Equipment[0];

        var price = _engine.CalculateBuyPrice(equipment, isMerchant: false);

        Assert.Equal(0, price);
    }

    [Fact]
    public void CalculateSellPrice_ReturnsWholesalePrice()
    {
        var equipment = _world.Gameplay.Equipment[0]; // Iron Sword: 100

        var price = _engine.CalculateSellPrice(equipment);

        Assert.Equal(100, price);
    }

    #endregion

    #region GetCategoryItemCount Tests

    [Fact]
    public void GetCategoryItemCount_Equipment_ReturnsCorrectCount()
    {
        var inventory = CreateMerchantInventory();

        var count = _engine.GetCategoryItemCount(inventory, "Equipment");

        Assert.Equal(2, count);
    }

    [Fact]
    public void GetCategoryItemCount_Consumables_ReturnsCorrectCount()
    {
        var inventory = CreateMerchantInventory();

        var count = _engine.GetCategoryItemCount(inventory, "Consumables");

        Assert.Equal(1, count);
    }

    [Fact]
    public void GetCategoryItemCount_NullInventory_ReturnsZero()
    {
        var count = _engine.GetCategoryItemCount(null, "Equipment");

        Assert.Equal(0, count);
    }

    [Fact]
    public void GetCategoryItemCount_UnknownCategory_ReturnsZero()
    {
        var inventory = CreateMerchantInventory();

        var count = _engine.GetCategoryItemCount(inventory, "UnknownCategory");

        Assert.Equal(0, count);
    }

    #endregion

    #region GetAvailableItems Tests

    [Fact]
    public void GetAvailableItems_Equipment_ReturnsBuyPrices()
    {
        var inventory = CreateMerchantInventory();

        var items = _engine.GetAvailableItems(inventory, "Equipment", isBuying: true);

        Assert.Equal(2, items.Count);
        Assert.Equal("iron_sword", items[0].Item.RefName);
        Assert.Equal(150, items[0].Price); // 100 * 1.5
        Assert.Equal(1.0f, items[0].Condition);
    }

    [Fact]
    public void GetAvailableItems_Equipment_ReturnsSellPrices()
    {
        var inventory = CreateMerchantInventory();

        var items = _engine.GetAvailableItems(inventory, "Equipment", isBuying: false);

        Assert.Equal(2, items.Count);
        Assert.Equal("iron_sword", items[0].Item.RefName);
        Assert.Equal(100, items[0].Price); // Wholesale price
    }

    [Fact]
    public void GetAvailableItems_Consumables_ReturnsItemsWithQuantity()
    {
        var inventory = CreateMerchantInventory();

        var items = _engine.GetAvailableItems(inventory, "Consumables", isBuying: true);

        Assert.Single(items);
        Assert.Equal("health_potion", items[0].Item.RefName);
        Assert.Equal(60, items[0].Price); // 50 * 1.2
        Assert.Equal(10, items[0].Quantity);
    }

    [Fact]
    public void GetAvailableItems_Blocks_ReturnsItemsWithQuantity()
    {
        var inventory = CreateMerchantInventory();

        var items = _engine.GetAvailableItems(inventory, "Blocks", isBuying: true);

        Assert.Single(items);
        Assert.Equal("stone_block", items[0].Item.RefName);
        Assert.Equal(11, items[0].Price); // 10 * 1.1
        Assert.Equal(100, items[0].Quantity);
    }

    [Fact]
    public void GetAvailableItems_Tools_ReturnsItemsWithCondition()
    {
        var inventory = CreateMerchantInventory();

        var items = _engine.GetAvailableItems(inventory, "Tools", isBuying: true);

        Assert.Single(items);
        Assert.Equal("pickaxe", items[0].Item.RefName);
        Assert.Equal(97, items[0].Price); // 75 * 1.3 = 97.5, truncated to 97
        Assert.Equal(1.0f, items[0].Condition);
    }

    [Fact]
    public void GetAvailableItems_Spells_ReturnsItemsWithCondition()
    {
        var inventory = CreateMerchantInventory();

        var items = _engine.GetAvailableItems(inventory, "Spells", isBuying: true);

        Assert.Single(items);
        Assert.Equal("fireball", items[0].Item.RefName);
        Assert.Equal(270, items[0].Price); // 150 * 1.8
        Assert.Equal(1.0f, items[0].Condition);
    }

    #endregion

    #region BuyItem Tests

    [Fact]
    public void BuyItem_WithSufficientCredits_SuccessfullyTransfers()
    {
        var buyer = CreateTestAvatar(credits: 500);
        var seller = CreateMerchantInventory();

        var equipment = _world.Gameplay.Equipment[0]; // Iron Sword
        var itemInfo = new TradeItemInfo(equipment, 150);

        var result = _engine.BuyItem(buyer, seller, itemInfo);

        Assert.True(result.Success);
        Assert.Equal(350, buyer.Stats.Credits); // 500 - 150
        Assert.Single(buyer.Capabilities.Equipment);
        Assert.Equal("iron_sword", buyer.Capabilities.Equipment[0].EquipmentRef);
        Assert.Single(seller.Equipment); // Merchant now has only steel_armor
    }

    [Fact]
    public void BuyItem_InsufficientCredits_Fails()
    {
        var buyer = CreateTestAvatar(credits: 100);
        var seller = CreateMerchantInventory();

        var equipment = _world.Gameplay.Equipment[0]; // Iron Sword, costs 150
        var itemInfo = new TradeItemInfo(equipment, 150);

        var result = _engine.BuyItem(buyer, seller, itemInfo);

        Assert.False(result.Success);
        Assert.Contains("Not enough money", result.Message);
        Assert.Equal(100, buyer.Stats.Credits); // No change
        Assert.Empty(buyer.Capabilities.Equipment); // No item transferred
    }

    [Fact]
    public void BuyItem_Consumable_StacksQuantity()
    {
        var buyer = CreateTestAvatar();
        buyer.Capabilities.Consumables = new[]
        {
            new ConsumableEntry { ConsumableRef = "health_potion", Quantity = 5 }
        };
        var seller = CreateMerchantInventory();

        var consumable = _world.Gameplay.Consumables[0];
        var itemInfo = new TradeItemInfo(consumable, 60, quantity: 3);

        var result = _engine.BuyItem(buyer, seller, itemInfo);

        Assert.True(result.Success);
        Assert.Equal(940, buyer.Stats.Credits); // 1000 - 60
        Assert.Single(buyer.Capabilities.Consumables);
        Assert.Equal(8, buyer.Capabilities.Consumables[0].Quantity); // 5 + 3
        Assert.Equal(7, seller.Consumables[0].Quantity); // 10 - 3
    }

    //[Fact]
    //public void BuyItem_Block_StacksQuantity()
    //{
    //    var buyer = CreateTestAvatar();
    //    var seller = CreateMerchantInventory();

    //    var block = _world.DerivedBlockList[0];
    //    var itemInfo = new TradeItemInfo(block, 11, quantity: 20);

    //    var result = _engine.BuyItem(buyer, seller, itemInfo);

    //    Assert.True(result.Success);
    //    Assert.Equal(989, buyer.Stats.Credits); // 1000 - 11
    //    Assert.Single(buyer.Capabilities.Blocks);
    //    Assert.Equal(20, buyer.Capabilities.Blocks[0].Quantity);
    //    Assert.Equal(80, seller.Blocks[0].Quantity); // 100 - 20
    //}

    [Fact]
    public void BuyItem_MissingStats_Fails()
    {
        var buyer = CreateTestAvatar();
        buyer.Stats = null!;
        var seller = CreateMerchantInventory();

        var equipment = _world.Gameplay.Equipment[0];
        var itemInfo = new TradeItemInfo(equipment, 150);

        var result = _engine.BuyItem(buyer, seller, itemInfo);

        Assert.False(result.Success);
        Assert.Contains("Missing buyer data", result.Message);
    }

    #endregion

    #region SellItem Tests

    [Fact]
    public void SellItem_SuccessfullyTransfers()
    {
        var seller = CreateTestAvatar(credits: 100);
        seller.Capabilities.Equipment = new[]
        {
            new EquipmentEntry { EquipmentRef = "iron_sword", Condition = 0.9f }
        };
        var buyer = CreateMerchantInventory();

        var equipment = _world.Gameplay.Equipment[0];
        var itemInfo = new TradeItemInfo(equipment, 100, condition: 0.9f);

        var result = _engine.SellItem(seller, buyer, itemInfo);

        Assert.True(result.Success);
        Assert.Equal(200, seller.Stats.Credits); // 100 + 100
        Assert.Empty(seller.Capabilities.Equipment); // Seller no longer has sword
        Assert.Equal(3, buyer.Equipment.Length); // Buyer now has 3 equipment pieces
    }

    [Fact]
    public void SellItem_Consumable_ReducesQuantity()
    {
        var seller = CreateTestAvatar();
        seller.Capabilities.Consumables = new[]
        {
            new ConsumableEntry { ConsumableRef = "health_potion", Quantity = 5 }
        };
        var buyer = CreateMerchantInventory();

        var consumable = _world.Gameplay.Consumables[0];
        var itemInfo = new TradeItemInfo(consumable, 50, quantity: 2);

        var result = _engine.SellItem(seller, buyer, itemInfo);

        Assert.True(result.Success);
        Assert.Equal(1050, seller.Stats.Credits); // 1000 + 50
        Assert.Single(seller.Capabilities.Consumables);
        Assert.Equal(3, seller.Capabilities.Consumables[0].Quantity); // 5 - 2
        Assert.Equal(12, buyer.Consumables[0].Quantity); // 10 + 2
    }

    [Fact]
    public void SellItem_InsufficientQuantity_Fails()
    {
        var seller = CreateTestAvatar();
        seller.Capabilities.Consumables = new[]
        {
            new ConsumableEntry { ConsumableRef = "health_potion", Quantity = 1 }
        };
        var buyer = CreateMerchantInventory();

        var consumable = _world.Gameplay.Consumables[0];
        var itemInfo = new TradeItemInfo(consumable, 50, quantity: 5); // Trying to sell 5 but only has 1

        var result = _engine.SellItem(seller, buyer, itemInfo);

        Assert.False(result.Success);
        Assert.Contains("Insufficient quantity", result.Message);
        Assert.Equal(1000, seller.Stats.Credits); // No change
    }

    [Fact]
    public void SellItem_ItemNotInInventory_Fails()
    {
        var seller = CreateTestAvatar();
        var buyer = CreateMerchantInventory();

        var equipment = _world.Gameplay.Equipment[0]; // Seller doesn't have this
        var itemInfo = new TradeItemInfo(equipment, 100);

        var result = _engine.SellItem(seller, buyer, itemInfo);

        Assert.False(result.Success);
        Assert.Contains("not found", result.Message);
    }

    #endregion

    #region Integration Tests

    [Fact]
    public void CompleteTradeScenario_BuyAndSell()
    {
        // Player starts with 1000 credits
        var player = CreateTestAvatar(credits: 1000);
        var merchant = CreateMerchantInventory();

        // Player buys an iron sword for 150
        var equipment = _world.Gameplay.Equipment[0];
        var buyItemInfo = new TradeItemInfo(equipment, 150);
        var buyResult = _engine.BuyItem(player, merchant, buyItemInfo);

        Assert.True(buyResult.Success);
        Assert.Equal(850, player.Stats.Credits);
        Assert.Single(player.Capabilities.Equipment);

        // Player sells the sword back for 100 (wholesale price)
        var sellItemInfo = new TradeItemInfo(equipment, 100, condition: 1.0f);
        var sellResult = _engine.SellItem(player, merchant, sellItemInfo);

        Assert.True(sellResult.Success);
        Assert.Equal(950, player.Stats.Credits); // 850 + 100
        Assert.Empty(player.Capabilities.Equipment);
        Assert.Equal(2, merchant.Equipment.Length); // Back to 2 items
    }

    [Fact]
    public void MultipleConsumablePurchases_StacksCorrectly()
    {
        var player = CreateTestAvatar();
        var merchant = CreateMerchantInventory();

        var consumable = _world.Gameplay.Consumables[0];

        // Buy 3 potions
        var buyResult1 = _engine.BuyItem(player, merchant, new TradeItemInfo(consumable, 60, quantity: 3));
        Assert.True(buyResult1.Success);
        Assert.Equal(3, player.Capabilities.Consumables[0].Quantity);

        // Buy 2 more potions
        var buyResult2 = _engine.BuyItem(player, merchant, new TradeItemInfo(consumable, 60, quantity: 2));
        Assert.True(buyResult2.Success);
        Assert.Equal(5, player.Capabilities.Consumables[0].Quantity);
        Assert.Equal(5, merchant.Consumables[0].Quantity); // 10 - 3 - 2
    }

    #endregion
}
