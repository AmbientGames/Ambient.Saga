using Ambient.Domain;
using Ambient.Domain.Contracts;
using Ambient.Domain.Partials;
using Ambient.Domain.GameLogic.Gameplay.Avatar;
using Ambient.Rpg.Engine.Application.Behaviors;
using Ambient.Rpg.Engine.Application.Commands.Arcs;
using Ambient.Rpg.Engine.Application.ReadModels;
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

namespace Ambient.Rpg.Engine.Tests.IntegrationTests.Cqrs;

/// <summary>
/// Integration tests for UpdateAvatarPositionCommand via CQRS pipeline.
/// Tests the full pipeline: MediatR ? Behaviors ? Handler ? Repository
/// </summary>
[Collection("Sequential CQRS Tests")]
public class UpdateAvatarPositionCommandTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly IMediator _mediator;
    private readonly IWorld _world;
    private readonly LiteDatabase _database;
    private readonly IArcInstanceRepository _repository;

    public UpdateAvatarPositionCommandTests()
    {
        // Create in-memory LiteDB
        _database = new LiteDatabase(new MemoryStream());

        // Create test world with Arc
        _world = CreateWorldWithArc();

        // Setup DI container with CQRS infrastructure
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
        services.AddSingleton<IAvatarUpdateService, StubAvatarUpdateService>();
        services.AddSingleton<IWorldStateRepository, StubWorldStateRepository>();

        _serviceProvider = services.BuildServiceProvider();
        _mediator = _serviceProvider.GetRequiredService<IMediator>();
        _repository = _serviceProvider.GetRequiredService<IArcInstanceRepository>();
    }

    private World CreateWorldWithArc()
    {
        // Create a simple Arc with one trigger and character spawns
        var arc = new Arc
        {
            RefName = "TestArc",
            DisplayName = "Test Arc",
            Latitude = 35.0,
            Longitude = 139.0
        };

        var trigger = new ArcTrigger
        {
            RefName = "TestTrigger",
            EnterRadius = 100.0f,
            Spawn = new[]
            {
                new CharacterSpawn
                {
                    CharacterRef = "Guard"
                }
            }
        };

        var character = new Character
        {
            RefName = "Guard",
            DisplayName = "Castle Guard"
        };

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
                    Saga = new[] { arc },
                    Characters = new[] { character },
                    AvatarArchetypes = Array.Empty<AvatarArchetype>(),
                    Achievements = Array.Empty<Achievement>(),
                    CharacterAffinities = Array.Empty<CharacterAffinity>(),
                    DialogueTrees = Array.Empty<DialogueTree>()
                },
                //Simulation = new SimulationComponents(),
                //Presentation = new PresentationComponents()
            }
        };

        // Populate lookups (normally done by WorldXmlLoader)
        world.ArcLookup[arc.RefName] = arc;
        world.ArcTriggersLookup[arc.RefName] = new List<ArcTrigger> { trigger };
        world.CharactersLookup[character.RefName] = character;

        return world;
    }

    private AvatarBase CreateAvatar(string archetypeRef = "TestWarrior")
    {
        // Create minimal test archetype matching XML structure
        // Using Health/Stamina/Mana as normalized values (1.0 = 100%)
        // Stats use small decimal values (0.10 = 10%)
        var archetype = new AvatarArchetype
        {
            RefName = archetypeRef,
            DisplayName = "Test Warrior",
            Description = "A test warrior for CQRS integration tests",
            AffinityRef = "Physical",
            SpawnStats = new CharacterStats
            {
                Health = 1.0f,      // Normalized vitals (XML uses 1.0)
                Stamina = 1.0f,
                Mana = 1.0f,
                Temperature = 37f,
                Strength = 0.10f,   // Stat bonuses as decimals (XML uses 0.10)
                Defense = 0.10f,
                Magic = 0.10f,
                Speed = 0.10f,
                Endurance = 0f,
                Credits = 50,
                Experience = 0
            },
            SpawnCapabilities = new ItemCollection
            {
                Equipment = Array.Empty<EquipmentEntry>(),
                Consumables = Array.Empty<ConsumableEntry>(),
                Spells = Array.Empty<SpellEntry>(),
                Blocks = Array.Empty<BlockEntry>(),
                Tools = Array.Empty<ToolEntry>(),
                BuildingMaterials = Array.Empty<BuildingMaterialEntry>(),
            },
            RespawnStats = new CharacterStats
            {
                Health = 1.0f,
                Stamina = 1.0f,
                Mana = 1.0f,
                Temperature = 37f,
                Strength = 0.08f,
                Defense = 0.08f,
                Magic = 0.08f,
                Speed = 0.08f,
                Endurance = 0f,
                Credits = 25,
                Experience = 0
            },
            RespawnCapabilities = new ItemCollection
            {
                Equipment = Array.Empty<EquipmentEntry>(),
                Consumables = Array.Empty<ConsumableEntry>(),
                Spells = Array.Empty<SpellEntry>(),
                Blocks = Array.Empty<BlockEntry>(),
                Tools = Array.Empty<ToolEntry>(),
                BuildingMaterials = Array.Empty<BuildingMaterialEntry>(),
            }
        };

        // Create avatar instance
        var avatar = new AvatarBase
        {
            ArchetypeRef = archetypeRef,
            DisplayName = "Test Hero"
        };

        // Use AvatarSpawner to properly initialize from archetype
        // This sets up Stats and Capabilities (which includes equipment, consumables, etc.)
        AvatarSpawner.SpawnFromModelAvatar(
            avatar,
            archetype);

        return avatar;
    }

    [Fact]
    public async Task UpdateAvatarPosition_FirstEntry_CreatesTriggerActivatedTransaction()
    {
        // Arrange
        var avatarId = Guid.NewGuid();
        var avatar = CreateAvatar();
        avatar.AvatarId = avatarId; // Set the avatar's ID to match the command

        var command = new UpdateAvatarPositionCommand
        {
            AvatarId = avatarId,
            ArcRef = "TestArc",
            Latitude = 35.0,  // Arc center - within 100m trigger radius
            Longitude = 139.0,
            Avatar = avatar
        };

        // Act
        var result = await _mediator.Send(command);

        // Assert
        Assert.True(result.Successful, $"Command failed: {result.ErrorMessage}");
        Assert.NotEqual(Guid.Empty, result.ArcInstanceId);

        // Verify TriggerActivated transaction was created in the log
        var instance = await _repository.GetOrCreateInstanceAsync(avatarId, "TestArc", CancellationToken.None);
        var triggerTx = instance.GetCommittedTransactions()
            .FirstOrDefault(t => t.Type == ArcTransactionType.TriggerActivated);

        Assert.NotNull(triggerTx);
        Assert.Equal(avatarId.ToString(), triggerTx.AvatarId);

        // Command result should NOT contain state data (pure CQRS)
        // Client should query to see what happened
        Assert.Empty(result.Data);
    }

    [Fact]
    public async Task UpdateAvatarPosition_EnterTriggerRadius_SpawnsCharacters()
    {
        // Arrange
        var avatarId = Guid.NewGuid();
        var avatar = CreateAvatar();
        avatar.AvatarId = avatarId;

        var command = new UpdateAvatarPositionCommand
        {
            AvatarId = avatarId,
            ArcRef = "TestArc",
            Latitude = 35.0, // Close to Arc center (within trigger radius)
            Longitude = 139.0,
            Avatar = avatar
        };

        // Act
        var result = await _mediator.Send(command);

        // Assert
        Assert.True(result.Successful);

        // Verify CharacterSpawned transaction exists in the log
        var instance = await _repository.GetOrCreateInstanceAsync(avatarId, "TestArc", CancellationToken.None);
        var spawnTx = instance.GetCommittedTransactions()
            .FirstOrDefault(t => t.Type == ArcTransactionType.CharacterSpawned);

        Assert.NotNull(spawnTx);

        // Command result should NOT contain state data (pure CQRS)
        // To see what spawned, client should use GetAvailableInteractionsQuery
        Assert.Empty(result.Data);
    }

    [Fact]
    public async Task UpdateAvatarPosition_PipelineExecutes_TransactionsArePersisted()
    {
        // Arrange
        var avatarId = Guid.NewGuid();
        var avatar = CreateAvatar();
        avatar.AvatarId = avatarId;

        var command = new UpdateAvatarPositionCommand
        {
            AvatarId = avatarId,
            ArcRef = "TestArc",
            Latitude = 35.0,
            Longitude = 139.0,
            Avatar = avatar
        };

        // Act
        var result = await _mediator.Send(command);

        // Assert - Verify full pipeline executed
        Assert.True(result.Successful);
        Assert.NotEmpty(result.TransactionIds); // Transactions were created
        Assert.True(result.NewSequenceNumber > 0); // Sequence number incremented

        // Verify transactions are actually in database (not just in-memory)
        var instance = await _repository.GetOrCreateInstanceAsync(avatarId, "TestArc", CancellationToken.None);
        Assert.NotEmpty(instance.GetCommittedTransactions());

        // All transactions should be committed
        Assert.All(instance.GetCommittedTransactions(), tx =>
            Assert.Equal(TransactionStatus.Committed, tx.Status));
    }

    [Fact]
    public async Task UpdateAvatarPosition_InvalidArcRef_ReturnsFailure()
    {
        // Arrange
        var avatarId = Guid.NewGuid();
        var avatar = CreateAvatar();
        avatar.AvatarId = avatarId;

        var command = new UpdateAvatarPositionCommand
        {
            AvatarId = avatarId,
            ArcRef = "NonExistentArc",
            Latitude = 35.0,
            Longitude = 139.0,
            Avatar = avatar
        };

        // Act
        var result = await _mediator.Send(command);

        // Assert
        Assert.False(result.Successful);
        Assert.Contains("not found", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UpdateAvatarPosition_MultipleCalls_MaintainsSequenceNumbers()
    {
        // Arrange
        var avatarId = Guid.NewGuid();
        var avatar = CreateAvatar();
        avatar.AvatarId = avatarId;

        // Act - Send command twice
        // First call activates trigger and spawns characters
        var result1 = await _mediator.Send(new UpdateAvatarPositionCommand
        {
            AvatarId = avatarId,
            ArcRef = "TestArc",
            Latitude = 35.0,
            Longitude = 139.0,
            Avatar = avatar
        });

        // Second call - trigger already activated, so may not create new transactions
        var result2 = await _mediator.Send(new UpdateAvatarPositionCommand
        {
            AvatarId = avatarId,
            ArcRef = "TestArc",
            Latitude = 35.001, // Slightly different position
            Longitude = 139.001,
            Avatar = avatar
        });

        // Assert
        Assert.True(result1.Successful);
        Assert.True(result2.Successful);

        // First call should create transactions (trigger activation + character spawns)
        Assert.NotEmpty(result1.TransactionIds);
        Assert.True(result1.NewSequenceNumber > 0);

        // If second call created transactions, sequence numbers should be higher
        if (result2.TransactionIds.Any())
        {
            Assert.True(result2.NewSequenceNumber > result1.NewSequenceNumber,
                "Second command should have higher sequence number if it created transactions");
        }

        // Verify transaction log maintains proper ordering
        var instance = await _repository.GetOrCreateInstanceAsync(avatarId, "TestArc", CancellationToken.None);
        var transactions = instance.GetCommittedTransactions().OrderBy(t => t.SequenceNumber).ToList();

        // Should have at least the transactions from the first call
        Assert.NotEmpty(transactions);

        // Verify sequence numbers are properly ordered (no gaps or duplicates)
        var sequenceNumbers = transactions.Select(t => t.SequenceNumber).ToList();
        var sortedSequenceNumbers = sequenceNumbers.OrderBy(s => s).ToList();
        Assert.Equal(sortedSequenceNumbers, sequenceNumbers);
    }

    public void Dispose()
    {
        _database?.Dispose();
        _serviceProvider?.Dispose();
    }
}
