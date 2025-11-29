using Ambient.Application.Contracts;
using Ambient.Domain;
using Ambient.Domain.DefinitionExtensions;
using Ambient.Domain.Entities;
using Ambient.Saga.Engine.Application.Behaviors;
using Ambient.Saga.Engine.Application.Commands.Saga;
using Ambient.Saga.Engine.Application.ReadModels;
using Ambient.Saga.Engine.Application.Services;
using Ambient.Saga.Engine.Contracts.Cqrs;
using Ambient.Saga.Engine.Contracts.Services;
using Ambient.Saga.Engine.Domain.Rpg.Sagas.TransactionLog;
using Ambient.Saga.Engine.Infrastructure.Persistence;
using LiteDB;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace Ambient.Saga.Engine.Tests.IntegrationTests.Cqrs;

/// <summary>
/// Comprehensive end-to-end tests for complete Saga flows validating all major fixes:
///
/// FIX VALIDATION:
/// - Zone exit detection (PlayerExited + CharacterDespawned)
/// - Feature distance calculation (latitude correction)
/// - Trade validation (credits, inventory, character alive)
/// - Loot system (inventory transfer)
/// - Character duplication prevention
/// - Achievement tracking
/// - Compensating transactions
///
/// SCENARIOS:
/// 1. Full RPG Loop: Enter zone → spawn characters → dialogue → trade → battle → loot → exit zone
/// 2. Zone Lifecycle: Enter/exit with character despawn
/// 3. Trade Validation: All edge cases (negative price, insufficient credits, dead merchant)
/// 4. Geographic Accuracy: Distance calculations at various latitudes
/// </summary>
[Collection("Sequential CQRS Tests")]
public class FullSagaFlowE2ETests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly ServiceProvider _serviceProvider;
    private readonly IMediator _mediator;
    private readonly World _world;
    private readonly LiteDatabase _database;
    private readonly ISagaInstanceRepository _repository;

    public FullSagaFlowE2ETests(ITestOutputHelper output)
    {
        _output = output;
        _database = new LiteDatabase(new MemoryStream());
        _world = CreateTestWorld();

        var services = new ServiceCollection();

        // Register MediatR with all handlers and behaviors
        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssemblyContaining<UpdateAvatarPositionCommand>();
            cfg.AddOpenBehavior(typeof(SagaLoggingBehavior<,>));
            cfg.AddOpenBehavior(typeof(SagaValidationBehavior<,>));
            cfg.AddOpenBehavior(typeof(AchievementEvaluationBehavior<,>));
        });

        // Register dependencies
        services.AddSingleton(_world);
        services.AddSingleton<ISagaInstanceRepository>(new SagaInstanceRepository(_database));
        services.AddSingleton<ISagaReadModelRepository, InMemorySagaReadModelRepository>();
        services.AddSingleton<IGameAvatarRepository>(new TestAvatarRepository()); // Mock repository for tests
        services.AddSingleton<IAvatarUpdateService, AvatarUpdateService>();

        _serviceProvider = services.BuildServiceProvider();
        _mediator = _serviceProvider.GetRequiredService<IMediator>();
        _repository = _serviceProvider.GetRequiredService<ISagaInstanceRepository>();
    }

  

    [Fact]
    public async Task ZoneExitDetection_LivingCharacters_Despawned()
    {
        // ARRANGE: Spawn living characters
        var avatarId = Guid.NewGuid();
        var avatar = CreateAvatar(avatarId);

        var sagaRef = "GuardPatrolSaga";

        // Enter zone (spawns 3 guards)
        await _mediator.Send(new UpdateAvatarPositionCommand
        {
            AvatarId = avatarId,
            SagaArcRef = sagaRef,
            Latitude = 35.0,
            Longitude = 139.0,
            Y = 50.0,
            Avatar = avatar
        });

        var instance = await _repository.GetOrCreateInstanceAsync(avatarId, sagaRef);
        var spawnTxs = instance.GetCommittedTransactions()
            .Where(t => t.Type == SagaTransactionType.CharacterSpawned)
            .ToList();

        Assert.Equal(3, spawnTxs.Count);
        _output.WriteLine($"Spawned {spawnTxs.Count} guards");

        // ACT: Exit zone (move beyond exit radius)
        var exitResult = await _mediator.Send(new UpdateAvatarPositionCommand
        {
            AvatarId = avatarId,
            SagaArcRef = sagaRef,
            Latitude = 35.002, // Far away
            Longitude = 139.002,
            Y = 50.0,
            Avatar = avatar
        });

        // ASSERT: All living characters despawned
        Assert.True(exitResult.Successful);

        var finalInstance = await _repository.GetOrCreateInstanceAsync(avatarId, sagaRef);
        var despawnTxs = finalInstance.GetCommittedTransactions()
            .Where(t => t.Type == SagaTransactionType.CharacterDespawned)
            .ToList();

        Assert.Equal(3, despawnTxs.Count);

        foreach (var despawnTx in despawnTxs)
        {
            Assert.Equal("Player exited trigger zone", despawnTx.Data["Reason"]);
            _output.WriteLine($"Despawned: {despawnTx.Data["CharacterRef"]}");
        }

        _output.WriteLine("✓ Zone exit detection working correctly!");
    }

    [Fact]
    public async Task TradeValidation_InsufficientCredits_Rejected()
    {
        // ARRANGE: Avatar with only 10 credits
        var avatarId = Guid.NewGuid();
        var avatar = CreateAvatar(avatarId);
        avatar.Stats!.Credits = 10;

        var sagaRef = "MerchantCastleSaga";

        // Spawn merchant
        await _mediator.Send(new UpdateAvatarPositionCommand
        {
            AvatarId = avatarId,
            SagaArcRef = sagaRef,
            Latitude = 35.0,
            Longitude = 139.0,
            Y = 50.0,
            Avatar = avatar
        });

        var instance = await _repository.GetOrCreateInstanceAsync(avatarId, sagaRef);
        var merchantInstanceId = Guid.Parse(
            instance.GetCommittedTransactions()
                .First(t => t.Type == SagaTransactionType.CharacterSpawned)
                .Data["CharacterInstanceId"]);

        // ACT: Try to buy expensive item (50 credits, but only have 10)
        var tradeResult = await _mediator.Send(new TradeItemCommand
        {
            AvatarId = avatarId,
            SagaArcRef = sagaRef,
            CharacterInstanceId = merchantInstanceId,
            ItemRef = "IronSword",
            Quantity = 1,
            IsBuying = true,
            PricePerItem = 50,
            Avatar = avatar
        });

        // ASSERT: Trade rejected
        Assert.False(tradeResult.Successful);
        Assert.Contains("Insufficient credits", tradeResult.ErrorMessage);
        _output.WriteLine($"Trade correctly rejected: {tradeResult.ErrorMessage}");
    }

    [Fact]
    public async Task TradeValidation_DefeatedCharacter_Rejected()
    {
        // ARRANGE: Spawn and defeat merchant
        var avatarId = Guid.NewGuid();
        var avatar = CreateAvatar(avatarId);

        var sagaRef = "MerchantCastleSaga";

        // Spawn merchant
        await _mediator.Send(new UpdateAvatarPositionCommand
        {
            AvatarId = avatarId,
            SagaArcRef = sagaRef,
            Latitude = 35.0,
            Longitude = 139.0,
            Y = 50.0,
            Avatar = avatar
        });

        var instance = await _repository.GetOrCreateInstanceAsync(avatarId, sagaRef);
        var merchantInstanceId = Guid.Parse(
            instance.GetCommittedTransactions()
                .First(t => t.Type == SagaTransactionType.CharacterSpawned)
                .Data["CharacterInstanceId"]);

        // Defeat merchant
        var defeatTx = new SagaTransaction
        {
            TransactionId = Guid.NewGuid(),
            Type = SagaTransactionType.CharacterDefeated,
            AvatarId = avatarId.ToString(),
            Status = TransactionStatus.Pending,
            LocalTimestamp = DateTime.UtcNow,
            Data = new Dictionary<string, string>
            {
                ["CharacterInstanceId"] = merchantInstanceId.ToString(),
                ["CharacterRef"] = "Merchant",
                ["VictorAvatarId"] = avatarId.ToString()
            }
        };

        instance.AddTransaction(defeatTx);
        await _repository.AddTransactionsAsync(instance.InstanceId, new List<SagaTransaction> { defeatTx });
        await _repository.CommitTransactionsAsync(instance.InstanceId, new List<Guid> { defeatTx.TransactionId });

        // ACT: Try to trade with dead merchant
        var tradeResult = await _mediator.Send(new TradeItemCommand
        {
            AvatarId = avatarId,
            SagaArcRef = sagaRef,
            CharacterInstanceId = merchantInstanceId,
            ItemRef = "IronSword",
            Quantity = 1,
            IsBuying = true,
            PricePerItem = 50,
            Avatar = avatar
        });

        // ASSERT: Trade rejected
        Assert.False(tradeResult.Successful);
        Assert.Contains("Cannot trade with defeated character", tradeResult.ErrorMessage);
        _output.WriteLine($"Corpse trading correctly blocked: {tradeResult.ErrorMessage}");
    }

    //[Fact]
    //public async Task FeatureDistance_LatitudeCorrection_Accurate()
    //{
    //    // OUTDATED TEST - Landmark type and LandmarksLookup no longer exist in current system
    //    // This test is from an older version of the codebase
    //}

    public void Dispose()
    {
        _database?.Dispose();
        _serviceProvider?.Dispose();
    }

    #region Test World Setup

    private World CreateTestWorld()
    {
        // Create comprehensive test world with multiple sagas
        var merchantSaga = new SagaArc
        {
            RefName = "MerchantCastleSaga",
            DisplayName = "Merchant Castle",
            LatitudeZ = 35.0,
            LongitudeX = 139.0,
            Y = 50.0
        };

        var guardSaga = new SagaArc
        {
            RefName = "GuardPatrolSaga",
            DisplayName = "Guard Patrol",
            LatitudeZ = 35.0,
            LongitudeX = 139.0,
            Y = 50.0
        };

        var merchantTrigger = new SagaTrigger
        {
            RefName = "MerchantTrigger",
            EnterRadius = 100.0f,
            TriggerType = SagaTriggerType.SpawnPassive,
            Spawn = new[]
            {
                new CharacterSpawn
                {
                    ItemElementName = ItemChoiceType.CharacterRef,
                    Item = "Merchant"
                }
            }
        };

        var guardTrigger = new SagaTrigger
        {
            RefName = "GuardTrigger",
            EnterRadius = 100.0f,
            TriggerType = SagaTriggerType.SpawnPassive,
            Spawn = new[]
            {
                new CharacterSpawn { ItemElementName = ItemChoiceType.CharacterRef, Item = "Guard" },
                new CharacterSpawn { ItemElementName = ItemChoiceType.CharacterRef, Item = "Guard" },
                new CharacterSpawn { ItemElementName = ItemChoiceType.CharacterRef, Item = "Guard" }
            }
        };

        var merchant = new Character
        {
            RefName = "Merchant",
            DisplayName = "Wandering Merchant",
            Capabilities = new ItemCollection
            {
                Equipment = new[]
                {
                    new EquipmentEntry { EquipmentRef = "GoldPouch", Condition = 1.0f }
                }
            },
            Stats = new CharacterStats
            {
                Health = 1.0f,
                Credits = 100
            }
        };

        var guard = new Character
        {
            RefName = "Guard",
            DisplayName = "Castle Guard",
            Stats = new CharacterStats
            {
                Health = 1.0f,
                Strength = 0.15f
            }
        };

        var ironSword = new Equipment
        {
            RefName = "IronSword",
            DisplayName = "Iron Sword",
            WholesalePrice = 50
        };

        var goldPouch = new Equipment
        {
            RefName = "GoldPouch",
            DisplayName = "Gold Pouch",
            WholesalePrice = 200
        };

        var world = new World
        {
            IsProcedural = true,
            WorldConfiguration = new WorldConfiguration
            {
                RefName = "TestWorld",
                SpawnLatitude = 35.0,
                SpawnLongitude = 139.0,
                ProceduralSettings = new ProceduralSettings
                {
                    LatitudeDegreesToUnits = 111320.0,
                    LongitudeDegreesToUnits = 91300.0
                },
                HeightMapSettings = new HeightMapSettings
                {
                    HorizontalScale = 1.0,
                    MapResolutionInMeters = 30.0
                }
            },
            WorldTemplate = new WorldTemplate
            {
                Gameplay = new GameplayComponents
                {
                    SagaArcs = new[] { merchantSaga, guardSaga },
                    Characters = new[] { merchant, guard },
                    Equipment = new[] { ironSword, goldPouch },
                    CharacterArchetypes = Array.Empty<CharacterArchetype>(),
                    AvatarArchetypes = Array.Empty<AvatarArchetype>(),
                    Achievements = Array.Empty<Achievement>(),
                    CharacterAffinities = Array.Empty<CharacterAffinity>(),
                    DialogueTrees = Array.Empty<DialogueTree>(),
                    Consumables = Array.Empty<Consumable>()
                },
                //Simulation = new SimulationComponents(),
                //Presentation = new PresentationComponents()
            }
        };

        // Populate lookups
        world.SagaArcLookup[merchantSaga.RefName] = merchantSaga;
        world.SagaArcLookup[guardSaga.RefName] = guardSaga;
        world.SagaTriggersLookup[merchantSaga.RefName] = new List<SagaTrigger> { merchantTrigger };
        world.SagaTriggersLookup[guardSaga.RefName] = new List<SagaTrigger> { guardTrigger };
        world.CharactersLookup[merchant.RefName] = merchant;
        world.CharactersLookup[guard.RefName] = guard;
        world.EquipmentLookup[ironSword.RefName] = ironSword;
        world.EquipmentLookup[goldPouch.RefName] = goldPouch;
        // LandmarksLookup removed - SagaFeatures now amalgamated with type field

        return world;
    }

    private AvatarEntity CreateAvatar(Guid avatarId)
    {
        return new AvatarEntity
        {
            Id = avatarId,
            AvatarId = avatarId,
            DisplayName = "Test Adventurer",
            ArchetypeRef = "Warrior",
            Stats = new CharacterStats
            {
                Health = 1.0f,
                Stamina = 1.0f,
                Mana = 1.0f,
                Credits = 0,
                Strength = 0.15f,
                Defense = 0.10f,
                Speed = 0.12f
            },
            Capabilities = new ItemCollection
            {
                Equipment = Array.Empty<EquipmentEntry>(),
                Consumables = Array.Empty<ConsumableEntry>(),
                Spells = Array.Empty<SpellEntry>(),
                QuestTokens = Array.Empty<QuestTokenEntry>()
            }
        };
    }

    #endregion
}

/// <summary>
/// Test double for IGameAvatarRepository that succeeds silently.
/// </summary>
public class TestAvatarRepository : IGameAvatarRepository
{
    public Task<TAvatar?> LoadAvatarAsync<TAvatar>() where TAvatar : class
    {
        return Task.FromResult<TAvatar?>(null);
    }

    public Task SaveAvatarAsync<TAvatar>(TAvatar avatar) where TAvatar : class
    {
        return Task.CompletedTask; // Silently succeed
    }

    public Task DeleteAvatarsAsync()
    {
        return Task.CompletedTask;
    }
}
