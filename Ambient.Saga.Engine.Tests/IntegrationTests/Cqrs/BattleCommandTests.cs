using Ambient.Domain;
using Ambient.Domain.DefinitionExtensions;
using Ambient.Domain.Entities;
using Ambient.Saga.Engine.Application.Behaviors;
using Ambient.Saga.Engine.Application.Commands.Saga;
using Ambient.Saga.Engine.Application.ReadModels;
using Ambient.Saga.Engine.Application.Services;
using Ambient.Saga.Engine.Contracts.Cqrs;
using Ambient.Saga.Engine.Contracts.Services;
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
///   - ExecuteBattleTurnCommand: Executes one player + one enemy turn
///
/// TESTS:
/// 1. StartBattle_CreatesValidTransactions - Verifies battle initialization ✅ UPDATED
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
    private readonly World _world;
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
        services.AddSingleton<ISagaReadModelRepository, InMemorySagaReadModelRepository>();
        services.AddSingleton<IAvatarUpdateService, AvatarUpdateService>();

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
            Y = 50.0,
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
        var playerCombatant = battleEngine.GetPlayer();
        var enemyCombatant = battleEngine.GetEnemy();

        var battleResult = await _mediator.Send(new StartBattleCommand
        {
            AvatarId = avatarId,
            SagaArcRef = sagaRef,
            EnemyCharacterInstanceId = bossInstanceId,
            PlayerCombatant = playerCombatant,
            EnemyCombatant = enemyCombatant,
            PlayerAffinityRefs = new List<string> { "Physical" },
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
        Assert.True(battleStarted.Data.ContainsKey("PlayerHealth"));
        Assert.True(battleStarted.Data.ContainsKey("EnemyHealth"));

        _output.WriteLine($"✓ Battle started with {turnTransactions.Count} turn(s) executed");
        _output.WriteLine($"  Random Seed: {battleStarted.Data["RandomSeed"]}");
        _output.WriteLine($"  Player HP: {battleStarted.Data["PlayerHealth"]}");
        _output.WriteLine($"  Enemy HP: {battleStarted.Data["EnemyHealth"]}");
    }

    // TODO: Rewrite remaining tests to use BattleSetup and new turn-based API
    // These tests use old API signatures (CharacterInstanceId instead of EnemyCharacterInstanceId)
    // and old schema properties (CharacterType, BasePrice, Energy) that no longer exist.
    /*
    [Fact]
    public async Task BattleReplay_SameSeed_DeterministicOutcome()
    {
        // ARRANGE: Create two identical battles with same seed
        var avatarId1 = Guid.NewGuid();
        var avatarId2 = Guid.NewGuid();
        var avatar1 = CreateWarriorAvatar(avatarId1);
        var avatar2 = CreateWarriorAvatar(avatarId2);

        var sagaRef = "WeakBossSaga";
        var fixedSeed = 12345; // Same seed for determinism

        // Spawn bosses for both avatars
        await _mediator.Send(new UpdateAvatarPositionCommand
        {
            AvatarId = avatarId1,
            SagaRef = sagaRef,
            Latitude = 35.0,
            Longitude = 139.0,
            Y = 50.0,
            Avatar = avatar1
        });

        await _mediator.Send(new UpdateAvatarPositionCommand
        {
            AvatarId = avatarId2,
            SagaRef = sagaRef,
            Latitude = 35.0,
            Longitude = 139.0,
            Y = 50.0,
            Avatar = avatar2
        });

        var instance1 = await _repository.GetOrCreateInstanceAsync(avatarId1, sagaRef);
        var instance2 = await _repository.GetOrCreateInstanceAsync(avatarId2, sagaRef);

        var boss1Id = Guid.Parse(instance1.GetCommittedTransactions()
            .First(t => t.Type == SagaTransactionType.CharacterSpawned).Data["CharacterInstanceId"]);
        var boss2Id = Guid.Parse(instance2.GetCommittedTransactions()
            .First(t => t.Type == SagaTransactionType.CharacterSpawned).Data["CharacterInstanceId"]);

        // ACT: Execute battles with SAME SEED
        // Note: StartBattleCommand uses Random.Shared by default
        // This test validates that seed is stored in BattleStarted transaction
        await _mediator.Send(new StartBattleCommand
        {
            AvatarId = avatarId1,
            SagaRef = sagaRef,
            CharacterInstanceId = boss1Id,
            Avatar = avatar1
        });

        await _mediator.Send(new StartBattleCommand
        {
            AvatarId = avatarId2,
            SagaRef = sagaRef,
            CharacterInstanceId = boss2Id,
            Avatar = avatar2
        });

        // ASSERT: Both battles should have identical turn counts and outcomes
        var final1 = await _repository.GetOrCreateInstanceAsync(avatarId1, sagaRef);
        var final2 = await _repository.GetOrCreateInstanceAsync(avatarId2, sagaRef);

        var battle1Turns = final1.GetCommittedTransactions()
            .Count(t => t.Type == SagaTransactionType.BattleTurnExecuted);
        var battle2Turns = final2.GetCommittedTransactions()
            .Count(t => t.Type == SagaTransactionType.BattleTurnExecuted);

        _output.WriteLine($"Battle 1 turns: {battle1Turns}");
        _output.WriteLine($"Battle 2 turns: {battle2Turns}");

        // Battles may differ due to different random seeds, but transaction structure should be identical
        Assert.Equal(
            final1.GetCommittedTransactions().Count(t => t.Type == SagaTransactionType.BattleStarted),
            final2.GetCommittedTransactions().Count(t => t.Type == SagaTransactionType.BattleStarted));
    }

    [Fact]
    public async Task ZoneExit_LivingEnemies_Despawned()
    {
        // ARRANGE: Spawn multiple enemies but DON'T defeat them
        var avatarId = Guid.NewGuid();
        var avatar = CreateWarriorAvatar(avatarId);

        var sagaRef = "MultiEnemySaga";

        // Enter zone (spawns 3 enemies)
        await _mediator.Send(new UpdateAvatarPositionCommand
        {
            AvatarId = avatarId,
            SagaRef = sagaRef,
            Latitude = 35.0,
            Longitude = 139.0,
            Y = 50.0,
            Avatar = avatar
        });

        var instance = await _repository.GetOrCreateInstanceAsync(avatarId, sagaRef);
        var spawnedEnemies = instance.GetCommittedTransactions()
            .Where(t => t.Type == SagaTransactionType.CharacterSpawned)
            .ToList();

        Assert.Equal(3, spawnedEnemies.Count);
        _output.WriteLine($"Spawned {spawnedEnemies.Count} living enemies");

        // ACT: Exit zone WITHOUT defeating enemies
        await _mediator.Send(new UpdateAvatarPositionCommand
        {
            AvatarId = avatarId,
            SagaRef = sagaRef,
            Latitude = 35.002, // Far away
            Longitude = 139.002,
            Y = 50.0,
            Avatar = avatar
        });

        // ASSERT: All living enemies despawned
        var finalInstance = await _repository.GetOrCreateInstanceAsync(avatarId, sagaRef);
        var despawns = finalInstance.GetCommittedTransactions()
            .Where(t => t.Type == SagaTransactionType.CharacterDespawned)
            .ToList();

        Assert.Equal(3, despawns.Count);

        foreach (var despawn in despawns)
        {
            Assert.Equal("Player exited trigger zone", despawn.Data["Reason"]);
            _output.WriteLine($"Living enemy despawned: {despawn.Data["CharacterRef"]}");
        }
    }

    [Fact]
    public async Task ZoneExit_DefeatedEnemy_CorpseRemains()
    {
        // ARRANGE: Defeat enemy
        var avatarId = Guid.NewGuid();
        var avatar = CreateWarriorAvatar(avatarId);

        var sagaRef = "WeakBossSaga";

        // Spawn and defeat boss
        await _mediator.Send(new UpdateAvatarPositionCommand
        {
            AvatarId = avatarId,
            SagaRef = sagaRef,
            Latitude = 35.0,
            Longitude = 139.0,
            Y = 50.0,
            Avatar = avatar
        });

        var instance = await _repository.GetOrCreateInstanceAsync(avatarId, sagaRef);
        var bossInstanceId = Guid.Parse(
            instance.GetCommittedTransactions()
                .First(t => t.Type == SagaTransactionType.CharacterSpawned)
                .Data["CharacterInstanceId"]);

        // Defeat boss
        await _mediator.Send(new StartBattleCommand
        {
            AvatarId = avatarId,
            SagaRef = sagaRef,
            CharacterInstanceId = bossInstanceId,
            Avatar = avatar
        });

        _output.WriteLine($"Boss defeated: {bossInstanceId}");

        // ACT: Exit zone with defeated enemy corpse
        await _mediator.Send(new UpdateAvatarPositionCommand
        {
            AvatarId = avatarId,
            SagaRef = sagaRef,
            Latitude = 35.002,
            Longitude = 139.002,
            Y = 50.0,
            Avatar = avatar
        });

        // ASSERT: Defeated enemy NOT despawned (corpse remains for looting)
        var finalInstance = await _repository.GetOrCreateInstanceAsync(avatarId, sagaRef);
        var despawns = finalInstance.GetCommittedTransactions()
            .Where(t => t.Type == SagaTransactionType.CharacterDespawned)
            .ToList();

        // Should have PlayerExited but NO despawn for defeated character
        Assert.Contains(finalInstance.GetCommittedTransactions(), t => t.Type == SagaTransactionType.PlayerExited);
        Assert.DoesNotContain(despawns, d => d.Data["CharacterInstanceId"] == bossInstanceId.ToString());

        _output.WriteLine("✓ Corpse remains for looting after zone exit");
    }

    [Fact]
    public async Task BattleTransactions_CompleteAuditTrail()
    {
        // ARRANGE: Setup battle
        var avatarId = Guid.NewGuid();
        var avatar = CreateWarriorAvatar(avatarId);

        var sagaRef = "WeakBossSaga";

        await _mediator.Send(new UpdateAvatarPositionCommand
        {
            AvatarId = avatarId,
            SagaRef = sagaRef,
            Latitude = 35.0,
            Longitude = 139.0,
            Y = 50.0,
            Avatar = avatar
        });

        var instance = await _repository.GetOrCreateInstanceAsync(avatarId, sagaRef);
        var bossInstanceId = Guid.Parse(
            instance.GetCommittedTransactions()
                .First(t => t.Type == SagaTransactionType.CharacterSpawned)
                .Data["CharacterInstanceId"]);

        // ACT: Execute battle
        await _mediator.Send(new StartBattleCommand
        {
            AvatarId = avatarId,
            SagaRef = sagaRef,
            CharacterInstanceId = bossInstanceId,
            Avatar = avatar
        });

        // ASSERT: Verify complete audit trail
        var finalInstance = await _repository.GetOrCreateInstanceAsync(avatarId, sagaRef);
        var battleTxs = finalInstance.GetCommittedTransactions()
            .Where(t => t.Type == SagaTransactionType.BattleStarted ||
                       t.Type == SagaTransactionType.BattleTurnExecuted ||
                       t.Type == SagaTransactionType.BattleEnded)
            .OrderBy(t => t.SequenceNumber)
            .ToList();

        var battleStarted = battleTxs.First(t => t.Type == SagaTransactionType.BattleStarted);
        var battleEnded = battleTxs.First(t => t.Type == SagaTransactionType.BattleEnded);
        var turns = battleTxs.Where(t => t.Type == SagaTransactionType.BattleTurnExecuted).ToList();

        // Verify BattleStarted has complete snapshot
        Assert.True(battleStarted.Data.ContainsKey("RandomSeed"));
        Assert.True(battleStarted.Data.ContainsKey("PlayerHealth"));
        Assert.True(battleStarted.Data.ContainsKey("PlayerStrength"));
        Assert.True(battleStarted.Data.ContainsKey("EnemyHealth"));

        _output.WriteLine($"Battle Started - Seed: {battleStarted.Data["RandomSeed"]}");
        _output.WriteLine($"  Player HP: {battleStarted.Data["PlayerHealth"]}");
        _output.WriteLine($"  Enemy HP: {battleStarted.Data["EnemyHealth"]}");

        // Verify each turn has required data
        foreach (var turn in turns)
        {
            Assert.True(turn.Data.ContainsKey("TurnNumber"));
            Assert.True(turn.Data.ContainsKey("Actor"));
            Assert.True(turn.Data.ContainsKey("DecisionType"));

            _output.WriteLine($"Turn {turn.Data["TurnNumber"]}: {turn.Data["Actor"]} - {turn.Data["DecisionType"]}");
        }

        // Verify BattleEnded links back to BattleStarted
        Assert.Equal(battleStarted.TransactionId.ToString(), battleEnded.Data["BattleStartedTransactionId"]);
        _output.WriteLine($"Battle Ended - Total Turns: {battleEnded.Data["TotalTurns"]}");
    }

    [Fact]
    public async Task MultipleEnemies_SequentialBattles_AllDefeated()
    {
        // ARRANGE: Spawn 3 enemies
        var avatarId = Guid.NewGuid();
        var avatar = CreateWarriorAvatar(avatarId);

        var sagaRef = "MultiEnemySaga";

        await _mediator.Send(new UpdateAvatarPositionCommand
        {
            AvatarId = avatarId,
            SagaRef = sagaRef,
            Latitude = 35.0,
            Longitude = 139.0,
            Y = 50.0,
            Avatar = avatar
        });

        var instance = await _repository.GetOrCreateInstanceAsync(avatarId, sagaRef);
        var enemyIds = instance.GetCommittedTransactions()
            .Where(t => t.Type == SagaTransactionType.CharacterSpawned)
            .Select(t => Guid.Parse(t.Data["CharacterInstanceId"]))
            .ToList();

        Assert.Equal(3, enemyIds.Count);

        // ACT: Defeat each enemy sequentially
        foreach (var enemyId in enemyIds)
        {
            var battleResult = await _mediator.Send(new StartBattleCommand
            {
                AvatarId = avatarId,
                SagaRef = sagaRef,
                CharacterInstanceId = enemyId,
                Avatar = avatar
            });

            Assert.True(battleResult.Success);
            _output.WriteLine($"Defeated enemy: {enemyId}");
        }

        // ASSERT: All 3 enemies defeated
        var finalInstance = await _repository.GetOrCreateInstanceAsync(avatarId, sagaRef);
        var defeats = finalInstance.GetCommittedTransactions()
            .Where(t => t.Type == SagaTransactionType.CharacterDefeated)
            .ToList();

        Assert.Equal(3, defeats.Count);
        _output.WriteLine($"Total enemies defeated: {defeats.Count}");
    }
    */

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
            LatitudeZ = 35.0,
            LongitudeX = 139.0,
            Y = 50.0
        };

        var multiEnemySaga = new SagaArc
        {
            RefName = "MultiEnemySaga",
            DisplayName = "Multi-Enemy Arena",
            LatitudeZ = 35.0,
            LongitudeX = 139.0,
            Y = 50.0
        };

        var weakBossTrigger = new SagaTrigger
        {
            RefName = "WeakBossTrigger",
            EnterRadius = 100.0f,
            TriggerType = SagaTriggerType.SpawnPassive,
            Spawn = new[]
            {
                new CharacterSpawn
                {
                    ItemElementName = ItemChoiceType.CharacterRef,
                    Item = "WeakBoss"
                }
            }
        };

        var multiEnemyTrigger = new SagaTrigger
        {
            RefName = "MultiEnemyTrigger",
            EnterRadius = 100.0f,
            TriggerType = SagaTriggerType.SpawnPassive,
            Spawn = new[]
            {
                new CharacterSpawn { ItemElementName = ItemChoiceType.CharacterRef, Item = "Goblin" },
                new CharacterSpawn { ItemElementName = ItemChoiceType.CharacterRef, Item = "Goblin" },
                new CharacterSpawn { ItemElementName = ItemChoiceType.CharacterRef, Item = "Goblin" }
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
            SlotRef = "RightHand"
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
                QuestTokens = Array.Empty<QuestTokenEntry>()
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
                    CharacterArchetypes = Array.Empty<CharacterArchetype>(),
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
                QuestTokens = Array.Empty<QuestTokenEntry>()
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
