using Ambient.Domain;
using Ambient.Domain.Contracts;
using Ambient.Domain.DefinitionExtensions;
using Ambient.Domain.Entities;
using Ambient.Saga.Engine.Application.Behaviors;
using Ambient.Saga.Engine.Application.Commands.Saga;
using Ambient.Saga.Engine.Application.ReadModels;
using Ambient.Saga.Engine.Contracts.Cqrs;
using Ambient.Saga.Engine.Contracts.Services;
using Ambient.Saga.Engine.Domain.Achievements;
using Ambient.Saga.Engine.Domain.Rpg.Sagas.TransactionLog;
using Ambient.Saga.Engine.Infrastructure.Persistence;
using LiteDB;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace Ambient.Saga.Engine.Tests.IntegrationTests.Cqrs;

/// <summary>
/// Stub implementation of IAvatarUpdateService for testing.
/// Simply returns the avatar without any modifications.
/// </summary>
public class StubAvatarUpdateService : IAvatarUpdateService
{
    public Task<AvatarEntity> UpdateAvatarForTradeAsync(AvatarEntity avatar, SagaInstance sagaInstance, Guid tradeTransactionId, CancellationToken ct = default)
    {
        return Task.FromResult(avatar);
    }

    public Task<AvatarEntity> UpdateAvatarForBattleAsync(AvatarEntity avatar, SagaInstance sagaInstance, Guid battleStartedTransactionId, CancellationToken ct = default)
    {
        return Task.FromResult(avatar);
    }

    public Task<AvatarEntity> UpdateAvatarForLootAsync(AvatarEntity avatar, SagaInstance sagaInstance, Guid lootTransactionId, CancellationToken ct = default)
    {
        return Task.FromResult(avatar);
    }

    public Task<AvatarEntity> AddQuestTokenAsync(AvatarEntity avatar, string questTokenRef, CancellationToken ct = default)
    {
        return Task.FromResult(avatar);
    }

    public Task<AvatarEntity> UpdateAvatarForEffectsAsync(AvatarEntity avatar, SagaInstance sagaInstance, Guid effectTransactionId, CancellationToken ct = default)
    {
        return Task.FromResult(avatar);
    }

    public Task PersistAvatarAsync(AvatarEntity avatar, CancellationToken ct = default)
    {
        return Task.CompletedTask;
    }

    public Task<List<AchievementInstance>> GetAchievementInstancesAsync(Guid avatarId, CancellationToken ct = default)
    {
        return Task.FromResult(new List<AchievementInstance>());
    }

    public Task UpdateAchievementInstancesAsync(Guid avatarId, List<AchievementInstance> instances, CancellationToken ct = default)
    {
        return Task.CompletedTask;
    }
}

