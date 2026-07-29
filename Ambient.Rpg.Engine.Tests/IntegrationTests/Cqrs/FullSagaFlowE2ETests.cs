using Ambient.Application.Contracts;
using Ambient.Domain;
using Ambient.Domain.Contracts;
using Ambient.Domain.Partials;
using Ambient.Domain.Entities;
using Ambient.Rpg.Engine.Application.Behaviors;
using Ambient.Rpg.Engine.Application.Commands.Arcs;
using Ambient.Rpg.Engine.Application.ReadModels;
using Ambient.Rpg.Engine.Application.Services;
using Ambient.Rpg.Engine.Contracts;
using Ambient.Rpg.Engine.Contracts.Cqrs;
using Ambient.Rpg.Engine.Contracts.Persistence;
using Ambient.Rpg.Engine.Contracts.Services;
using Ambient.Rpg.Engine.Tests.Helpers;
using Ambient.Rpg.Engine.Domain.Arcs.TransactionLog;
using Ambient.Rpg.Engine.Infrastructure.Persistence;
using LiteDB;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace Ambient.Rpg.Engine.Tests.IntegrationTests.Cqrs;

/// <summary>
/// Comprehensive end-to-end tests for complete Arc flows validating all major fixes:
///
/// FIX VALIDATION:
/// - Zone exit detection (AvatarExited + CharacterDespawned)
/// - Feature distance calculation (latitude correction)
/// - Trade validation (credits, inventory, character alive)
/// - Loot system (inventory transfer)
/// - Character duplication prevention
/// - Achievement tracking
/// - Compensating transactions
///
/// SCENARIOS:
/// 1. Full RPG Loop: Enter zone ? spawn characters ? dialogue ? trade ? battle ? loot ? exit zone
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
    private readonly IWorld _world;
    private readonly LiteDatabase _database;
    private readonly IArcInstanceRepository _repository;

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
            cfg.AddOpenBehavior(typeof(LoggingBehavior<,>));
            cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));
            cfg.AddOpenBehavior(typeof(AchievementEvaluationBehavior<,>));
        });

        // Register dependencies
        services.AddSingleton(_world);
        services.AddSingleton<IArcInstanceRepository>(new ArcInstanceRepository(_database));
        services.AddSingleton<IAvatarProgressRepository>(new AvatarProgressRepository(_database));
        services.AddSingleton<IArcReadModelRepository, InMemoryArcReadModelRepository>();
        services.AddSingleton<IGameAvatarRepository>(new TestAvatarRepository()); // Mock repository for tests
        services.AddSingleton<Func<IGameAvatarRepository>>(sp => () => sp.GetRequiredService<IGameAvatarRepository>());
        services.AddSingleton<Func<IWorld>>(sp => () => sp.GetRequiredService<IWorld>());
        services.AddSingleton<IAvatarUpdateService, AvatarUpdateService>();
        services.AddSingleton<IWorldStateRepository, StubWorldStateRepository>();

        _serviceProvider = services.BuildServiceProvider();
        _mediator = _serviceProvider.GetRequiredService<IMediator>();
        _repository = _serviceProvider.GetRequiredService<IArcInstanceRepository>();
    }

  

    

    [Fact]
    public async Task TradeValidation_InsufficientCredits_Rejected()
    {
        // ARRANGE: Avatar with only 10 credits
        var avatarId = Guid.NewGuid();
        var avatar = CreateAvatar(avatarId);
        avatar.Stats!.Credits = 10;

        var arcRef = "MerchantCastleArc";

        // Spawn merchant
        await _mediator.Send(new UpdateAvatarPositionCommand
        {
            AvatarId = avatarId,
            ArcRef = arcRef,
            Latitude = 35.0,
            Longitude = 139.0,
            Avatar = avatar
        });

        var instance = await _repository.GetOrCreateInstanceAsync(avatarId, arcRef);
        var merchantInstanceId = Guid.Parse(
            instance.GetCommittedTransactions()
                .First(t => t.Type == ArcTransactionType.CharacterSpawned)
                .Data["CharacterInstanceId"]);

        // ACT: Try to buy expensive item (50 credits, but only have 10)
        var tradeResult = await _mediator.Send(new TradeItemCommand
        {
            AvatarId = avatarId,
            ArcRef = arcRef,
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

        var arcRef = "MerchantCastleArc";

        // Spawn merchant
        await _mediator.Send(new UpdateAvatarPositionCommand
        {
            AvatarId = avatarId,
            ArcRef = arcRef,
            Latitude = 35.0,
            Longitude = 139.0,
            Avatar = avatar
        });

        var instance = await _repository.GetOrCreateInstanceAsync(avatarId, arcRef);
        var merchantInstanceId = Guid.Parse(
            instance.GetCommittedTransactions()
                .First(t => t.Type == ArcTransactionType.CharacterSpawned)
                .Data["CharacterInstanceId"]);

        // Defeat merchant
        var defeatTx = new ArcTransaction
        {
            TransactionId = Guid.NewGuid(),
            Type = ArcTransactionType.CharacterDefeated,
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
        await _repository.AddTransactionsAsync(instance.InstanceId, new List<ArcTransaction> { defeatTx });
        await _repository.CommitTransactionsAsync(instance.InstanceId, new List<Guid> { defeatTx.TransactionId });

        // ACT: Try to trade with dead merchant
        var tradeResult = await _mediator.Send(new TradeItemCommand
        {
            AvatarId = avatarId,
            ArcRef = arcRef,
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
        // Create comprehensive test world with multiple arcs
        var merchantArc = new Arc
        {
            RefName = "MerchantCastleArc",
            DisplayName = "Merchant Castle",
            Latitude = 35.0,
            Longitude = 139.0
        };

        var guardArc = new Arc
        {
            RefName = "GuardPatrolArc",
            DisplayName = "Guard Patrol",
            Latitude = 35.0,
            Longitude = 139.0
        };

        var merchantTrigger = new ArcTrigger
        {
            RefName = "MerchantTrigger",
            EnterRadius = 100.0f,
            Spawn = new[]
            {
                new CharacterSpawn
                {
                    CharacterRef = "Merchant"
                }
            }
        };

        var guardTrigger = new ArcTrigger
        {
            RefName = "GuardTrigger",
            EnterRadius = 100.0f,
            Spawn = new[]
            {
                new CharacterSpawn { CharacterRef = "Guard" },
                new CharacterSpawn { CharacterRef = "Guard" },
                new CharacterSpawn { CharacterRef = "Guard" }
            }
        };

        var merchant = new Character
        {
            RefName = "Merchant",
            DisplayName = "Wandering Merchant",
            // Trade stock lives in Interactable.Loot (cloned into CurrentInventory per spawn)
            Interactable = new Interactable
            {
                Loot = new ItemCollection
                {
                    Equipment = new[]
                    {
                        new EquipmentEntry { EquipmentRef = "GoldPouch", Condition = 1.0f },
                        new EquipmentEntry { EquipmentRef = "IronSword", Condition = 1.0f }
                    }
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
            BaseValue = 50
        };

        var goldPouch = new Equipment
        {
            RefName = "GoldPouch",
            DisplayName = "Gold Pouch",
            BaseValue = 200
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
                    Saga = new[] { merchantArc, guardArc },
                    Characters = new[] { merchant, guard },
                    Equipment = new[] { ironSword, goldPouch },
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
        world.ArcLookup[merchantArc.RefName] = merchantArc;
        world.ArcLookup[guardArc.RefName] = guardArc;
        world.ArcTriggersLookup[merchantArc.RefName] = new List<ArcTrigger> { merchantTrigger };
        world.ArcTriggersLookup[guardArc.RefName] = new List<ArcTrigger> { guardTrigger };
        world.CharactersLookup[merchant.RefName] = merchant;
        world.CharactersLookup[guard.RefName] = guard;
        world.EquipmentLookup[ironSword.RefName] = ironSword;
        world.EquipmentLookup[goldPouch.RefName] = goldPouch;
        // LandmarksLookup removed - ArcFeatures now amalgamated with type field

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
