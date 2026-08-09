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
/// Integration tests for StartDialogueCommand via CQRS pipeline.
/// Tests dialogue initiation, transaction logging, and achievement tracking.
/// </summary>
[Collection("Sequential CQRS Tests")]
public class StartDialogueCommandTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly IMediator _mediator;
    private readonly IWorld _world;
    private readonly LiteDatabase _database;
    private readonly IArcInstanceRepository _repository;

    public StartDialogueCommandTests()
    {
        _database = new LiteDatabase(new MemoryStream());
        _world = CreateWorldWithMerchant();

        var services = new ServiceCollection();

        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssemblyContaining<StartDialogueCommand>();
            cfg.AddOpenBehavior(typeof(LoggingBehavior<,>));
            cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));
            cfg.AddOpenBehavior(typeof(AchievementEvaluationBehavior<,>));
        });

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

    private World CreateWorldWithMerchant()
    {
        var merchant = new Character
        {
            RefName = "Merchant",
            DisplayName = "Village Merchant",
            Interactable = new Interactable
            {
                DialogueTreeRef = "MerchantDialogue"
            }
        };

        var arc = new Arc
        {
            RefName = "VillageMerchant",
            DisplayName = "Village Merchant",
            Latitude = 35.0,
            Longitude = 139.0
        };

        var dialogueTree = new DialogueTree
        {
            RefName = "MerchantDialogue",
            StartNodeId = "greeting",
            Node = new[]
            {
                new DialogueNode
                {
                    NodeId = "greeting",
                    Text = new[] { "Welcome to my shop!" },
                    Choice = Array.Empty<DialogueChoice>()
                }
            }
        };

        var world = new World
        {
            WorldTemplate = new WorldTemplate
            {
                Gameplay = new GameplayComponents
                {
                    Saga = new[] { arc },
                    Characters = new[] { merchant },
                    DialogueTrees = new[] { dialogueTree }
                }
            }
        };

        world.ArcLookup[arc.RefName] = arc;
        world.CharactersLookup[merchant.RefName] = merchant;
        world.ArcTriggersLookup[arc.RefName] = new List<ArcTrigger>();
        world.DialogueTreesLookup[dialogueTree.RefName] = dialogueTree;

        return world;
    }

    private async Task<Guid> SpawnMerchant(Guid avatarId, string arcRef)
    {
        var characterInstanceId = Guid.NewGuid();
        var instance = await _repository.GetOrCreateInstanceAsync(avatarId, arcRef);

        var spawnTx = new ArcTransaction
        {
            TransactionId = Guid.NewGuid(),
            Type = ArcTransactionType.CharacterSpawned,
            AvatarId = avatarId.ToString(),
            LocalTimestamp = DateTime.UtcNow,
            Data = new Dictionary<string, string>
            {
                ["CharacterRef"] = "Merchant",
                ["CharacterInstanceId"] = characterInstanceId.ToString()
            }
        };

        await _repository.AddTransactionsAsync(instance.InstanceId, new List<ArcTransaction> { spawnTx });
        await _repository.CommitTransactionsAsync(instance.InstanceId, new List<Guid> { spawnTx.TransactionId });

        return characterInstanceId;
    }

    private AvatarBase CreateAvatar(string name = "Test Hero")
    {
        var archetype = new AvatarArchetype
        {
            RefName = "Merchant",
            DisplayName = "Merchant",
            Description = "A test merchant",
            AffinityRef = "Physical",
            SpawnStats = new CharacterStats
            {
                Health = 1.0f,
                Stamina = 1.0f,
                Mana = 1.0f,
                Temperature = 37f,
                Strength = 0.10f,
                Defense = 0.10f,
                Magic = 0.10f,
                Speed = 0.10f,
                Endurance = 0f,
                Credits = 1000,
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
                Credits = 100,
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

        var avatar = new AvatarBase
        {
            ArchetypeRef = "Merchant",
            DisplayName = name
        };

        AvatarSpawner.SpawnFromModelAvatar(
            avatar,
            archetype);

        return avatar;
    }

    [Fact]
    public async Task StartDialogue_ValidMerchant_CreatesDialogueStartedTransaction()
    {
        // Arrange
        var avatarId = Guid.NewGuid();
        var avatar = CreateAvatar();
        avatar.AvatarId = avatarId;
        var characterInstanceId = await SpawnMerchant(avatarId, "VillageMerchant");

        var command = new StartDialogueCommand
        {
            AvatarId = avatarId,
            ArcRef = "VillageMerchant",
            CharacterInstanceId = characterInstanceId,
            Avatar = avatar
        };

        // Act
        var result = await _mediator.Send(command);

        // Assert
        Assert.True(result.Successful, $"Command failed: {result.ErrorMessage}");
        Assert.NotEmpty(result.TransactionIds);

        // Verify DialogueStarted transaction was created
        var instance = await _repository.GetOrCreateInstanceAsync(avatarId, "VillageMerchant");
        var dialogueTx = instance.GetCommittedTransactions()
            .FirstOrDefault(t => t.Type == ArcTransactionType.DialogueStarted);

        Assert.NotNull(dialogueTx);
        Assert.Equal(characterInstanceId.ToString(), dialogueTx.Data["CharacterInstanceId"]);
        Assert.Equal("MerchantDialogue", dialogueTx.Data["DialogueTreeRef"]);
    }

    [Fact]
    public async Task StartDialogue_MultipleDialogues_AllTracked()
    {
        // Arrange
        var avatarId = Guid.NewGuid();
        var avatar = CreateAvatar();
        avatar.AvatarId = avatarId;
        var characterInstanceId = await SpawnMerchant(avatarId, "VillageMerchant");

        // Act - Start dialogue twice
        await _mediator.Send(new StartDialogueCommand
        {
            AvatarId = avatarId,
            ArcRef = "VillageMerchant",
            CharacterInstanceId = characterInstanceId,
            Avatar = avatar
        });

        await _mediator.Send(new StartDialogueCommand
        {
            AvatarId = avatarId,
            ArcRef = "VillageMerchant",
            CharacterInstanceId = characterInstanceId,
            Avatar = avatar
        });

        // Assert
        var instance = await _repository.GetOrCreateInstanceAsync(avatarId, "VillageMerchant");
        var dialogueTransactions = instance.GetCommittedTransactions()
            .Where(t => t.Type == ArcTransactionType.DialogueStarted)
            .ToList();

        Assert.Equal(2, dialogueTransactions.Count);
    }

    [Fact]
    public async Task StartDialogue_NonExistentCharacter_ReturnsFailure()
    {
        // Arrange
        var avatarId = Guid.NewGuid();
        var avatar = CreateAvatar();
        avatar.AvatarId = avatarId;
        var fakeCharacterInstanceId = Guid.NewGuid();

        var command = new StartDialogueCommand
        {
            AvatarId = avatarId,
            ArcRef = "VillageMerchant",
            CharacterInstanceId = fakeCharacterInstanceId,
            Avatar = avatar
        };

        // Act
        var result = await _mediator.Send(command);

        // Assert
        Assert.False(result.Successful);
        Assert.Contains("not found", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task StartDialogue_InvalidArcRef_ReturnsFailure()
    {
        // Arrange
        var avatarId = Guid.NewGuid();
        var avatar = CreateAvatar();
        avatar.AvatarId = avatarId;
        var characterInstanceId = Guid.NewGuid();

        var command = new StartDialogueCommand
        {
            AvatarId = avatarId,
            ArcRef = "NonExistentArc",
            CharacterInstanceId = characterInstanceId,
            Avatar = avatar
        };

        // Act
        var result = await _mediator.Send(command);

        // Assert
        Assert.False(result.Successful);
        Assert.Contains("not found", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task StartDialogue_PipelineExecutes_TransactionsProperlySaved()
    {
        // Arrange
        var avatarId = Guid.NewGuid();
        var avatar = CreateAvatar();
        avatar.AvatarId = avatarId;
        var characterInstanceId = await SpawnMerchant(avatarId, "VillageMerchant");

        var command = new StartDialogueCommand
        {
            AvatarId = avatarId,
            ArcRef = "VillageMerchant",
            CharacterInstanceId = characterInstanceId,
            Avatar = avatar
        };

        // Act
        var result = await _mediator.Send(command);

        // Assert
        Assert.True(result.Successful);

        // Verify all transactions are committed
        var instance = await _repository.GetOrCreateInstanceAsync(avatarId, "VillageMerchant");
        Assert.All(instance.Transactions, tx =>
            Assert.Equal(TransactionStatus.Committed, tx.Status));

        // Verify sequence numbers are incremented
        Assert.True(result.NewSequenceNumber > 0);
    }

    public void Dispose()
    {
        _database?.Dispose();
        _serviceProvider?.Dispose();
    }
}
