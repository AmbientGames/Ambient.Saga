using Ambient.Domain;
using Ambient.Domain.Contracts;
using Ambient.Domain.Partials;
using Ambient.Domain.Entities;
using Ambient.Saga.Engine.Application.Behaviors;
using Ambient.Saga.Engine.Application.Commands.Saga;
using Ambient.Saga.Engine.Application.Queries.Saga;
using Ambient.Saga.Engine.Application.ReadModels;
using Ambient.Saga.Engine.Application.Services;
using Ambient.Saga.Engine.Contracts;
using Ambient.Saga.Engine.Contracts.Cqrs;
using Ambient.Saga.Engine.Contracts.Persistence;
using Ambient.Saga.Engine.Contracts.Services;
using Ambient.Saga.Engine.Tests.Helpers;
using Ambient.Saga.Engine.Domain.Rpg.Battle;
using Ambient.Saga.Engine.Domain.Rpg.Sagas.TransactionLog;
using Ambient.Saga.Engine.Infrastructure.Persistence;
using LiteDB;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace Ambient.Saga.Engine.Tests.IntegrationTests.Cqrs;

/// <summary>
/// Comprehensive integration tests for battle system via CQRS commands.
///
/// Uses BattleSetup helper to create properly configured StartBattleCommand objects.
/// The battle system uses turn-based combat via:
///   - StartBattleCommand: Initializes battle, creates BattleStarted transaction
///   - ExecuteBattleTurnCommand: Executes one avatar + one enemy turn
///
/// TESTS:
/// 1. StartBattle_CreatesValidTransactions - Verifies battle initialization ? UPDATED
/// 2. Battle replay determinism - Same seed = same outcome (TODO: update for turn-based)
/// 3. Equipment degradation during battle (TODO: update for turn-based)
/// 4. Affinity switching mid-battle (TODO: update for turn-based)
/// 5. Character defeat and loot drops (TODO: update for turn-based)
/// 6. Living character despawn on zone exit (TODO: update for turn-based)
/// 7. Defeated character persistence (corpse remains) (TODO: update for turn-based)
///
/// VALIDATES:
/// - BattleSetup properly creates Combatant objects from templates
/// - Transaction log completeness (BattleStarted/TurnExecuted/Ended)
/// - Deterministic battle replay with fixed random seed
/// </summary>
[Collection("Sequential CQRS Tests")]
public class BattleCommandTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly ServiceProvider _serviceProvider;
    private readonly IMediator _mediator;
    private readonly IWorld _world;
    private readonly LiteDatabase _database;
    private readonly ISagaInstanceRepository _repository;

    public BattleCommandTests(ITestOutputHelper output)
    {
        _output = output;
        _database = new LiteDatabase(new MemoryStream());
        _world = CreateBattleTestWorld();

        var services = new ServiceCollection();

        // Register MediatR with all handlers and behaviors
        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssemblyContaining<StartBattleCommand>();
            cfg.AddOpenBehavior(typeof(SagaLoggingBehavior<,>));
            cfg.AddOpenBehavior(typeof(SagaValidationBehavior<,>));
            cfg.AddOpenBehavior(typeof(AchievementEvaluationBehavior<,>));
        });

        // Register dependencies
        services.AddSingleton(_world);
        services.AddSingleton<ISagaInstanceRepository>(new SagaInstanceRepository(_database));
        services.AddSingleton<IAvatarProgressRepository>(new AvatarProgressRepository(_database));
        services.AddSingleton<ISagaReadModelRepository, InMemorySagaReadModelRepository>();
        services.AddSingleton<IAvatarUpdateService, StubAvatarUpdateService>();
        services.AddSingleton<IWorldStateRepository, StubWorldStateRepository>();

        _serviceProvider = services.BuildServiceProvider();
        _mediator = _serviceProvider.GetRequiredService<IMediator>();
        _repository = _serviceProvider.GetRequiredService<ISagaInstanceRepository>();
    }

    [Fact]
    public async Task StartBattle_CreatesValidTransactions()
    {
        // ARRANGE: Spawn a weak boss
        var avatarId = Guid.NewGuid();
        var avatar = CreateWarriorAvatar(avatarId);
        var sagaRef = "WeakBossSaga";

        // Spawn boss
        await _mediator.Send(new UpdateAvatarPositionCommand
        {
            AvatarId = avatarId,
            SagaArcRef = sagaRef,
            Latitude = 35.0,
            Longitude = 139.0,
            Avatar = avatar
        });

        var instance = await _repository.GetOrCreateInstanceAsync(avatarId, sagaRef);
        var bossInstanceId = Guid.Parse(
            instance.GetCommittedTransactions()
                .First(t => t.Type == SagaTransactionType.CharacterSpawned)
                .Data["CharacterInstanceId"]);

        _output.WriteLine($"Boss spawned: {bossInstanceId}");

        // ACT: Use BattleSetup to create proper StartBattleCommand
        var weakBoss = _world.CharactersLookup["WeakBoss"];
        var warriorArchetype = _world.AvatarArchetypesLookup["Warrior"];

        var battleSetup = new BattleSetup();
        battleSetup.SetupFromWorld(_world);
        battleSetup.SelectedAvatarArchetype = warriorArchetype;
        battleSetup.AvatarCapabilities = avatar.Capabilities ?? new ItemCollection();
        battleSetup.AvatarAffinityRefs = new List<string> { "Physical" };
        battleSetup.SelectedOpponentCharacter = weakBoss;
        battleSetup.OpponentCapabilities = weakBoss.Capabilities ?? new ItemCollection();

        var battleEngine = battleSetup.CreateBattleEngine();
        var avatarCombatant = battleEngine.GetAvatar();
        var enemyCombatant = battleEngine.GetEnemy();

        var battleResult = await _mediator.Send(new StartBattleCommand
        {
            AvatarId = avatarId,
            SagaArcRef = sagaRef,
            EnemyCharacterInstanceId = bossInstanceId,
            AvatarCombatant = avatarCombatant,
            EnemyCombatant = enemyCombatant,
            AvatarAffinityRefs = new List<string> { "Physical" },
            EnemyMind = new CombatAI(_world),
            RandomSeed = 12345,  // Fixed seed for determinism
            Avatar = avatar
        });

        // ASSERT: Battle started successfully
        Assert.True(battleResult.Successful, $"Battle start failed: {battleResult.ErrorMessage}");

        var finalInstance = await _repository.GetOrCreateInstanceAsync(avatarId, sagaRef);
        var transactions = finalInstance.GetCommittedTransactions();

        // Verify BattleStarted transaction created
        Assert.Contains(transactions, t => t.Type == SagaTransactionType.BattleStarted);

        // Verify enemy's opening turn was executed (enemy moves first)
        var turnTransactions = transactions.Where(t => t.Type == SagaTransactionType.BattleTurnExecuted).ToList();
        Assert.NotEmpty(turnTransactions);

        var battleStarted = transactions.First(t => t.Type == SagaTransactionType.BattleStarted);
        Assert.Equal("12345", battleStarted.Data["RandomSeed"]);
        Assert.True(battleStarted.Data.ContainsKey("AvatarHealth"));
        Assert.True(battleStarted.Data.ContainsKey("EnemyHealth"));

        _output.WriteLine($"? Battle started with {turnTransactions.Count} turn(s) executed");
        _output.WriteLine($"  Random Seed: {battleStarted.Data["RandomSeed"]}");
        _output.WriteLine($"  Avatar HP: {battleStarted.Data["AvatarHealth"]}");
        _output.WriteLine($"  Enemy HP: {battleStarted.Data["EnemyHealth"]}");
    }

    [Fact]
    public async Task ExecuteBattleTurn_StateMatchesRecordedTurnResults_NoDoubleApplication()
    {
        // ARRANGE: spawn boss and start a battle (same flow as StartBattle test)
        var avatarId = Guid.NewGuid();
        var avatar = CreateWarriorAvatar(avatarId);
        var sagaRef = "WeakBossSaga";

        await _mediator.Send(new UpdateAvatarPositionCommand
        {
            AvatarId = avatarId,
            SagaArcRef = sagaRef,
            Latitude = 35.0,
            Longitude = 139.0,
            Avatar = avatar
        });

        var instance = await _repository.GetOrCreateInstanceAsync(avatarId, sagaRef);
        var bossInstanceId = Guid.Parse(
            instance.GetCommittedTransactions()
                .First(t => t.Type == SagaTransactionType.CharacterSpawned)
                .Data["CharacterInstanceId"]);

        var weakBoss = _world.CharactersLookup["WeakBoss"];
        var warriorArchetype = _world.AvatarArchetypesLookup["Warrior"];

        var battleSetup = new BattleSetup();
        battleSetup.SetupFromWorld(_world);
        battleSetup.SelectedAvatarArchetype = warriorArchetype;
        battleSetup.AvatarCapabilities = avatar.Capabilities ?? new ItemCollection();
        battleSetup.AvatarAffinityRefs = new List<string> { "Physical" };
        battleSetup.SelectedOpponentCharacter = weakBoss;
        battleSetup.OpponentCapabilities = weakBoss.Capabilities ?? new ItemCollection();

        var battleEngine = battleSetup.CreateBattleEngine();

        var battleResult = await _mediator.Send(new StartBattleCommand
        {
            AvatarId = avatarId,
            SagaArcRef = sagaRef,
            EnemyCharacterInstanceId = bossInstanceId,
            AvatarCombatant = battleEngine.GetAvatar(),
            EnemyCombatant = battleEngine.GetEnemy(),
            AvatarAffinityRefs = new List<string> { "Physical" },
            EnemyMind = new CombatAI(_world, 12345),
            RandomSeed = 12345,
            Avatar = avatar
        });
        Assert.True(battleResult.Successful, $"Battle start failed: {battleResult.ErrorMessage}");

        var battleId = (await _repository.GetOrCreateInstanceAsync(avatarId, sagaRef))
            .GetCommittedTransactions()
            .First(t => t.Type == SagaTransactionType.BattleStarted)
            .TransactionId;

        // ACT: one avatar attack (+ enemy response) through the CQRS layer
        var turnResult = await _mediator.Send(new ExecuteBattleTurnCommand
        {
            AvatarId = avatarId,
            SagaArcRef = sagaRef,
            BattleInstanceId = battleId,
            AvatarAction = new CombatAction { ActionType = ActionType.Attack },
            Avatar = avatar
        });
        Assert.True(turnResult.Successful, $"Turn failed: {turnResult.ErrorMessage}");

        // ASSERT: reconstructed state equals the recorded per-turn results exactly
        var state = await _mediator.Send(new GetBattleStateQuery
        {
            AvatarId = avatarId,
            SagaRef = sagaRef,
            BattleInstanceId = battleId,
            Avatar = avatar
        });
        Assert.True(state.IsActive || state.HasEnded);

        var turnTxs = (await _repository.GetOrCreateInstanceAsync(avatarId, sagaRef))
            .GetCommittedTransactions()
            .Where(t => t.Type == SagaTransactionType.BattleTurnExecuted &&
                        t.Data["BattleTransactionId"] == battleId.ToString())
            .OrderBy(t => t.SequenceNumber)
            .ToList();

        // The avatar's recorded hit must carry a real (non-garbage) enemy health,
        // and the reconstructed enemy health must equal it — applied exactly once
        var avatarHit = turnTxs.Last(t => bool.Parse(t.Data["IsAvatarTurn"]));
        var recordedEnemyHealth = float.Parse(avatarHit.Data["TargetHealthAfter"]);
        var initialEnemyHealth = weakBoss.Stats.Health;

        Assert.True(recordedEnemyHealth > 0f, "Recorded enemy health should not be zero after one weak hit");
        Assert.True(recordedEnemyHealth < initialEnemyHealth, "Avatar's hit should have reduced enemy health");
        Assert.Equal(recordedEnemyHealth, state.EnemyCombatant!.Health, 3);

        // Enemy's response (no tells in this world) must land real damage on the avatar
        var enemyHit = turnTxs.LastOrDefault(t => !bool.Parse(t.Data["IsAvatarTurn"]));
        if (enemyHit != null)
        {
            var recordedAvatarHealth = float.Parse(enemyHit.Data["TargetHealthAfter"]);
            Assert.Equal(recordedAvatarHealth, state.AvatarCombatant!.Health, 3);
        }
    }

    public void Dispose()
    {
        _database?.Dispose();
        _serviceProvider?.Dispose();
    }

    #region Test World Setup

    private World CreateBattleTestWorld()
    {
        // Weak boss for testing victories
        var weakBossSaga = new SagaArc
        {
            RefName = "WeakBossSaga",
            DisplayName = "Weak Boss Arena",
            Latitude = 35.0,
            Longitude = 139.0
        };

        var multiEnemySaga = new SagaArc
        {
            RefName = "MultiEnemySaga",
            DisplayName = "Multi-Enemy Arena",
            Latitude = 35.0,
            Longitude = 139.0
        };

        var weakBossTrigger = new SagaTrigger
        {
            RefName = "WeakBossTrigger",
            EnterRadius = 100.0f,
            Spawn = new[]
            {
                new CharacterSpawn
                {
                    CharacterRef = "WeakBoss"
                }
            }
        };

        var multiEnemyTrigger = new SagaTrigger
        {
            RefName = "MultiEnemyTrigger",
            EnterRadius = 100.0f,
            Spawn = new[]
            {
                new CharacterSpawn { CharacterRef = "Goblin" },
                new CharacterSpawn { CharacterRef = "Goblin" },
                new CharacterSpawn { CharacterRef = "Goblin" }
            }
        };

        var weakBoss = new Character
        {
            RefName = "WeakBoss",
            DisplayName = "Weak Training Dummy",
            Stats = new CharacterStats
            {
                Health = 0.5f, // Half health
                Strength = 0.05f, // Very weak
                Defense = 0.05f
            },
            Capabilities = new ItemCollection
            {
                Equipment = new[]
                {
                    new EquipmentEntry { EquipmentRef = "GoldCoin", Condition = 1.0f }
                }
            }
        };

        var goblin = new Character
        {
            RefName = "Goblin",
            DisplayName = "Cave Goblin",
            Stats = new CharacterStats
            {
                Health = 0.6f,
                Strength = 0.08f,
                Defense = 0.05f
            }
        };

        var goldCoin = new Equipment
        {
            RefName = "GoldCoin",
            DisplayName = "Gold Coin",
            WholesalePrice = 100
        };

        var ironSword = new Equipment
        {
            RefName = "IronSword",
            DisplayName = "Iron Sword",
            WholesalePrice = 50,
            SlotRef = "MainHand"
        };

        // Create Warrior archetype for BattleSetup
        var warriorArchetype = new AvatarArchetype
        {
            RefName = "Warrior",
            DisplayName = "Warrior",
            AffinityRef = "Physical",
            SpawnStats = new CharacterStats
            {
                Health = 1.0f,
                Stamina = 1.0f,
                Mana = 1.0f,
                Strength = 0.20f,
                Defense = 0.15f,
                Speed = 0.12f,
                Magic = 0.10f
            },
            SpawnCapabilities = new ItemCollection
            {
                Equipment = new[] { new EquipmentEntry { EquipmentRef = "IronSword", Condition = 1.0f } },
                Spells = Array.Empty<SpellEntry>(),
            }
        };

        var world = new World
        {
            IsProcedural = true,
            WorldConfiguration = new WorldConfiguration
            {
                RefName = "BattleTestWorld",
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
                    SagaArcs = new[] { weakBossSaga, multiEnemySaga },
                    Characters = new[] { weakBoss, goblin },
                    Equipment = new[] { goldCoin, ironSword },
                    AvatarArchetypes = new[] { warriorArchetype },
                    Achievements = Array.Empty<Achievement>(),
                    CharacterAffinities = new[] { CreatePhysicalAffinity() },
                    DialogueTrees = Array.Empty<DialogueTree>(),
                    Consumables = Array.Empty<Consumable>()
                },
                //Simulation = new SimulationComponents(),
                //Presentation = new PresentationComponents()
            }
        };

        // Populate lookups
        world.SagaArcLookup[weakBossSaga.RefName] = weakBossSaga;
        world.SagaArcLookup[multiEnemySaga.RefName] = multiEnemySaga;
        world.SagaTriggersLookup[weakBossSaga.RefName] = new List<SagaTrigger> { weakBossTrigger };
        world.SagaTriggersLookup[multiEnemySaga.RefName] = new List<SagaTrigger> { multiEnemyTrigger };
        world.CharactersLookup[weakBoss.RefName] = weakBoss;
        world.CharactersLookup[goblin.RefName] = goblin;
        world.EquipmentLookup[goldCoin.RefName] = goldCoin;
        world.EquipmentLookup[ironSword.RefName] = ironSword;
        world.AvatarArchetypesLookup[warriorArchetype.RefName] = warriorArchetype;
        world.CharacterAffinitiesLookup["Physical"] = CreatePhysicalAffinity();

        return world;
    }

    private AvatarEntity CreateWarriorAvatar(Guid avatarId)
    {
        return new AvatarEntity
        {
            Id = avatarId,
            AvatarId = avatarId,
            DisplayName = "Test Warrior",
            ArchetypeRef = "Warrior",
            Stats = new CharacterStats
            {
                Health = 1.0f,
                Stamina = 1.0f,
                Mana = 1.0f,
                Strength = 0.20f, // Strong enough to defeat weak enemies
                Defense = 0.15f,
                Speed = 0.12f,
                Magic = 0.10f
            },
            Capabilities = new ItemCollection
            {
                Equipment = new[]
                {
                    new EquipmentEntry { EquipmentRef = "IronSword", Condition = 1.0f }
                },
                Spells = Array.Empty<SpellEntry>(),
            },
            AffinityRef = "Physical"
        };
    }

    private CharacterAffinity CreatePhysicalAffinity()
    {
        return new CharacterAffinity
        {
            RefName = "Physical",
            DisplayName = "Physical",
            Description = "Physical combat affinity"
        };
    }

    #endregion
}
