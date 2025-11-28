using Ambient.Domain;
using Ambient.Domain.DefinitionExtensions;
using Ambient.Domain.Entities;
using Ambient.Presentation.WindowsUI.RpgControls.ViewModels;
using Ambient.SagaEngine.Application.Commands.Saga;
using Ambient.SagaEngine.Application.Results.Saga;
using Ambient.SagaEngine.Domain.Rpg.Sagas;
using MediatR;

namespace Ambient.Saga.Sandbox.Tests.ViewModels;

// Simple stub mediator for testing that simulates basic trade operations
public class StubMediator : IMediator
{
    public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
    {
        // For TradeItemCommand, simulate a successful trade by returning a success result
        if (request is TradeItemCommand tradeCmd)
        {
            // Simulate the trade by directly modifying the avatar's state
            var avatar = tradeCmd.Avatar;
            var totalCost = tradeCmd.PricePerItem * tradeCmd.Quantity;

            if (tradeCmd.IsBuying)
            {
                // Check if avatar has enough credits
                if (avatar.Stats.Credits < totalCost)
                {
                    var failureResult = SagaCommandResult.Failure(
                        Guid.Empty,
                        "Not enough money"
                    );
                    return Task.FromResult((TResponse)(object)failureResult);
                }

                // Buying: deduct credits and add item to inventory
                avatar.Stats.Credits -= totalCost;

                // Add equipment to avatar
                var equipmentList = avatar.Capabilities.Equipment?.ToList() ?? new List<EquipmentEntry>();
                equipmentList.Add(new EquipmentEntry { EquipmentRef = tradeCmd.ItemRef, Condition = 1.0f });
                avatar.Capabilities.Equipment = equipmentList.ToArray();
            }
            else
            {
                // Selling: check if item exists in inventory
                var equipmentList = avatar.Capabilities.Equipment?.ToList() ?? new List<EquipmentEntry>();
                var itemToRemove = equipmentList.FirstOrDefault(e => e.EquipmentRef == tradeCmd.ItemRef);

                if (itemToRemove == null)
                {
                    var failureResult = SagaCommandResult.Failure(
                        Guid.Empty,
                        "Item not found in inventory"
                    );
                    return Task.FromResult((TResponse)(object)failureResult);
                }

                // Selling: add credits and remove item from inventory
                avatar.Stats.Credits += totalCost;
                equipmentList.Remove(itemToRemove);
                avatar.Capabilities.Equipment = equipmentList.ToArray();
            }

            // Return a successful result
            var result = SagaCommandResult.Success(
                Guid.NewGuid(),
                new List<Guid> { Guid.NewGuid() },
                1L, // sequence number
                null, // data
                avatar // updated avatar
            );
            return Task.FromResult((TResponse)(object)result);
        }

        return Task.FromResult(default(TResponse)!);
    }

    public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest
    {
        return Task.CompletedTask;
    }

    public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public Task<object?> Send(object request, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<object?>(null);
    }

    public Task Publish(object notification, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default) where TNotification : INotification
    {
        return Task.CompletedTask;
    }
}

public class MerchantTradeViewModelTests
{
    private readonly World _world;
    private readonly AvatarEntity _player;
    private readonly Character _merchant;
    private readonly SagaInteractionContext _context;
    private readonly IMediator _mediator;

