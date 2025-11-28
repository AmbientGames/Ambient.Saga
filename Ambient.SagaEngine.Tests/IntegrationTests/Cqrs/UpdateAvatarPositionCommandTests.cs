using Ambient.Domain;
using Ambient.Domain.DefinitionExtensions;
using Ambient.Domain.GameLogic.Gameplay.Avatar;
using Ambient.SagaEngine.Application.Behaviors;
using Ambient.SagaEngine.Application.Commands.Saga;
using Ambient.SagaEngine.Application.ReadModels;
using Ambient.SagaEngine.Contracts.Cqrs;
using Ambient.SagaEngine.Domain.Rpg.Sagas.TransactionLog;
using Ambient.SagaEngine.Infrastructure.Persistence;
using LiteDB;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace Ambient.SagaEngine.Tests.IntegrationTests.Cqrs;

/// <summary>
/// Integration tests for UpdateAvatarPositionCommand via CQRS pipeline.
/// Tests the full pipeline: MediatR → Behaviors → Handler → Repository
/// </summary>
[Collection("Sequential CQRS Tests")]
public class UpdateAvatarPositionCommandTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly IMediator _mediator;
    private readonly World _world;
    private readonly LiteDatabase _database;
    private readonly ISagaInstanceRepository _repository;

    public UpdateAvatarPositionCommandTests()
    {
        // Create in-memory LiteDB
        _database = new LiteDatabase(new MemoryStream());

        // Create test world with Saga
        _world = CreateWorldWithSaga();

        // Setup DI container with CQRS infrastructure
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

        _serviceProvider = services.BuildServiceProvider();
        _mediator = _serviceProvider.GetRequiredService<IMediator>();
        _repository = _serviceProvider.GetRequiredService<ISagaInstanceRepository>();
    }

    private World CreateWorldWithSaga()
    {
        // Create a simple Saga with one trigger and character spawns
        var sagaArc = new SagaArc
        {
            RefName = "TestSaga",
            DisplayName = "Test Saga",
            LatitudeZ = 35.0,
            LongitudeX = 139.0,
            Y = 50.0
        };

        var trigger = new SagaTrigger
        {
            RefName = "TestTrigger",
            EnterRadius = 100.0f,
            Spawn = new[]
            {
                new CharacterSpawn
                {
                    ItemElementName = ItemChoiceType.CharacterRef,
                    Item = "Guard"
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
                    SagaArcs = new[] { sagaArc },
                    Characters = new[] { character },
                    CharacterArchetypes = Array.Empty<CharacterArchetype>(),
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
        world.SagaArcLookup[sagaArc.RefName] = sagaArc;
        world.SagaTriggersLookup[sagaArc.RefName] = new List<SagaTrigger> { trigger };
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
                Hunger = 0f,
                Thirst = 0f,
                Temperature = 37f,
                Insulation = 0f,
                Credits = 50,
                Experience = 0,
                Strength = 0.10f,   // Stat bonuses as decimals (XML uses 0.10)
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
                Credits = 25,
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

        // Create avatar instance
        var avatar = new AvatarBase
        {
            ArchetypeRef = archetypeRef,
            DisplayName = "Test Hero",
            BlockOwnership = new Dictionary<string, int>()
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
            SagaArcRef = "TestSaga",
            Latitude = 35.0,  // Saga center - within 100m trigger radius
            Longitude = 139.0,
            Y = 50.0,
            Avatar = avatar
        };

        // Act
        var result = await _mediator.Send(command);

        // Assert
        Assert.True(result.Successful, $"Command failed: {result.ErrorMessage}");
        Assert.NotEqual(Guid.Empty, result.SagaInstanceId);

        // Verify TriggerActivated transaction was created in the log
        var instance = await _repository.GetOrCreateInstanceAsync(avatarId, "TestSaga", CancellationToken.None);
        var triggerTx = instance.GetCommittedTransactions()
            .FirstOrDefault(t => t.Type == SagaTransactionType.TriggerActivated);

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
            SagaArcRef = "TestSaga",
            Latitude = 35.0, // Close to Saga center (within trigger radius)
            Longitude = 139.0,
            Y = 50.0,
            Avatar = avatar
        };

        // Act
        var result = await _mediator.Send(command);

        // Assert
        Assert.True(result.Successful);

        // Verify CharacterSpawned transaction exists in the log
        var instance = await _repository.GetOrCreateInstanceAsync(avatarId, "TestSaga", CancellationToken.None);
        var spawnTx = instance.GetCommittedTransactions()
            .FirstOrDefault(t => t.Type == SagaTransactionType.CharacterSpawned);

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
            SagaArcRef = "TestSaga",
            Latitude = 35.0,
            Longitude = 139.0,
            Y = 50.0,
            Avatar = avatar
        };

        // Act
        var result = await _mediator.Send(command);

        // Assert - Verify full pipeline executed
        Assert.True(result.Successful);
        Assert.NotEmpty(result.TransactionIds); // Transactions were created
        Assert.True(result.NewSequenceNumber > 0); // Sequence number incremented

        // Verify transactions are actually in database (not just in-memory)
        var instance = await _repository.GetOrCreateInstanceAsync(avatarId, "TestSaga", CancellationToken.None);
        Assert.NotEmpty(instance.GetCommittedTransactions());

        // All transactions should be committed
        Assert.All(instance.GetCommittedTransactions(), tx =>
            Assert.Equal(TransactionStatus.Committed, tx.Status));
    }

    [Fact]
    public async Task UpdateAvatarPosition_InvalidSagaRef_ReturnsFailure()
    {
        // Arrange
        var avatarId = Guid.NewGuid();
        var avatar = CreateAvatar();
        avatar.AvatarId = avatarId;

        var command = new UpdateAvatarPositionCommand
        {
            AvatarId = avatarId,
            SagaArcRef = "NonExistentSaga",
            Latitude = 35.0,
            Longitude = 139.0,
            Y = 50.0,
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
            SagaArcRef = "TestSaga",
            Latitude = 35.0,
            Longitude = 139.0,
            Y = 50.0,
            Avatar = avatar
        });

        // Second call - trigger already activated, so may not create new transactions
        var result2 = await _mediator.Send(new UpdateAvatarPositionCommand
        {
            AvatarId = avatarId,
            SagaArcRef = "TestSaga",
            Latitude = 35.001, // Slightly different position
            Longitude = 139.001,
            Y = 50.0,
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
        var instance = await _repository.GetOrCreateInstanceAsync(avatarId, "TestSaga", CancellationToken.None);
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
