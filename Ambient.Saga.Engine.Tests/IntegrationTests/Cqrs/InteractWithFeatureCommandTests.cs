using Ambient.Domain;
using Ambient.Domain.DefinitionExtensions;
using Ambient.Domain.GameLogic.Gameplay.Avatar;
using Ambient.Saga.Engine.Application.Behaviors;
using Ambient.Saga.Engine.Application.Commands.Saga;
using Ambient.Saga.Engine.Application.ReadModels;
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
/// Integration tests for InteractWithFeatureCommand via CQRS pipeline.
/// Tests feature interactions, loot awarding, quest tokens, and MaxInteractions limits.
/// </summary>
[Collection("Sequential CQRS Tests")]
public class InteractWithFeatureCommandTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly ServiceProvider _serviceProvider;
    private readonly IMediator _mediator;
    private readonly World _world;
    private readonly LiteDatabase _database;
    private readonly ISagaInstanceRepository _repository;

    public InteractWithFeatureCommandTests(ITestOutputHelper output)
    {
        _output = output;
        _database = new LiteDatabase(new MemoryStream());
        _world = CreateWorldWithFeatures();

        var services = new ServiceCollection();

        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssemblyContaining<InteractWithFeatureCommand>();
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

    private World CreateWorldWithFeatures()
    {
        // Create a SagaFeature with loot (treasure chest)
        var treasureChest = new SagaFeature
        {
            RefName = "TreasureChest",
            DisplayName = "Ancient Treasure Chest",
            Type = SagaFeatureType.Landmark,
            Interactable = new Interactable
            {
                Loot = new ItemCollection
                {
                    Equipment = new[]
                    {
                        new EquipmentEntry { EquipmentRef = "IronSword", Condition = 1.0f }
                    },
                    Consumables = new[]
                    {
                        new ConsumableEntry { ConsumableRef = "HealthPotion", Quantity = 5 }
                    }
                }
            }
        };

        // Create a SagaFeature with MaxInteractions limit
        var limitedChest = new SagaFeature
        {
            RefName = "LimitedChest",
            DisplayName = "Limited Chest",
            Type = SagaFeatureType.Landmark,
            Interactable = new Interactable
            {
                MaxInteractions = 2,
                Loot = new ItemCollection
                {
                    Consumables = new[]
                    {
                        new ConsumableEntry { ConsumableRef = "HealthPotion", Quantity = 1 }
                    }
                }
            }
        };

        // Create a SagaFeature that gives quest tokens
        var questSignpost = new SagaFeature
        {
            RefName = "QuestSignpost",
            DisplayName = "Dragon Lair Marker",
            Type = SagaFeatureType.Quest,
            QuestRef = "DragonSlayerQuest",
            Interactable = new Interactable
            {
                GivesQuestTokenRef = new[] { "DragonSlayerToken" }
            }
        };

        // Create a SagaFeature that requires quest tokens
        var lockedDoor = new SagaFeature
        {
            RefName = "LockedDoor",
            DisplayName = "Sealed Door",
            Type = SagaFeatureType.Landmark,
            Interactable = new Interactable
            {
                RequiresQuestTokenRef = new[] { "GoldenKey" }
            }
        };

        // Create Saga arcs for each feature
        var lootChestSaga = new SagaArc
        {
            RefName = "LootChestSaga",
            DisplayName = "Treasure Hunt",
            LatitudeZ = 35.0,
            LongitudeX = 139.0,
            SagaFeatureRef = "TreasureChest"
        };

        var limitedChestSaga = new SagaArc
        {
            RefName = "LimitedChestSaga",
            DisplayName = "Limited Treasure",
            LatitudeZ = 36.0,
            LongitudeX = 140.0,
            SagaFeatureRef = "LimitedChest"
        };

        var questMarkerSaga = new SagaArc
        {
            RefName = "QuestMarkerSaga",
            DisplayName = "Quest Marker",
            LatitudeZ = 37.0,
            LongitudeX = 141.0,
            SagaFeatureRef = "QuestSignpost"
        };

        var lockedDoorSaga = new SagaArc
        {
            RefName = "LockedDoorSaga",
            DisplayName = "Locked Door",
            LatitudeZ = 38.0,
            LongitudeX = 142.0,
            SagaFeatureRef = "LockedDoor"
        };

        var sagas = new[] { lootChestSaga, limitedChestSaga, questMarkerSaga, lockedDoorSaga };

        var world = new World
        {
            WorldConfiguration = new WorldConfiguration
            {
                RefName = "TestWorld",
                SpawnLatitude = 35.0,
                SpawnLongitude = 139.0,
                ProceduralSettings = new ProceduralSettings
                {
                    LatitudeDegreesToUnits = 111320.0,
                    LongitudeDegreesToUnits = 91300.0
                }
            },
            WorldTemplate = new WorldTemplate
            {
                Gameplay = new GameplayComponents
                {
                    SagaArcs = sagas,
                    Characters = Array.Empty<Character>(),
                    CharacterArchetypes = Array.Empty<CharacterArchetype>(),
                    AvatarArchetypes = Array.Empty<AvatarArchetype>(),
                    Achievements = Array.Empty<Achievement>(),
                    CharacterAffinities = Array.Empty<CharacterAffinity>(),
                    DialogueTrees = Array.Empty<DialogueTree>()
                }
            }
        };

        // Initialize lookups
        world.SagaArcLookup = sagas.ToDictionary(s => s.RefName, s => s);
        world.SagaTriggersLookup = sagas.ToDictionary(
            s => s.RefName,
            s => new List<SagaTrigger>());

        world.SagaFeaturesLookup[treasureChest.RefName] = treasureChest;
        world.SagaFeaturesLookup[limitedChest.RefName] = limitedChest;
        world.SagaFeaturesLookup[questSignpost.RefName] = questSignpost;
        world.SagaFeaturesLookup[lockedDoor.RefName] = lockedDoor;

        return world;
    }

    private AvatarBase CreateAvatar(string name = "Test Hero")
    {
        var archetype = new AvatarArchetype
        {
            RefName = "TestWarrior",
            DisplayName = "Test Warrior",
            Description = "A test warrior",
            AffinityRef = "Physical",
            SpawnStats = new CharacterStats
            {
                Health = 1.0f,
                Stamina = 1.0f,
                Mana = 1.0f,
                Hunger = 0f,
                Thirst = 0f,
                Temperature = 37f,
                Insulation = 0f,
                Credits = 100,
                Experience = 0,
                Strength = 0.10f,
                Defense = 0.10f,
                Speed = 0.10f,
                Magic = 0.10f
            },
            SpawnCapabilities = new ItemCollection
            {
                Equipment = Array.Empty<EquipmentEntry>(),
                Consumables = Array.Empty<ConsumableEntry>(),
                Spells = Array.Empty<SpellEntry>(),
                Blocks = Array.Empty<BlockEntry>(),
                Tools = Array.Empty<ToolEntry>(),
                BuildingMaterials = Array.Empty<BuildingMaterialEntry>(),
                QuestTokens = Array.Empty<QuestTokenEntry>()
            },
            RespawnStats = new CharacterStats
            {
                Health = 1.0f,
                Stamina = 1.0f,
                Mana = 1.0f,
                Hunger = 0f,
                Thirst = 0f,
                Temperature = 37f,
                Insulation = 0f,
                Credits = 50,
                Experience = 0,
                Strength = 0.08f,
                Defense = 0.08f,
                Speed = 0.08f,
                Magic = 0.08f
            },
            RespawnCapabilities = new ItemCollection
            {
                Equipment = Array.Empty<EquipmentEntry>(),
                Consumables = Array.Empty<ConsumableEntry>(),
                Spells = Array.Empty<SpellEntry>(),
                Blocks = Array.Empty<BlockEntry>(),
                Tools = Array.Empty<ToolEntry>(),
                BuildingMaterials = Array.Empty<BuildingMaterialEntry>(),
                QuestTokens = Array.Empty<QuestTokenEntry>()
            }
        };

        var avatar = new AvatarBase
        {
            ArchetypeRef = "TestWarrior",
            DisplayName = name,
            BlockOwnership = new Dictionary<string, int>()
        };

        AvatarSpawner.SpawnFromModelAvatar(avatar, archetype);

        return avatar;
    }

    [Fact]
    public async Task InteractWithFeature_WithLoot_CreatesLootAwardedTransaction()
    {
        // Arrange
        var avatarId = Guid.NewGuid();
        var avatar = CreateAvatar();
        avatar.AvatarId = avatarId;

        _output.WriteLine("=== TEST: Feature Loot Transaction ===");

        var command = new InteractWithFeatureCommand
        {
            AvatarId = avatarId,
            SagaArcRef = "LootChestSaga",
            FeatureRef = "TreasureChest",
            Avatar = avatar
        };

        // Act
        var result = await _mediator.Send(command);

        // Assert
        Assert.True(result.Successful, $"Command failed: {result.ErrorMessage}");
        Assert.NotEmpty(result.TransactionIds);

        var instance = await _repository.GetOrCreateInstanceAsync(avatarId, "LootChestSaga");
        var transactions = instance.GetCommittedTransactions().ToList();

        // Should have EntityInteracted + LootAwarded
        Assert.Contains(transactions, t => t.Type == SagaTransactionType.EntityInteracted);
        var lootTx = transactions.FirstOrDefault(t => t.Type == SagaTransactionType.LootAwarded);
        Assert.NotNull(lootTx);

        // Verify loot items are serialized in transaction Data
        Assert.True(lootTx.Data.ContainsKey("Equipment"));
        Assert.True(lootTx.Data.ContainsKey("Consumables"));
        Assert.Contains("IronSword", lootTx.Data["Equipment"]);
        Assert.Contains("HealthPotion", lootTx.Data["Consumables"]);

        _output.WriteLine($"✓ Equipment serialized: {lootTx.Data["Equipment"]}");
        _output.WriteLine($"✓ Consumables serialized: {lootTx.Data["Consumables"]}");
        _output.WriteLine("✓ LootAwarded transaction created with serialized items");
    }

    [Fact]
    public async Task InteractWithFeature_MaxInteractions_BlocksAfterLimit()
    {
        // Arrange
        var avatarId = Guid.NewGuid();
        var avatar = CreateAvatar();
        avatar.AvatarId = avatarId;

        _output.WriteLine("=== TEST: MaxInteractions Enforcement ===");

        var command = new InteractWithFeatureCommand
        {
            AvatarId = avatarId,
            SagaArcRef = "LimitedChestSaga",
            FeatureRef = "LimitedChest",
            Avatar = avatar
        };

        // Act - Interact twice (should succeed)
        var result1 = await _mediator.Send(command);
        Assert.True(result1.Successful);
        _output.WriteLine("✓ Interaction 1/2 succeeded");

        var result2 = await _mediator.Send(command);
        Assert.True(result2.Successful);
        _output.WriteLine("✓ Interaction 2/2 succeeded");

        // Act - Interact third time (should fail)
        var result3 = await _mediator.Send(command);

        // Assert
        Assert.False(result3.Successful);
        Assert.Contains("maximum interactions", result3.ErrorMessage);
        _output.WriteLine($"✓ Interaction 3 blocked: {result3.ErrorMessage}");
    }

    [Fact]
    public async Task InteractWithFeature_QuestTokens_CreatesQuestTokenAwardedTransaction()
    {
        // Arrange
        var avatarId = Guid.NewGuid();
        var avatar = CreateAvatar();
        avatar.AvatarId = avatarId;

        _output.WriteLine("=== TEST: Quest Token Awarded ===");

        var command = new InteractWithFeatureCommand
        {
            AvatarId = avatarId,
            SagaArcRef = "QuestMarkerSaga",
            FeatureRef = "QuestSignpost",
            Avatar = avatar
        };

        // Act
        var result = await _mediator.Send(command);

        // Assert
        Assert.True(result.Successful, $"Command failed: {result.ErrorMessage}");

        var instance = await _repository.GetOrCreateInstanceAsync(avatarId, "QuestMarkerSaga");
        var transactions = instance.GetCommittedTransactions().ToList();

        var tokenTx = transactions.FirstOrDefault(t => t.Type == SagaTransactionType.QuestTokenAwarded);
        Assert.NotNull(tokenTx);
        Assert.Equal("DragonSlayerToken", tokenTx.Data["QuestTokenRef"]);
        _output.WriteLine($"✓ Quest token awarded: {tokenTx.Data["QuestTokenRef"]}");
    }

    [Fact]
    public async Task InteractWithFeature_RequiresQuestToken_BlocksWithoutToken()
    {
        // Arrange
        var avatarId = Guid.NewGuid();
        var avatar = CreateAvatar();
        avatar.AvatarId = avatarId;
        // Avatar has no quest tokens

        _output.WriteLine("=== TEST: Required Quest Token ===");

        var command = new InteractWithFeatureCommand
        {
            AvatarId = avatarId,
            SagaArcRef = "LockedDoorSaga",
            FeatureRef = "LockedDoor",
            Avatar = avatar
        };

        // Act
        var result = await _mediator.Send(command);

        // Assert
        Assert.False(result.Successful);
        Assert.Contains("quest tokens", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        _output.WriteLine($"✓ Interaction blocked: {result.ErrorMessage}");
    }

    [Fact]
    public async Task InteractWithFeature_NonExistentFeature_ReturnsFailure()
    {
        // Arrange
        var avatarId = Guid.NewGuid();
        var avatar = CreateAvatar();
        avatar.AvatarId = avatarId;

        var command = new InteractWithFeatureCommand
        {
            AvatarId = avatarId,
            SagaArcRef = "LootChestSaga",
            FeatureRef = "NonExistentFeature",
            Avatar = avatar
        };

        // Act
        var result = await _mediator.Send(command);

        // Assert
        Assert.False(result.Successful);
        Assert.Contains("not found", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InteractWithFeature_InvalidSagaRef_ReturnsFailure()
    {
        // Arrange
        var avatarId = Guid.NewGuid();
        var avatar = CreateAvatar();
        avatar.AvatarId = avatarId;

        var command = new InteractWithFeatureCommand
        {
            AvatarId = avatarId,
            SagaArcRef = "NonExistentSaga",
            FeatureRef = "TreasureChest",
            Avatar = avatar
        };

        // Act
        var result = await _mediator.Send(command);

        // Assert
        Assert.False(result.Successful);
        Assert.Contains("not found", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InteractWithFeature_TransactionsCommitted_ProperlyPersisted()
    {
        // Arrange
        var avatarId = Guid.NewGuid();
        var avatar = CreateAvatar();
        avatar.AvatarId = avatarId;

        var command = new InteractWithFeatureCommand
        {
            AvatarId = avatarId,
            SagaArcRef = "LootChestSaga",
            FeatureRef = "TreasureChest",
            Avatar = avatar
        };

        // Act
        var result = await _mediator.Send(command);

        // Assert
        Assert.True(result.Successful);

        // Verify transactions are committed, not pending
        var instance = await _repository.GetOrCreateInstanceAsync(avatarId, "LootChestSaga");
        var allTransactions = instance.Transactions;

        Assert.All(allTransactions, tx =>
            Assert.Equal(TransactionStatus.Committed, tx.Status));

        // Sequence numbers should be incremented
        Assert.True(result.NewSequenceNumber > 0);
    }

    public void Dispose()
    {
        _database?.Dispose();
        _serviceProvider?.Dispose();
    }
}