    public MerchantTradeViewModelTests()
    {
        _world = CreateTestWorld();
        _player = CreateTestPlayer();
        _merchant = CreateTestMerchant();
        _mediator = new StubMediator();
        _context = new SagaInteractionContext
        {
            World = _world,
            AvatarEntity = _player,
            ActiveCharacter = _merchant
        };
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

    private AvatarEntity CreateTestPlayer(float credits = 1000f)
    {
        var player = new AvatarEntity
        {
            Id = Guid.NewGuid(),
            AvatarId = Guid.NewGuid(),
            Stats = new CharacterStats
            {
                Credits = credits,
                Health = 1.0f
            },
            Capabilities = new ItemCollection
            {
                Equipment = Array.Empty<EquipmentEntry>(),
                Consumables = Array.Empty<ConsumableEntry>(),
                Blocks = Array.Empty<BlockEntry>(),
                Tools = Array.Empty<ToolEntry>(),
                Spells = Array.Empty<SpellEntry>()
            }
        };
        return player;
    }

    private Character CreateTestMerchant()
    {
        var merchant = new Character();

        // Merchant inventory goes in Interactable.Loot (what they HAVE to sell)
        merchant.Interactable = new Interactable();
        merchant.Interactable.Loot = new ItemCollection();
        merchant.Interactable.Loot.Equipment = new[]
        {
            new EquipmentEntry { EquipmentRef = "iron_sword", Condition = 1.0f },
            new EquipmentEntry { EquipmentRef = "steel_armor", Condition = 0.8f }
        };
        merchant.Interactable.Loot.Consumables = new[]
        {
            new ConsumableEntry { ConsumableRef = "health_potion", Quantity = 10 }
        };
        merchant.Interactable.Loot.Blocks = new[]
        {
            new BlockEntry { BlockRef = "stone_block", Quantity = 100 }
        };
        merchant.Interactable.Loot.Tools = new[]
        {
            new ToolEntry { ToolRef = "pickaxe", Condition = 1.0f }
        };
        merchant.Interactable.Loot.Spells = new[]
        {
            new SpellEntry { SpellRef = "fireball", Condition = 1.0f }
        };
        return merchant;
    }

    #region Initialization Tests

    [Fact]
    public void Constructor_InitializesWithValidContext()
    {
        var viewModel = new MerchantTradeViewModel(_context, _mediator);

        Assert.NotNull(viewModel);
        Assert.Equal("Equipment", viewModel.SelectedTradeCategory);
        Assert.Equal("Buy", viewModel.TradeMode);
    }

    [Fact]
    public void PlayerAvatar_ReturnsContextAvatar()
    {
        var viewModel = new MerchantTradeViewModel(_context, _mediator);

        Assert.Equal(_player, viewModel.PlayerAvatar);
    }

    #endregion

    #region Category Tests

    [Fact]
    public void HasEquipment_WithMerchantInventory_ReturnsTrue()
    {
        var viewModel = new MerchantTradeViewModel(_context, _mediator);

        Assert.True(viewModel.HasEquipment);
    }

    [Fact]
    public void HasConsumables_WithMerchantInventory_ReturnsTrue()
    {
        var viewModel = new MerchantTradeViewModel(_context, _mediator);

        Assert.True(viewModel.HasConsumables);
    }

    [Fact]
    public void HasBlocks_WithMerchantInventory_ReturnsTrue()
    {
        var viewModel = new MerchantTradeViewModel(_context, _mediator);

        Assert.True(viewModel.HasBlocks);
    }

    [Fact]
    public void HasTools_WithMerchantInventory_ReturnsTrue()
    {
        var viewModel = new MerchantTradeViewModel(_context, _mediator);

        Assert.True(viewModel.HasTools);
    }

    [Fact]
    public void HasSpells_WithMerchantInventory_ReturnsTrue()
    {
        var viewModel = new MerchantTradeViewModel(_context, _mediator);

        Assert.True(viewModel.HasSpells);
    }

    [Fact]
    public void AvailableCategories_WithFullMerchantInventory_ReturnsAllCategories()
    {
        var viewModel = new MerchantTradeViewModel(_context, _mediator);

        var categories = viewModel.AvailableCategories;
        Assert.Equal(5, categories.Count);
        Assert.Contains("Equipment", categories);
        Assert.Contains("Consumables", categories);
        Assert.Contains("Blocks", categories);
        Assert.Contains("Tools", categories);
        Assert.Contains("Spells", categories);
    }

    [Fact]
    public void ShowCategorySelector_WithMultipleCategories_ReturnsTrue()
    {
        var viewModel = new MerchantTradeViewModel(_context, _mediator);

        Assert.True(viewModel.ShowCategorySelector);
    }

    [Fact]
    public void HasPotentialLoot_WithAnyItems_ReturnsTrue()
    {
        var viewModel = new MerchantTradeViewModel(_context, _mediator);

        Assert.True(viewModel.HasPotentialLoot);
    }

    #endregion

    #region Trade Mode Tests

    [Fact]
    public void TradeMode_SwitchingToSell_RefreshesInventory()
    {
        var viewModel = new MerchantTradeViewModel(_context, _mediator);
        var initialInventory = viewModel.TradeInventory;

        viewModel.TradeMode = "Sell";

        Assert.NotEqual(initialInventory, viewModel.TradeInventory);
    }

    [Fact]
    public void TradeInventory_InBuyMode_ReturnsMerchantItems()
    {
        var viewModel = new MerchantTradeViewModel(_context, _mediator);
        viewModel.TradeMode = "Buy";

        var inventory = viewModel.TradeInventory;

        Assert.NotEmpty(inventory);
        // In buy mode, should show merchant's equipment
        Assert.True(inventory.Any(i => i.Item.RefName == "iron_sword"));
    }

    [Fact]
    public void TradeInventory_InSellMode_ReturnsPlayerItems()
    {
        // Give player an item
        _player.Capabilities.Equipment = new[]
        {
            new EquipmentEntry { EquipmentRef = "iron_sword", Condition = 1.0f }
        };

        var viewModel = new MerchantTradeViewModel(_context, _mediator);
        viewModel.TradeMode = "Sell";

        var inventory = viewModel.TradeInventory;

        Assert.Single(inventory);
        Assert.Equal("iron_sword", inventory[0].Item.RefName);
    }

    #endregion

    #region RefreshCategories Tests

    [Fact]
    public void RefreshCategories_AutoSelectsFirstAvailableCategory()
    {
        var viewModel = new MerchantTradeViewModel(_context, _mediator);
        viewModel.SelectedTradeCategory = "InvalidCategory";

        viewModel.RefreshCategories();

        Assert.Equal("Equipment", viewModel.SelectedTradeCategory);
    }

    [Fact]
    public void RefreshCategories_KeepsValidCategorySelection()
    {
        var viewModel = new MerchantTradeViewModel(_context, _mediator);
        viewModel.SelectedTradeCategory = "Consumables";

        viewModel.RefreshCategories();

        Assert.Equal("Consumables", viewModel.SelectedTradeCategory);
    }

    #endregion

    #region BuyItem Tests

    //[Fact]
    //public void BuyItem_WithSufficientCredits_SuccessfullyPurchases()
    //{
    //    var viewModel = new MerchantTradeViewModel(_context, _mediator);
    //    var itemToBuy = viewModel.TradeInventory.First(i => i.Item.RefName == "iron_sword");

    //    // var itemTransferredFired = false; // Event removed - now uses CQRS
    //    var statusChangedFired = false;
    //    var activityMessageFired = false;

    //    // viewModel.ItemTransferred += (s, e) => itemTransferredFired = true; // Event removed - now uses CQRS
    //    viewModel.StatusMessageChanged += (s, e) => statusChangedFired = true;
    //    viewModel.ActivityMessageGenerated += (s, e) => activityMessageFired = true;

    //    viewModel.BuyItemCommand.Execute(itemToBuy);

    //    Assert.Single(_player.Capabilities.Equipment);
    //    Assert.Equal("iron_sword", _player.Capabilities.Equipment[0].EquipmentRef);
    //    Assert.Equal(850, _player.Stats.Credits); // 1000 - 150
    //    // Assert.True(itemTransferredFired); // Event removed - now uses CQRS
    //    Assert.True(statusChangedFired);
    //    Assert.True(activityMessageFired);
    //}

    //[Fact]
    //public void BuyItem_WithInsufficientCredits_FiresStatusMessageOnly()
    //{
    //    _player.Stats.Credits = 50; // Not enough for iron sword (150)
    //    var viewModel = new MerchantTradeViewModel(_context, _mediator);
    //    var itemToBuy = viewModel.TradeInventory.First(i => i.Item.RefName == "iron_sword");

    //    var statusChangedFired = false;
    //    string? statusMessage = null;

    //    viewModel.StatusMessageChanged += (s, e) =>
    //    {
    //        statusChangedFired = true;
    //        statusMessage = e;
    //    };

    //    viewModel.BuyItemCommand.Execute(itemToBuy);

    //    Assert.Empty(_player.Capabilities.Equipment);
    //    Assert.Equal(50, _player.Stats.Credits); // No change
    //    Assert.True(statusChangedFired);
    //    Assert.Contains("Not enough money", statusMessage);
    //}

    //[Fact]
    //public void BuyItem_Consumable_GeneratesCorrectActivityMessage()
    //{
    //    var viewModel = new MerchantTradeViewModel(_context, _mediator);
    //    viewModel.SelectedTradeCategory = "Consumables";
    //    var itemToBuy = viewModel.TradeInventory.First(i => i.Item.RefName == "health_potion");

    //    string? activityMessage = null;
    //    viewModel.ActivityMessageGenerated += (s, e) => activityMessage = e;

    //    viewModel.BuyItemCommand.Execute(itemToBuy);

    //    Assert.NotNull(activityMessage);
    //    Assert.Contains("Bought Health Potion", activityMessage);
    //    Assert.Contains("60", activityMessage); // 50 * 1.2 = 60
    //}

    // [Fact]
    // public void BuyItem_FiresItemTransferredEventWithCorrectData()
    // {
    //     // NOTE: ItemTransferred event removed - ViewModel now uses CQRS for trade operations
    //     var viewModel = new MerchantTradeViewModel(_context, _mediator);
    //     var itemToBuy = viewModel.TradeInventory.First(i => i.Item.RefName == "iron_sword");

    //     ItemTransferredEventArgs? eventArgs = null;
    //     viewModel.ItemTransferred += (s, e) => eventArgs = e;

    //     viewModel.BuyItemCommand.Execute(itemToBuy);

    //     Assert.NotNull(eventArgs);
    //     Assert.Equal("Iron Sword", eventArgs.ItemName);
    //     Assert.Equal(150, eventArgs.Price);
    //     Assert.True(eventArgs.WasPurchase);
    // }

    #endregion

    #region SellItem Tests

    //[Fact]
    //public void SellItem_SuccessfullyTransfersAndCreditsPlayer()
    //{
    //    _player.Capabilities.Equipment = new[]
    //    {
    //        new EquipmentEntry { EquipmentRef = "iron_sword", Condition = 1.0f }
    //    };

    //    var viewModel = new MerchantTradeViewModel(_context, _mediator);
    //    viewModel.TradeMode = "Sell";
    //    var itemToSell = viewModel.TradeInventory.First();

    //    // var itemTransferredFired = false; // Event removed - now uses CQRS
    //    // viewModel.ItemTransferred += (s, e) => itemTransferredFired = true; // Event removed - now uses CQRS

    //    viewModel.SellItemCommand.Execute(itemToSell);

    //    Assert.Empty(_player.Capabilities.Equipment);
    //    Assert.Equal(1100, _player.Stats.Credits); // 1000 + 100 wholesale
    //    // Assert.True(itemTransferredFired); // Event removed - now uses CQRS
    //}

    // [Fact]
    // public void SellItem_FiresItemTransferredEventWithCorrectData()
    // {
    //     // NOTE: ItemTransferred event removed - ViewModel now uses CQRS for trade operations
    //     _player.Capabilities.Equipment = new[]
    //     {
    //         new EquipmentEntry { EquipmentRef = "iron_sword", Condition = 1.0f }
    //     };

    //     var viewModel = new MerchantTradeViewModel(_context, _mediator);
    //     viewModel.TradeMode = "Sell";
    //     var itemToSell = viewModel.TradeInventory.First();

    //     ItemTransferredEventArgs? eventArgs = null;
    //     viewModel.ItemTransferred += (s, e) => eventArgs = e;

    //     viewModel.SellItemCommand.Execute(itemToSell);

    //     Assert.NotNull(eventArgs);
    //     Assert.Equal("Iron Sword", eventArgs.ItemName);
    //     Assert.Equal(100, eventArgs.Price); // Wholesale price
    //     Assert.False(eventArgs.WasPurchase);
    // }

    [Fact]
    public void SellItem_WithEmptyInventory_DoesNotCrash()
    {
        var viewModel = new MerchantTradeViewModel(_context, _mediator);
        viewModel.TradeMode = "Sell";

        var inventory = viewModel.TradeInventory;

        Assert.Empty(inventory);
    }

    //[Fact]
    //public void SellItem_GeneratesCorrectActivityMessage()
    //{
    //    _player.Capabilities.Equipment = new[]
    //    {
    //        new EquipmentEntry { EquipmentRef = "iron_sword", Condition = 1.0f }
    //    };

    //    var viewModel = new MerchantTradeViewModel(_context, _mediator);
    //    viewModel.TradeMode = "Sell";
    //    var itemToSell = viewModel.TradeInventory.First();

    //    string? activityMessage = null;
    //    viewModel.ActivityMessageGenerated += (s, e) => activityMessage = e;

    //    viewModel.SellItemCommand.Execute(itemToSell);

    //    Assert.NotNull(activityMessage);
    //    Assert.Contains("Sold Iron Sword", activityMessage);
    //    Assert.Contains("100", activityMessage);
    //}

    #endregion

    #region Merchant Type Tests

    [Fact]
    public void ShowBuySellToggle_AlwaysReturnsTrue()
    {
        // MerchantTradeViewModel is only instantiated for merchant interactions,
        // so these properties always return true
        var viewModel = new MerchantTradeViewModel(_context, _mediator);

        Assert.True(viewModel.ShowBuySellToggle);
        Assert.True(viewModel.IsMerchant);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void Constructor_WithNullWorld_DoesNotCrash()
    {
        var contextWithNullWorld = new SagaInteractionContext
        {
            World = null,
            AvatarEntity = _player
        };

        var viewModel = new MerchantTradeViewModel(contextWithNullWorld, _mediator);

        Assert.NotNull(viewModel);
    }

    [Fact]
    public void BuyItem_WithNullAvatar_FiresStatusMessage()
    {
        var contextWithNullAvatar = new SagaInteractionContext
        {
            World = _world,
            AvatarEntity = null,
            ActiveCharacter = _merchant
        };
        var viewModel = new MerchantTradeViewModel(contextWithNullAvatar, _mediator);
        var itemToBuy = viewModel.TradeInventory.FirstOrDefault();

        var statusChangedFired = false;
        viewModel.StatusMessageChanged += (s, e) => statusChangedFired = true;

        if (itemToBuy != null)
            viewModel.BuyItemCommand.Execute(itemToBuy);

        Assert.True(statusChangedFired);
    }

    #endregion
}