/// <summary>
/// Integration tests for TradeItemCommand via CQRS pipeline.
/// Tests economy transactions, inventory updates, and transaction logging.
/// </summary>
[Collection("Sequential CQRS Tests")]
public class TradeItemCommandTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly IMediator _mediator;
    private readonly IWorld _world;
    private readonly LiteDatabase _database;
    private readonly ISagaInstanceRepository _repository;

    public TradeItemCommandTests()
    {
        _database = new LiteDatabase(new MemoryStream());
        _world = CreateWorldWithMerchant();

        var services = new ServiceCollection();

        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssemblyContaining<TradeItemCommand>();
            cfg.AddOpenBehavior(typeof(SagaLoggingBehavior<,>));
            cfg.AddOpenBehavior(typeof(SagaValidationBehavior<,>));
            cfg.AddOpenBehavior(typeof(AchievementEvaluationBehavior<,>));
        });

        services.AddSingleton(_world);
        services.AddSingleton<ISagaInstanceRepository>(new SagaInstanceRepository(_database));
        services.AddSingleton<ISagaReadModelRepository, InMemorySagaReadModelRepository>();
        services.AddSingleton<IAvatarUpdateService, StubAvatarUpdateService>();

        _serviceProvider = services.BuildServiceProvider();
        _mediator = _serviceProvider.GetRequiredService<IMediator>();
        _repository = _serviceProvider.GetRequiredService<ISagaInstanceRepository>();
    }

    private World CreateWorldWithMerchant()
    {
        var merchant = new Character
        {
            RefName = "Merchant",
            DisplayName = "Village Merchant"
        };

        var sagaarc = new SagaArc
        {
            RefName = "VillageMerchant",
            DisplayName = "Village Merchant",
            LatitudeZ = 35.0,
            LongitudeX = 139.0
        };

        var world = new World
        {
            WorldTemplate = new WorldTemplate
            {
                Gameplay = new GameplayComponents
                {
                    SagaArcs = new[] { sagaarc },
                    Characters = new[] { merchant }
                }
            }
        };

        world.SagaArcLookup[sagaarc.RefName] = sagaarc;
        world.CharactersLookup[merchant.RefName] = merchant;
        world.SagaTriggersLookup[sagaarc.RefName] = new List<SagaTrigger>();

        return world;
    }

    private async Task<Guid> SpawnMerchant(Guid avatarId, string sagaRef)
    {
        var characterInstanceId = Guid.NewGuid();
        var instance = await _repository.GetOrCreateInstanceAsync(avatarId, sagaRef);

        var spawnTx = new SagaTransaction
        {
            TransactionId = Guid.NewGuid(),
            Type = SagaTransactionType.CharacterSpawned,
            AvatarId = avatarId.ToString(),
            LocalTimestamp = DateTime.UtcNow,
            Data = new Dictionary<string, string>
            {
                ["CharacterRef"] = "Merchant",
                ["CharacterInstanceId"] = characterInstanceId.ToString()
            }
        };

        await _repository.AddTransactionsAsync(instance.InstanceId, new List<SagaTransaction> { spawnTx });
        await _repository.CommitTransactionsAsync(instance.InstanceId, new List<Guid> { spawnTx.TransactionId });

        return characterInstanceId;
    }

    private AvatarEntity CreateTestAvatar(Guid avatarId)
    {
        return new AvatarEntity
        {
            Id = avatarId,
            AvatarId = avatarId,
            ArchetypeRef = "Warrior",
            DisplayName = "Test Avatar",
            Stats = new CharacterStats
            {
                Credits = 1000,
                Health = 100
            },
            Capabilities = new ItemCollection()
        };
    }

    [Fact]
    public async Task TradeItem_BuyFromMerchant_CreatesItemTradedTransaction()
    {
        // Arrange
        var avatarId = Guid.NewGuid();
        var avatar = CreateTestAvatar(avatarId);
        var characterInstanceId = await SpawnMerchant(avatarId, "VillageMerchant");

        var command = new TradeItemCommand
        {
            AvatarId = avatarId,
            Avatar = avatar,
            SagaArcRef = "VillageMerchant",
            CharacterInstanceId = characterInstanceId,
            ItemRef = "HealthPotion",
            Quantity = 3,
            IsBuying = true,
            PricePerItem = 50
        };

        // Act
        var result = await _mediator.Send(command);

        // Assert
        Assert.True(result.Successful, $"Command failed: {result.ErrorMessage}");
        Assert.NotEmpty(result.TransactionIds);

        // Verify ItemTraded transaction was created
        var instance = await _repository.GetOrCreateInstanceAsync(avatarId, "VillageMerchant");
        var tradeTx = instance.GetCommittedTransactions()
            .FirstOrDefault(t => t.Type == SagaTransactionType.ItemTraded);

        Assert.NotNull(tradeTx);
        Assert.Equal("HealthPotion", tradeTx.Data["ItemRef"]);
        Assert.Equal("3", tradeTx.Data["Quantity"]);
        Assert.Equal("True", tradeTx.Data["IsBuying"]);
        Assert.Equal("150", tradeTx.Data["TotalPrice"]); // 3 * 50
    }

    [Fact]
    public async Task TradeItem_SellToMerchant_CreatesItemTradedTransaction()
    {
        // Arrange
        var avatarId = Guid.NewGuid();
        var avatar = CreateTestAvatar(avatarId);

        // Give avatar some HealthPotions to sell
        avatar.Capabilities.Consumables = new[]
        {
            new ConsumableEntry { ConsumableRef = "HealthPotion", Quantity = 5 }
        };

        var characterInstanceId = await SpawnMerchant(avatarId, "VillageMerchant");

        var command = new TradeItemCommand
        {
            AvatarId = avatarId,
            Avatar = avatar,
            SagaArcRef = "VillageMerchant",
            CharacterInstanceId = characterInstanceId,
            ItemRef = "HealthPotion",
            Quantity = 2,
            IsBuying = false, // Selling
            PricePerItem = 40 // Sell price (usually lower than buy price)
        };

        // Act
        var result = await _mediator.Send(command);

        // Assert
        Assert.True(result.Successful, $"Command failed: {result.ErrorMessage}");

        var instance = await _repository.GetOrCreateInstanceAsync(avatarId, "VillageMerchant");
        var tradeTx = instance.GetCommittedTransactions()
            .FirstOrDefault(t => t.Type == SagaTransactionType.ItemTraded);

        Assert.NotNull(tradeTx);
        Assert.Equal("False", tradeTx.Data["IsBuying"]);
        Assert.Equal("80", tradeTx.Data["TotalPrice"]); // 2 * 40
    }

    [Fact]
    public async Task TradeItem_MultipleTrades_AllTracked()
    {
        // Arrange
        var avatarId = Guid.NewGuid();
        var avatar = CreateTestAvatar(avatarId);
        var characterInstanceId = await SpawnMerchant(avatarId, "VillageMerchant");

        // Act - Perform multiple trades
        await _mediator.Send(new TradeItemCommand
        {
            AvatarId = avatarId,
            Avatar = avatar,
            SagaArcRef = "VillageMerchant",
            CharacterInstanceId = characterInstanceId,
            ItemRef = "HealthPotion",
            Quantity = 1,
            IsBuying = true,
            PricePerItem = 50
        });

        await _mediator.Send(new TradeItemCommand
        {
            AvatarId = avatarId,
            Avatar = avatar,
            SagaArcRef = "VillageMerchant",
            CharacterInstanceId = characterInstanceId,
            ItemRef = "HealthPotion",
            Quantity = 2,
            IsBuying = true,
            PricePerItem = 50
        });

        // Assert
        var instance = await _repository.GetOrCreateInstanceAsync(avatarId, "VillageMerchant");
        var tradeTransactions = instance.GetCommittedTransactions()
            .Where(t => t.Type == SagaTransactionType.ItemTraded)
            .ToList();

        Assert.Equal(2, tradeTransactions.Count);

        // Verify sequence numbers are properly ordered
        Assert.True(tradeTransactions[0].SequenceNumber < tradeTransactions[1].SequenceNumber);
    }

    [Fact]
    public async Task TradeItem_NonExistentCharacter_ReturnsFailure()
    {
        // Arrange
        var avatarId = Guid.NewGuid();
        var avatar = CreateTestAvatar(avatarId);
        var fakeCharacterInstanceId = Guid.NewGuid();

        var command = new TradeItemCommand
        {
            AvatarId = avatarId,
            Avatar = avatar,
            SagaArcRef = "VillageMerchant",
            CharacterInstanceId = fakeCharacterInstanceId,
            ItemRef = "HealthPotion",
            Quantity = 1,
            IsBuying = true,
            PricePerItem = 50
        };

        // Act
        var result = await _mediator.Send(command);

        // Assert
        Assert.False(result.Successful);
        Assert.Contains("not found", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TradeItem_InvalidSagaRef_ReturnsFailure()
    {
        // Arrange
        var avatarId = Guid.NewGuid();
        var avatar = CreateTestAvatar(avatarId);
        var characterInstanceId = Guid.NewGuid();

        var command = new TradeItemCommand
        {
            AvatarId = avatarId,
            Avatar = avatar,
            SagaArcRef = "NonExistentSaga",
            CharacterInstanceId = characterInstanceId,
            ItemRef = "HealthPotion",
            Quantity = 1,
            IsBuying = true,
            PricePerItem = 50
        };

        // Act
        var result = await _mediator.Send(command);

        // Assert
        Assert.False(result.Successful);
        Assert.Contains("not found", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TradeItem_ZeroQuantity_ReturnsFailure()
    {
        // Arrange
        var avatarId = Guid.NewGuid();
        var avatar = CreateTestAvatar(avatarId);
        var characterInstanceId = await SpawnMerchant(avatarId, "VillageMerchant");

        var command = new TradeItemCommand
        {
            AvatarId = avatarId,
            Avatar = avatar,
            SagaArcRef = "VillageMerchant",
            CharacterInstanceId = characterInstanceId,
            ItemRef = "HealthPotion",
            Quantity = 0, // Invalid
            IsBuying = true,
            PricePerItem = 50
        };

        // Act
        var result = await _mediator.Send(command);

        // Assert
        Assert.False(result.Successful);
        Assert.Contains("quantity", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TradeItem_PipelineExecutes_TransactionsCommitted()
    {
        // Arrange
        var avatarId = Guid.NewGuid();
        var avatar = CreateTestAvatar(avatarId);
        var characterInstanceId = await SpawnMerchant(avatarId, "VillageMerchant");

        var command = new TradeItemCommand
        {
            AvatarId = avatarId,
            Avatar = avatar,
            SagaArcRef = "VillageMerchant",
            CharacterInstanceId = characterInstanceId,
            ItemRef = "HealthPotion",
            Quantity = 1,
            IsBuying = true,
            PricePerItem = 50
        };

        // Act
        var result = await _mediator.Send(command);

        // Assert
        Assert.True(result.Successful);

        // Verify all transactions are committed, not pending
        var instance = await _repository.GetOrCreateInstanceAsync(avatarId, "VillageMerchant");
        Assert.All(instance.Transactions, tx =>
            Assert.Equal(TransactionStatus.Committed, tx.Status));

        Assert.All(instance.Transactions, tx =>
            Assert.NotNull(tx.ServerTimestamp));
    }

    public void Dispose()
    {
        _database?.Dispose();
        _serviceProvider?.Dispose();
    }
}
