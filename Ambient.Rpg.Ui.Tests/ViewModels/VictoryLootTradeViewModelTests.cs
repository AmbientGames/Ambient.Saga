using Ambient.Domain;
using Ambient.Domain.Contracts;
using Ambient.Domain.Partials;
using Ambient.Domain.Entities;
using Ambient.Presentation.WindowsUI.RpgControls.ViewModels;
using Ambient.Rpg.Engine.Application.Commands.Arcs;
using Ambient.Rpg.Engine.Application.Queries.Arcs;
using Ambient.Rpg.Engine.Application.Results.Arcs;
using Ambient.Rpg.Engine.Domain.Arcs;
using Ambient.Rpg.Engine.Domain.Arcs.TransactionLog;
using MediatR;

namespace Ambient.Rpg.Ui.Tests.ViewModels;

/// <summary>
/// VM-seam tests for the VICTORY LOOT trade mode (defeated character's remaining
/// Interactable.Loot, free-takeable by its victor). Seeded content drives the exact
/// seams MerchantTradeModal exercises:
/// - RefreshArcStateAsync loads the arc-REPLAYED remaining inventory (the drop as
///   it stands after prior takes), not the template Loot fallback,
/// - Take sends TradeItemCommand with IsBuying=true and PricePerItem=0 (the shape
///   the victor exception in TradeItemHandler / ArcTransactionValidator accepts),
/// - the mode is take-only (no sell/deposit into a corpse) and visually distinct
///   from the cache modes.
/// </summary>
public class VictoryLootTradeViewModelTests
{
    private static readonly Guid DefeatedInstanceId = Guid.NewGuid();

    /// <summary>
    /// Mediator stub that serves a replayed arc state containing the DEFEATED
    /// character with its remaining loot, and records every TradeItemCommand sent.
    /// </summary>
    private sealed class VictoryLootStubMediator : IMediator
    {
        public List<TradeItemCommand> SentTradeCommands { get; } = new();

        /// <summary>Remaining (replayed) drop — deliberately SMALLER than the template Loot.</summary>
        public ItemCollection RemainingInventory { get; set; } = new()
        {
            Consumables = new[]
            {
                new ConsumableEntry { ConsumableRef = "health_potion", Quantity = 1 }
            }
        };

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            if (request is TradeItemCommand tradeCmd)
            {
                SentTradeCommands.Add(tradeCmd);
                var result = ArcCommandResult.Success(
                    Guid.NewGuid(), new List<Guid> { Guid.NewGuid() }, 1L, null, tradeCmd.Avatar as AvatarEntity);
                return Task.FromResult((TResponse)(object)result);
            }

            if (request is GetArcStateQuery)
            {
                var state = new ArcState { ArcRef = "TestArc", Status = ArcStatus.Active };
                state.Characters[DefeatedInstanceId.ToString()] = new CharacterState
                {
                    CharacterInstanceId = DefeatedInstanceId,
                    CharacterRef = "Bandit",
                    IsSpawned = true,
                    IsAlive = false,
                    DefeatedByAvatarId = "victor",
                    CurrentInventory = RemainingInventory
                };
                return Task.FromResult((TResponse)(object)state);
            }

