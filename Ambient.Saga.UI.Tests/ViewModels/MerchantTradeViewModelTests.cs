using Ambient.Domain;
using Ambient.Domain.Contracts;
using Ambient.Domain.DefinitionExtensions;
using Ambient.Domain.Entities;
using Ambient.Presentation.WindowsUI.RpgControls.ViewModels;
using Ambient.Saga.Engine.Application.Commands.Saga;
using Ambient.Saga.Engine.Application.Queries.Saga;
using Ambient.Saga.Engine.Application.Results.Saga;
using Ambient.Saga.Engine.Domain.Rpg.Sagas;
using Ambient.Saga.Engine.Domain.Rpg.Sagas.TransactionLog;
using MediatR;

namespace Ambient.Saga.UI.Tests.ViewModels;

/// <summary>
/// Stub mediator for testing MerchantTradeViewModel CQRS operations.
/// Simulates basic trade operations without actual persistence.
/// </summary>
public class StubMediator : IMediator
{
    public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
    {
        // Handle TradeItemCommand
        if (request is TradeItemCommand tradeCmd)
        {
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
                1L,
                null,
                avatar
            );
            return Task.FromResult((TResponse)(object)result);
        }

        // Handle GetSagaStateQuery
        if (request is GetSagaStateQuery)
        {
            var sagaState = new SagaState
            {
                SagaRef = "TestSaga",
                Status = SagaStatus.Active
            };
            return Task.FromResult((TResponse)(object)sagaState);
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

/// <summary>
/// Unit tests for MerchantTradeViewModel.
/// Tests CQRS-based trading, category management, and UI state.
/// </summary>
public class MerchantTradeViewModelTests
{
    private readonly IWorld _world;
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
        _context = CreateTestContext();
    }

    private SagaInteractionContext CreateTestContext()
    {
        return new SagaInteractionContext
        {
            World = _world,
            AvatarEntity = _player,
            ActiveCharacter = _merchant,
            CurrentSagaRef = "TestSaga",
            CurrentCharacterInstanceId = Guid.NewGuid()
        };
    }

    private IWorld CreateTestWorld()
    {
        var world = new World
        {
            WorldTemplate = new WorldTemplate
            {
                Gameplay = new GameplayComponents()
            }
        };

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
        return new AvatarEntity
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
    }

    private Character CreateTestMerchant()
    {
        return new Character
        {
            RefName = "TestMerchant",
            Interactable = new Interactable
            {
                Loot = new ItemCollection
                {
                    Equipment = new[]
                    {
                        new EquipmentEntry { EquipmentRef = "iron_sword", Condition = 1.0f },
                        new EquipmentEntry { EquipmentRef = "steel_armor", Condition = 0.8f }
                    },
                    Consumables = new[]
                    {
                        new ConsumableEntry { ConsumableRef = "health_potion", Quantity = 10 }
                    },
                    Blocks = new[]
                    {
                        new BlockEntry { BlockRef = "stone_block", Quantity = 100 }
                    },
                    Tools = new[]
                    {
                        new ToolEntry { ToolRef = "pickaxe", Condition = 1.0f }
                    },
                    Spells = new[]
                    {
                        new SpellEntry { SpellRef = "fireball", Condition = 1.0f }
                    }
                }
            }
        };
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_InitializesWithValidContext()
    {
        var viewModel = new MerchantTradeViewModel(_context, _mediator);

        Assert.NotNull(viewModel);
        Assert.Equal("Equipment", viewModel.SelectedTradeCategory);
        Assert.Equal("Buy", viewModel.TradeMode);
    }

    [Fact]
    public void Constructor_ThrowsOnNullContext()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new MerchantTradeViewModel(null!, _mediator));
    }

