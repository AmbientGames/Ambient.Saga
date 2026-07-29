using Ambient.Domain;
using Ambient.Domain.Contracts;
using Ambient.Domain.Partials;
using Ambient.Domain.GameLogic.Gameplay.Avatar;
using Ambient.Rpg.Engine.Application.Behaviors;
using Ambient.Rpg.Engine.Application.Commands.Arcs;
using Ambient.Rpg.Engine.Application.Queries.Arcs;
using Ambient.Rpg.Engine.Application.ReadModels;
using Ambient.Rpg.Engine.Application.Results.Arcs;
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
/// Proximity assault: a spawned, alive character whose EFFECTIVE traits (template
/// traits merged with replayed TraitAssigned/TraitRemoved transactions) include
/// Hostile — and no truce trait (Disengaged from a successful flee, or Spared) —
/// initiates battle when the avatar is inside its ApproachRadius. The engine
/// surfaces this as IsAssault on the arbiter result (GetInitiatedInteractionQuery)
/// so hosts never re-derive trait logic.
/// </summary>
[Collection("Sequential CQRS Tests")]
public class ProximityAssaultTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly IMediator _mediator;
    private readonly IWorld _world;
    private readonly LiteDatabase _database;

    public ProximityAssaultTests()
    {
        _database = new LiteDatabase(new MemoryStream());
        _world = CreateWorldWithHostileRaider();

        var services = new ServiceCollection();

        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssemblyContaining<UpdateAvatarPositionCommand>();
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
    }

    private static World CreateWorldWithHostileRaider()
    {
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
                new CharacterSpawn { CharacterRef = "Raider" }
            }
        };

        // Hostile template trait + Interactable section: assault candidate
        var raider = new Character
        {
            RefName = "Raider",
            DisplayName = "Raider",
            Stats = new CharacterStats { Health = 100, Mana = 50 },
            Capabilities = new ItemCollection(),
            Traits = new[]
            {
                new CharacterTrait { Name = CharacterTraitType.Hostile }
            },
            Interactable = new Interactable
            {
                ApproachRadius = 100.0f
            }
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
                    Characters = new[] { raider },
                    AvatarArchetypes = Array.Empty<AvatarArchetype>(),
                    Achievements = Array.Empty<Achievement>(),
                    CharacterAffinities = Array.Empty<CharacterAffinity>(),
                    DialogueTrees = Array.Empty<DialogueTree>()
                }
            }
        };

        world.ArcLookup[arc.RefName] = arc;
        world.ArcTriggersLookup[arc.RefName] = new List<ArcTrigger> { trigger };
        world.CharactersLookup[raider.RefName] = raider;

        return world;
    }

    private static AvatarBase CreateAvatar(Guid avatarId)
    {
        var archetype = new AvatarArchetype
        {
            RefName = "TestWarrior",
            DisplayName = "Test Warrior",
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
                Speed = 0.10f
            },
            SpawnCapabilities = new ItemCollection
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
            ArchetypeRef = "TestWarrior",
            DisplayName = "Test Hero"
        };

        AvatarSpawner.SpawnFromModelAvatar(avatar, archetype);
        avatar.AvatarId = avatarId;
        return avatar;
    }

    /// <summary>
    /// Enters the trigger ring (spawning the Raider ~15 m from the avatar) and
    /// returns the arbiter's verdict at the arc center.
    /// </summary>
    private async Task<(Guid AvatarId, AvatarBase Avatar)> SpawnRaiderAsync()
    {
        var avatarId = Guid.NewGuid();
        var avatar = CreateAvatar(avatarId);

        var positionResult = await _mediator.Send(new UpdateAvatarPositionCommand
        {
            AvatarId = avatarId,
            ArcRef = "TestArc",
            Latitude = 35.0,
            Longitude = 139.0,
            Avatar = avatar
        });

        Assert.True(positionResult.Successful, $"Position command failed: {positionResult.ErrorMessage}");
        return (avatarId, avatar);
    }

    private async Task<InitiatedInteractionResult> QueryArbiterAsync(Guid avatarId, AvatarBase avatar)
    {
        return await _mediator.Send(new GetInitiatedInteractionQuery
        {
            AvatarId = avatarId,
            Latitude = 35.0,
            Longitude = 139.0,
            Avatar = avatar
        });
    }

    [Fact]
    public async Task HostileCharacterInRange_ArbiterReportsAssault()
    {
        var (avatarId, avatar) = await SpawnRaiderAsync();

        var result = await QueryArbiterAsync(avatarId, avatar);

        Assert.True(result.HasInteraction);
        Assert.NotNull(result.Character);
        Assert.Equal("Raider", result.Character!.CharacterRef);
        Assert.True(result.IsAssault, "Effectively-Hostile character in range must be an assault");
        Assert.True(result.Character.Options.IsAssault);
    }

    [Fact]
    public async Task DisengagedTrait_SuppressesAssault_ButKeepsInteraction()
    {
        // Disengaged is what a successful flee assigns to the enemy
        // (ExecuteBattleTurnHandler -> TraitAssigned). The arbiter reads the
        // character's EFFECTIVE traits from replayed state, so the fled-from enemy
        // stops assaulting while remaining an interaction candidate.
        var (avatarId, avatar) = await SpawnRaiderAsync();

        var assignResult = await _mediator.Send(new AssignTraitCommand
        {
            AvatarId = avatarId,
            ArcRef = "TestArc",
            CharacterRef = "Raider",
            TraitType = "Disengaged",
            Reason = "Avatar fled the battle"
        });
        Assert.True(assignResult.Successful, $"AssignTrait failed: {assignResult.ErrorMessage}");

        var result = await QueryArbiterAsync(avatarId, avatar);

        Assert.True(result.HasInteraction);
        Assert.NotNull(result.Character);
        Assert.False(result.IsAssault, "Disengaged must suppress the assault");
        Assert.False(result.Character!.Options.IsAssault);
        Assert.True(result.Character.State.Traits.ContainsKey("Hostile"), "Hostile survives; only the truce suppresses");
        Assert.True(result.Character.State.Traits.ContainsKey("Disengaged"));
    }

    [Fact]
    public void RespawnedInstance_DoesNotInheritDisengaged()
    {
        // Disengaged is per-encounter combat state: a fresh spawn of the same
        // character template is a fresh encounter and assaults again. Other
        // dialogue-assigned traits still carry over to new spawns.
        var arc = _world.ArcLookup["TestArc"];
        var triggers = _world.ArcTriggersLookup["TestArc"];
        var stateMachine = new ArcStateMachine(arc, triggers, _world);

        var instance = new ArcInstance { ArcRef = "TestArc" };
        var firstInstanceId = Guid.NewGuid();
        var respawnInstanceId = Guid.NewGuid();

        instance.AddTransaction(new ArcTransaction
        {
            Type = ArcTransactionType.CharacterSpawned,
            Status = TransactionStatus.Committed,
            SequenceNumber = 1,
            Data = new()
            {
                ["CharacterInstanceId"] = firstInstanceId.ToString(),
                ["CharacterRef"] = "Raider",
                ["Latitude"] = "35.0",
                ["Longitude"] = "139.0",
                ["Y"] = "50.0"
            }
        });

        // Avatar flees: the live instance gets Disengaged
        instance.AddTransaction(new ArcTransaction
        {
            Type = ArcTransactionType.TraitAssigned,
            AvatarId = "Avatar1",
            Status = TransactionStatus.Committed,
            SequenceNumber = 2,
            Data = new()
            {
                ["CharacterRef"] = "Raider",
                ["TraitType"] = "Disengaged"
            }
        });

        // Later respawn of the same template: a NEW instance
        instance.AddTransaction(new ArcTransaction
        {
            Type = ArcTransactionType.CharacterSpawned,
            Status = TransactionStatus.Committed,
            SequenceNumber = 3,
            Data = new()
            {
                ["CharacterInstanceId"] = respawnInstanceId.ToString(),
                ["CharacterRef"] = "Raider",
                ["Latitude"] = "35.0",
                ["Longitude"] = "139.0",
                ["Y"] = "50.0"
            }
        });

        var state = stateMachine.ReplayToNow(instance);

        var fledFrom = state.Characters[firstInstanceId.ToString()];
        Assert.True(fledFrom.Traits.ContainsKey("Disengaged"), "The fled-from instance keeps its truce");

        var respawned = state.Characters[respawnInstanceId.ToString()];
        Assert.True(respawned.Traits.ContainsKey("Hostile"), "Template trait copied on spawn");
        Assert.False(respawned.Traits.ContainsKey("Disengaged"), "Respawned instances start fresh — no inherited truce");
    }

    public void Dispose()
    {
        _database?.Dispose();
        _serviceProvider?.Dispose();
    }
}