            return Task.FromResult(default(TResponse)!);
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest
            => Task.CompletedTask;
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
        public Task<object?> Send(object request, CancellationToken cancellationToken = default)
            => Task.FromResult<object?>(null);
        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default) where TNotification : INotification
            => Task.CompletedTask;
    }

    private static IWorld CreateTestWorld()
    {
        var world = new World
        {
            WorldTemplate = new WorldTemplate { Gameplay = new GameplayComponents() }
        };
        world.Gameplay.Consumables = new[]
        {
            new Consumable
            {
                RefName = "health_potion",
                DisplayName = "Health Potion",
                BaseValue = 50,
                MerchantMarkupMultiplier = 1.2f
            },
            new Consumable
            {
                RefName = "bandage",
                DisplayName = "Bandage",
                BaseValue = 10,
                MerchantMarkupMultiplier = 1.2f
            }
        };
        return world;
    }

    private static Character CreateDefeatedBanditTemplate() => new()
    {
        RefName = "Bandit",
        // Template Loot: what a FRESH kill would drop — the replayed remaining
        // inventory (one potion) must win over this fallback once loaded.
        Interactable = new Interactable
        {
            Loot = new ItemCollection
            {
                Consumables = new[]
                {
                    new ConsumableEntry { ConsumableRef = "health_potion", Quantity = 2 },
                    new ConsumableEntry { ConsumableRef = "bandage", Quantity = 3 }
                }
            }
        }
    };

    private static AvatarEntity CreateVictorAvatar() => new()
    {
        Id = Guid.NewGuid(),
        AvatarId = Guid.NewGuid(),
        Stats = new CharacterStats { Credits = 100, Health = 1.0f },
        Capabilities = new ItemCollection()
    };

    private static (MerchantTradeViewModel Vm, VictoryLootStubMediator Mediator, AvatarEntity Avatar) CreateVictoryLootVm()
    {
        var world = CreateTestWorld();
        var avatar = CreateVictorAvatar();
        var mediator = new VictoryLootStubMediator();
        var context = new ArcInteractionContext
        {
            World = world,
            AvatarEntity = avatar,
            ActiveCharacter = CreateDefeatedBanditTemplate(),
            CurrentArcRef = "TestArc",
            CurrentCharacterInstanceId = DefeatedInstanceId
        };
        var vm = new MerchantTradeViewModel(context, mediator, isCache: false, isVictoryLoot: true);
        return (vm, mediator, avatar);
    }

    [Fact]
    public void VictoryLootMode_IsTakeOnlyAndFree()
    {
        var (vm, _, _) = CreateVictoryLootVm();

        Assert.True(vm.IsVictoryLoot);
        Assert.True(vm.IsFreeTake);
        Assert.False(vm.IsMerchant);
        Assert.False(vm.ShowBuySellToggle); // no deposit side at all
        Assert.False(vm.ShowMoneyBar);
        Assert.False(vm.ShowPrices);
    }

    [Fact]
    public void VictoryLootMode_LabelsDistinguishItFromCaches()
    {
        var (vm, _, _) = CreateVictoryLootVm();
        var cacheVm = new MerchantTradeViewModel(new ArcInteractionContext
        {
            World = CreateTestWorld(),
            AvatarEntity = CreateVictorAvatar(),
            ActiveCharacter = CreateDefeatedBanditTemplate(),
            CurrentArcRef = "TestArc",
            CurrentCharacterInstanceId = DefeatedInstanceId
        }, new VictoryLootStubMediator(), isCache: true);

        // A defeat drop must never read as "Cache" / "Geocache" / "Remnant Loot"
        Assert.Equal("- Defeated", vm.HeaderSubtitle);
        Assert.NotEqual(cacheVm.HeaderSubtitle, vm.HeaderSubtitle);
        Assert.Equal("Spoils of Victory", vm.BuyModeLabel);
        Assert.Equal("Take", vm.ItemBuyLabel);
        Assert.Equal("Nothing left to take", vm.EmptyBuyText);
    }

    [Fact]
    public async Task RefreshArcStateAsync_LoadsReplayedRemainingLoot_NotTemplateFallback()
    {
        var (vm, _, _) = CreateVictoryLootVm();

        // Before the refresh the VM falls back to the TEMPLATE Loot (fresh-kill shape)
        vm.RefreshCategories();
        Assert.Contains(vm.TradeInventory, i => i.Item.RefName == "bandage");

        // After the refresh the arc-REPLAYED remaining inventory wins: only the one
        // potion the victor hasn't taken yet — prior takes stay taken
        await vm.RefreshArcStateAsync();

        var remaining = vm.TradeInventory;
        var potion = Assert.Single(remaining);
        Assert.Equal("health_potion", potion.Item.RefName);
        Assert.Equal(1, potion.Quantity);
        Assert.Equal(0, potion.Price); // free take
    }

    [Fact]
    public async Task TakeItem_SendsZeroPriceBuyCommand_TheVictorExceptionShape()
    {
        var (vm, mediator, avatar) = CreateVictoryLootVm();
        await vm.RefreshArcStateAsync();
        var item = vm.TradeInventory.Single();

        await vm.BuyItemCommand.ExecuteAsync(item);

        var command = Assert.Single(mediator.SentTradeCommands);
        Assert.True(command.IsBuying);
        Assert.Equal(0, command.PricePerItem); // zero-price buy = the only accepted corpse trade
        Assert.Equal(DefeatedInstanceId, command.CharacterInstanceId);
        Assert.Equal(100, avatar.Stats!.Credits); // nothing charged
    }

    [Fact]
    public async Task TakeItem_ActivityMessageSaysVictoryLoot()
    {
        var (vm, _, _) = CreateVictoryLootVm();
        await vm.RefreshArcStateAsync();
        var item = vm.TradeInventory.Single();

        string? activityMessage = null;
        vm.ActivityMessageGenerated += (_, msg) => activityMessage = msg;

        await vm.BuyItemCommand.ExecuteAsync(item);

        Assert.NotNull(activityMessage);
        Assert.Contains("victory loot", activityMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SellItem_InVictoryLootMode_IsRefusedWithoutSendingACommand()
    {
        var (vm, mediator, _) = CreateVictoryLootVm();
        await vm.RefreshArcStateAsync();
        var item = vm.TradeInventory.Single();

        string? statusMessage = null;
        vm.StatusMessageChanged += (_, msg) => statusMessage = msg;

        await vm.SellItemCommand.ExecuteAsync(item);

        Assert.Empty(mediator.SentTradeCommands); // never reaches the engine
        Assert.NotNull(statusMessage);
    }
}