    [Fact]
    public void Constructor_ThrowsOnNullMediator()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new MerchantTradeViewModel(_context, null!));
    }

    [Fact]
    public void PlayerAvatar_ReturnsContextAvatar()
    {
        var viewModel = new MerchantTradeViewModel(_context, _mediator);

        Assert.Equal(_player, viewModel.PlayerAvatar);
    }

    #endregion

    #region Merchant Type Tests

    [Fact]
    public void ShowBuySellToggle_AlwaysReturnsTrue()
    {
        var viewModel = new MerchantTradeViewModel(_context, _mediator);

        Assert.True(viewModel.ShowBuySellToggle);
    }

    [Fact]
    public void IsMerchant_AlwaysReturnsTrue()
    {
        var viewModel = new MerchantTradeViewModel(_context, _mediator);

        Assert.True(viewModel.IsMerchant);
    }

    #endregion

    #region Category Availability Tests

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
    public void HasPotentialLoot_WithAnyItems_ReturnsTrue()
    {
        var viewModel = new MerchantTradeViewModel(_context, _mediator);

        Assert.True(viewModel.HasPotentialLoot);
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

    #endregion

    #region Trade Mode Tests

    [Fact]
    public void TradeMode_DefaultsToBuy()
    {
        var viewModel = new MerchantTradeViewModel(_context, _mediator);

        Assert.Equal("Buy", viewModel.TradeMode);
    }

    [Fact]
    public void TradeMode_SwitchingToSell_ChangesInventorySource()
    {
        var viewModel = new MerchantTradeViewModel(_context, _mediator);
        var buyInventory = viewModel.TradeInventory;

        viewModel.TradeMode = "Sell";

        // In sell mode with empty player inventory, should return empty
        Assert.Empty(viewModel.TradeInventory);
    }

    [Fact]
    public void TradeInventory_InBuyMode_ReturnsMerchantItems()
    {
        var viewModel = new MerchantTradeViewModel(_context, _mediator);
        viewModel.TradeMode = "Buy";

        var inventory = viewModel.TradeInventory;

        Assert.NotEmpty(inventory);
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

    #region Buy Item Tests

    [Fact]
    public async Task BuyItemCommand_WithSufficientCredits_DeductsCreditsAndAddsItem()
    {
        var viewModel = new MerchantTradeViewModel(_context, _mediator);
        var itemToBuy = viewModel.TradeInventory.First(i => i.Item.RefName == "iron_sword");
        var initialCredits = _player.Stats.Credits;

        await viewModel.BuyItemCommand.ExecuteAsync(itemToBuy);

        Assert.True(_player.Stats.Credits < initialCredits);
        Assert.Contains(_player.Capabilities.Equipment!, e => e.EquipmentRef == "iron_sword");
    }

    [Fact]
    public async Task BuyItemCommand_WithSufficientCredits_FiresStatusMessage()
    {
        var viewModel = new MerchantTradeViewModel(_context, _mediator);
        var itemToBuy = viewModel.TradeInventory.First(i => i.Item.RefName == "iron_sword");

        var statusMessageFired = false;
        viewModel.StatusMessageChanged += (s, e) => statusMessageFired = true;

        await viewModel.BuyItemCommand.ExecuteAsync(itemToBuy);

        Assert.True(statusMessageFired);
    }

    [Fact]
    public async Task BuyItemCommand_WithSufficientCredits_FiresActivityMessage()
    {
        var viewModel = new MerchantTradeViewModel(_context, _mediator);
        var itemToBuy = viewModel.TradeInventory.First(i => i.Item.RefName == "iron_sword");

        string? activityMessage = null;
        viewModel.ActivityMessageGenerated += (s, e) => activityMessage = e;

        await viewModel.BuyItemCommand.ExecuteAsync(itemToBuy);

        Assert.NotNull(activityMessage);
        Assert.Contains("Bought", activityMessage);
        Assert.Contains("Iron Sword", activityMessage);
    }

    [Fact]
    public async Task BuyItemCommand_WithInsufficientCredits_FiresErrorStatus()
    {
        _player.Stats.Credits = 10; // Not enough for any item
        var viewModel = new MerchantTradeViewModel(_context, _mediator);
        var itemToBuy = viewModel.TradeInventory.First(i => i.Item.RefName == "iron_sword");

        string? statusMessage = null;
        viewModel.StatusMessageChanged += (s, e) => statusMessage = e;

        await viewModel.BuyItemCommand.ExecuteAsync(itemToBuy);

        Assert.NotNull(statusMessage);
        Assert.Contains("Not enough money", statusMessage);
    }

    [Fact]
    public async Task BuyItemCommand_WithInsufficientCredits_DoesNotAddItem()
    {
        _player.Stats.Credits = 10;
        var viewModel = new MerchantTradeViewModel(_context, _mediator);
        var itemToBuy = viewModel.TradeInventory.First(i => i.Item.RefName == "iron_sword");

        await viewModel.BuyItemCommand.ExecuteAsync(itemToBuy);

        Assert.Empty(_player.Capabilities.Equipment!);
    }

    #endregion

    #region Sell Item Tests

    [Fact]
    public async Task SellItemCommand_WithItemInInventory_AddsCreditsAndRemovesItem()
    {
        _player.Capabilities.Equipment = new[]
        {
            new EquipmentEntry { EquipmentRef = "iron_sword", Condition = 1.0f }
        };
        var initialCredits = _player.Stats.Credits;

        var viewModel = new MerchantTradeViewModel(_context, _mediator);
        viewModel.TradeMode = "Sell";
        var itemToSell = viewModel.TradeInventory.First();

        await viewModel.SellItemCommand.ExecuteAsync(itemToSell);

        Assert.True(_player.Stats.Credits > initialCredits);
        Assert.Empty(_player.Capabilities.Equipment!);
    }

    [Fact]
    public async Task SellItemCommand_WithItemInInventory_FiresActivityMessage()
    {
        _player.Capabilities.Equipment = new[]
        {
            new EquipmentEntry { EquipmentRef = "iron_sword", Condition = 1.0f }
        };

        var viewModel = new MerchantTradeViewModel(_context, _mediator);
        viewModel.TradeMode = "Sell";
        var itemToSell = viewModel.TradeInventory.First();

        string? activityMessage = null;
        viewModel.ActivityMessageGenerated += (s, e) => activityMessage = e;

        await viewModel.SellItemCommand.ExecuteAsync(itemToSell);

        Assert.NotNull(activityMessage);
        Assert.Contains("Sold", activityMessage);
        Assert.Contains("Iron Sword", activityMessage);
    }

    [Fact]
    public void SellItemInventory_WithEmptyPlayerInventory_ReturnsEmpty()
    {
        var viewModel = new MerchantTradeViewModel(_context, _mediator);
        viewModel.TradeMode = "Sell";

        Assert.Empty(viewModel.TradeInventory);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void Constructor_WithNullWorld_DoesNotCrash()
    {
        var contextWithNullWorld = new SagaInteractionContext
        {
            World = null,
            AvatarEntity = _player,
            ActiveCharacter = _merchant
        };

        var viewModel = new MerchantTradeViewModel(contextWithNullWorld, _mediator);

        Assert.NotNull(viewModel);
        Assert.Empty(viewModel.TradeInventory);
    }

    [Fact]
    public async Task BuyItemCommand_WithNullAvatar_FiresStatusMessage()
    {
        var contextWithNullAvatar = new SagaInteractionContext
        {
            World = _world,
            AvatarEntity = null,
            ActiveCharacter = _merchant
        };
        var viewModel = new MerchantTradeViewModel(contextWithNullAvatar, _mediator);
        var itemToBuy = viewModel.TradeInventory.FirstOrDefault();

        string? statusMessage = null;
        viewModel.StatusMessageChanged += (s, e) => statusMessage = e;

        if (itemToBuy != null)
            await viewModel.BuyItemCommand.ExecuteAsync(itemToBuy);

        Assert.NotNull(statusMessage);
        Assert.Contains("missing", statusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task BuyItemCommand_WithNullSagaRef_FiresStatusMessage()
    {
        var contextWithNullSaga = new SagaInteractionContext
        {
            World = _world,
            AvatarEntity = _player,
            ActiveCharacter = _merchant,
            CurrentSagaRef = null,
            CurrentCharacterInstanceId = Guid.NewGuid()
        };
        var viewModel = new MerchantTradeViewModel(contextWithNullSaga, _mediator);
        var itemToBuy = viewModel.TradeInventory.First();

        string? statusMessage = null;
        viewModel.StatusMessageChanged += (s, e) => statusMessage = e;

        await viewModel.BuyItemCommand.ExecuteAsync(itemToBuy);

        Assert.NotNull(statusMessage);
        Assert.Contains("missing", statusMessage, StringComparison.OrdinalIgnoreCase);
    }

    #endregion

    #region Currency Name Tests

    [Fact]
    public void CurrencyName_ReturnsDefaultWhenNotSet()
    {
        var viewModel = new MerchantTradeViewModel(_context, _mediator);

        Assert.Equal("Credit", viewModel.CurrencyName);
    }

    [Fact]
    public void PluralCurrencyName_ReturnsDefaultWhenNotSet()
    {
        var viewModel = new MerchantTradeViewModel(_context, _mediator);

        Assert.Equal("Credits", viewModel.PluralCurrencyName);
    }

    #endregion
}
