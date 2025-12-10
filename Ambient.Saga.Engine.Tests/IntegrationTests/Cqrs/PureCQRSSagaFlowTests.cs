using Ambient.Domain;
using Ambient.Domain.DefinitionExtensions;
using Ambient.Domain.GameLogic.Gameplay.Avatar;
using Ambient.Saga.Engine.Application.Behaviors;
using Ambient.Saga.Engine.Application.Commands.Saga;
using Ambient.Saga.Engine.Application.Queries.Saga;
using Ambient.Saga.Engine.Application.ReadModels;
using Ambient.Saga.Engine.Contracts.Cqrs;
using Ambient.Saga.Engine.Contracts.Services;
using Ambient.Saga.Engine.Infrastructure.Persistence;
using LiteDB;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace Ambient.Saga.Engine.Tests.IntegrationTests.Cqrs;

/// <summary>
/// Demonstrates the pure CQRS pattern for Saga interactions.
///
/// KEY PRINCIPLE:
/// - Commands are WRITE-ONLY (just create transactions, return success/failure)
/// - Queries are READ-ONLY (replay state, return what's available)
/// - NEVER mix them - command results don't contain state, queries provide state
///
/// FLOW:
/// 1. Client sends command: UpdateAvatarPosition
/// 2. Saga creates transactions (spawns, triggers, etc.)
/// 3. Client queries: GetAvailableInteractions - "what can I do?"
/// 4. UI shows options based on query result
/// 5. Player acts: StartDialogue, Trade, Attack, etc.
/// 6. Client queries again to update UI
///
/// This is how you "put Sagas away and never think about them again" - the pattern is stable.
/// </summary>
[Collection("Sequential CQRS Tests")]
public class PureCQRSSagaFlowTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly ServiceProvider _serviceProvider;
    private readonly IMediator _mediator;
    private readonly World _world;
    private readonly LiteDatabase _database;
    private readonly ISagaInstanceRepository _repository;

    public PureCQRSSagaFlowTests(ITestOutputHelper output)
    {
        _output = output;
        _database = new LiteDatabase(new MemoryStream());
        _world = CreateTestWorld();

        var services = new ServiceCollection();

        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssemblyContaining<UpdateAvatarPositionCommand>();
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

    private World CreateTestWorld()
    {
        // Create a merchant character
        var merchant = new Character
        {
            RefName = "TownMerchant",
            DisplayName = "Yuki the Merchant",
            Interactable = new Interactable
            {
                DialogueTreeRef = "MerchantGreeting",
                ApproachRadius = 15.0f // Must be >= 10.0 to reach characters that spawn at 10m
            }
        };

        // Create a boss character
        var boss = new Character
        {
            RefName = "SamuraiLord",
            DisplayName = "Lord Takeda",
            Interactable = new Interactable
            {
                DialogueTreeRef = "BossChallenge",
                ApproachRadius = 15.0f // Must be >= 10.0 to reach characters that spawn at 10m
            }
        };

        // Create Saga with two triggers
        var sagaArc = new SagaArc
        {
            RefName = "TownSquare",
            DisplayName = "Town Square",
            LatitudeZ = 35.6762,
            LongitudeX = 139.6503
        };

        var merchantTrigger = new SagaTrigger
        {
            RefName = "MarketArea",
            EnterRadius = 50.0f,
            Spawn = new[]
            {
                new CharacterSpawn
                {
                    ItemElementName = ItemChoiceType.CharacterRef,
                    Item = "TownMerchant"
                }
            }
        };

        var bossTrigger = new SagaTrigger
        {
            RefName = "DojoEntrance",
            EnterRadius = 30.0f,
            Spawn = new[]
            {
                new CharacterSpawn
                {
                    ItemElementName = ItemChoiceType.CharacterRef,
                    Item = "SamuraiLord"
                }
            }
        };

        // Create dialogue trees for the characters
        var merchantDialogue = new DialogueTree
        {
            RefName = "MerchantGreeting",
            StartNodeId = "greeting",
            Node = new[]
            {
                new DialogueNode
                {
                    NodeId = "greeting",
                    Text = new[] { "Welcome, traveler! What can I offer you today?" },
                    Choice = Array.Empty<DialogueChoice>()
                }
            }
        };

        var bossDialogue = new DialogueTree
        {
            RefName = "BossChallenge",
            StartNodeId = "challenge",
            Node = new[]
            {
                new DialogueNode
                {
                    NodeId = "challenge",
                    Text = new[] { "You dare approach me? Prepare yourself!" },
                    Choice = Array.Empty<DialogueChoice>()
                }
            }
        };

        var world = new World
        {
            WorldConfiguration = new WorldConfiguration
            {
                RefName = "TestWorld",
                SpawnLatitude = 35.6762,
                SpawnLongitude = 139.6503,
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
                    Characters = new[] { merchant, boss },
                    CharacterArchetypes = Array.Empty<CharacterArchetype>(),
                    AvatarArchetypes = Array.Empty<AvatarArchetype>(),
                    Achievements = Array.Empty<Achievement>(),
                    CharacterAffinities = Array.Empty<CharacterAffinity>(),
                    DialogueTrees = new[] { merchantDialogue, bossDialogue }
                },
                //Simulation = new SimulationComponents(),
                //Presentation = new PresentationComponents()
            }
        };

        world.SagaArcLookup[sagaArc.RefName] = sagaArc;
        world.SagaTriggersLookup[sagaArc.RefName] = new List<SagaTrigger> { merchantTrigger, bossTrigger };
        world.CharactersLookup[merchant.RefName] = merchant;
        world.CharactersLookup[boss.RefName] = boss;
        world.DialogueTreesLookup[merchantDialogue.RefName] = merchantDialogue;
        world.DialogueTreesLookup[bossDialogue.RefName] = bossDialogue;

        return world;
    }

    private AvatarBase CreateAvatar(string name = "Test Hero")
    {
        var archetype = new AvatarArchetype
        {
            RefName = "Samurai",
            DisplayName = "Samurai",
            Description = "A skilled warrior",
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
                Credits = 1000,
                Experience = 0,
                Strength = 0.15f,
                Defense = 0.12f,
                Speed = 0.10f,
                Magic = 0.05f
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
                Credits = 100,
                Experience = 0,
                Strength = 0.10f,
                Defense = 0.08f,
                Speed = 0.08f,
                Magic = 0.03f
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
            ArchetypeRef = "Samurai",
            DisplayName = name,
            BlockOwnership = new Dictionary<string, int>()
        };

        AvatarSpawner.SpawnFromModelAvatar(
            avatar,
            archetype);

        return avatar;
    }

    [Fact]
    public async Task PureCQRSFlow_MoveQueryInteract_DemonstratesPattern()
    {
        _output.WriteLine("=== PURE CQRS SAGA FLOW ===");
        _output.WriteLine("");

        var avatarId = Guid.NewGuid();
        var avatar = CreateAvatar("Hanzo");
        avatar.AvatarId = avatarId;

        _output.WriteLine($"Avatar: {avatar.DisplayName} (ID: {avatarId})");
        _output.WriteLine($"Starting Credits: {avatar.Stats.Credits}");
        _output.WriteLine("");

        // ================================================================
        // STEP 1: COMMAND - Move avatar (Saga decides what happens)
        // ================================================================
        _output.WriteLine("--- STEP 1: COMMAND (Write) ---");
        _output.WriteLine("Action: UpdateAvatarPosition → Saga center");
        _output.WriteLine("");

        var moveResult = await _mediator.Send(new UpdateAvatarPositionCommand
        {
            AvatarId = avatarId,
            SagaArcRef = "TownSquare",
            Latitude = 35.6762,  // Saga center - both triggers will activate
            Longitude = 139.6503,
            Y = 50.0,
            Avatar = avatar
        });

        // Command only tells us success/failure + transaction info
        Assert.True(moveResult.Successful, $"Move failed: {moveResult.ErrorMessage}");
        _output.WriteLine($"✓ Command succeeded");
        _output.WriteLine($"  Transactions created: {moveResult.TransactionIds.Count}");
        _output.WriteLine($"  Sequence number: {moveResult.NewSequenceNumber}");
        _output.WriteLine("");

        // ================================================================
        // STEP 2: QUERY - Ask Saga "what can I do?"
        // ================================================================
        _output.WriteLine("--- STEP 2: QUERY (Read) ---");
        _output.WriteLine("Query: GetAvailableInteractions → What's available?");
        _output.WriteLine("");

        var interactions = await _mediator.Send(new GetAvailableInteractionsQuery
        {
            AvatarId = avatarId,
            SagaRef = "TownSquare",
            Latitude = 35.6762,
            Longitude = 139.6503,
            Avatar = avatar
        });

        // Query tells us current state
        _output.WriteLine($"  Saga discovered: {interactions.SagaDiscovered}");
        _output.WriteLine($"  Nearby characters: {interactions.NearbyCharacters.Count}");
        _output.WriteLine("");

        // Verify we got characters (this is what matters for the CQRS pattern)
        Assert.NotEmpty(interactions.NearbyCharacters);

        foreach (var character in interactions.NearbyCharacters)
        {
            _output.WriteLine($"  Character: {character.DisplayName}");
            _output.WriteLine($"    Can Dialogue: {character.Options.CanDialogue}");
            _output.WriteLine($"    Can Trade: {character.Options.CanTrade}");
            _output.WriteLine($"    Can Attack: {character.Options.CanAttack}");
            if (character.Options.CanDialogue)
            {
                _output.WriteLine($"    Dialogue Tree: {character.Options.DialogueTreeRef}");
            }
        }
        _output.WriteLine("");

        // Verify we have both characters
        var merchant = interactions.NearbyCharacters.FirstOrDefault(c => c.CharacterRef == "TownMerchant");
        var boss = interactions.NearbyCharacters.FirstOrDefault(c => c.CharacterRef == "SamuraiLord");

        Assert.NotNull(merchant);
        Assert.NotNull(boss);
        Assert.True(merchant.Options.CanDialogue);
        Assert.True(merchant.Options.CanTrade);
        Assert.True(boss.Options.CanDialogue);
        Assert.True(boss.Options.CanAttack);

        // ================================================================
        // STEP 3: COMMAND - Player chooses to talk to merchant
        // ================================================================
        _output.WriteLine("--- STEP 3: COMMAND (Write) ---");
        _output.WriteLine($"Action: StartDialogue with {merchant.DisplayName}");
        _output.WriteLine("");

        var dialogueResult = await _mediator.Send(new StartDialogueCommand
        {
            AvatarId = avatarId,
            SagaArcRef = "TownSquare",
            CharacterInstanceId = merchant.CharacterInstanceId,
            Avatar = avatar
        });

        Assert.True(dialogueResult.Successful, $"Dialogue failed: {dialogueResult.ErrorMessage}");
        _output.WriteLine($"✓ Dialogue started");
        _output.WriteLine($"  Transactions created: {dialogueResult.TransactionIds.Count}");
        _output.WriteLine("");

        // ================================================================
        // STEP 4: QUERY - Check available interactions again
        // ================================================================
        _output.WriteLine("--- STEP 4: QUERY (Read) ---");
        _output.WriteLine("Query: GetAvailableInteractions → State after dialogue");
        _output.WriteLine("");

        var interactions2 = await _mediator.Send(new GetAvailableInteractionsQuery
        {
            AvatarId = avatarId,
            SagaRef = "TownSquare",
            Latitude = 35.6762,
            Longitude = 139.6503,
            Avatar = avatar
        });

        // Characters should still be there
        Assert.Equal(2, interactions2.NearbyCharacters.Count);
        _output.WriteLine($"✓ Characters still available: {interactions2.NearbyCharacters.Count}");
        _output.WriteLine("");

        // ================================================================
        // SUMMARY
        // ================================================================
        _output.WriteLine("=== CQRS PATTERN DEMONSTRATED ===");
        _output.WriteLine("");
        _output.WriteLine("✓ Commands create transactions (write-only)");
        _output.WriteLine("✓ Queries read state from transaction log (read-only)");
        _output.WriteLine("✓ Client decides WHEN to interact (player agency)");
        _output.WriteLine("✓ Saga decides WHAT is available (server authoritative)");
        _output.WriteLine("");
        _output.WriteLine("This is the stable pattern. Put Sagas away and never think about them again.");
    }

    public void Dispose()
    {
        _database?.Dispose();
        _serviceProvider?.Dispose();
    }
}
