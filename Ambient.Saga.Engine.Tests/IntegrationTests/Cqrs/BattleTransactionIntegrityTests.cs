using Ambient.Domain;
using Ambient.Domain.Contracts;
using Ambient.Domain.Partials;
using Ambient.Domain.Entities;
using Ambient.Saga.Engine.Application.Behaviors;
using Ambient.Saga.Engine.Application.Commands.Saga;
using Ambient.Saga.Engine.Application.Handlers.Saga;
using Ambient.Saga.Engine.Application.ReadModels;
using Ambient.Saga.Engine.Contracts;
using Ambient.Saga.Engine.Contracts.Cqrs;
using Ambient.Saga.Engine.Contracts.Persistence;
using Ambient.Saga.Engine.Contracts.Services;
using Ambient.Saga.Engine.Domain;
using Ambient.Saga.Engine.Domain.Rpg.Battle;
using Ambient.Saga.Engine.Domain.Rpg.Sagas.TransactionLog;
using Ambient.Saga.Engine.Infrastructure.Persistence;
using Ambient.Saga.Engine.Tests.Helpers;
using LiteDB;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace Ambient.Saga.Engine.Tests.IntegrationTests.Cqrs;

/// <summary>
/// Regression tests for round-3 audit findings A1/A4/A5 at the CQRS seam:
/// - every persisted battle turn carries absolute post-turn health/energy for BOTH
///   combatants (no target/turn-parity inference),
/// - state reconstruction prefers those absolute snapshots over the legacy
///   Target/TargetHealthAfter fields,
/// - fleeing (or otherwise ending) a battle must not permanently block re-engaging
///   the same character (StartBattle used to return the ended battle's id).
/// </summary>
[Collection("Sequential CQRS Tests")]
public class BattleTransactionIntegrityTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly ServiceProvider _serviceProvider;
    private readonly IMediator _mediator;
    private readonly IWorld _world;
    private readonly LiteDatabase _database;
    private readonly ISagaInstanceRepository _repository;

    public BattleTransactionIntegrityTests(ITestOutputHelper output)
    {
        _output = output;
        _database = new LiteDatabase(new MemoryStream());
        _world = CreateTestWorld();

        var services = new ServiceCollection();
        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssemblyContaining<StartBattleCommand>();
            cfg.AddOpenBehavior(typeof(SagaLoggingBehavior<,>));
            cfg.AddOpenBehavior(typeof(SagaValidationBehavior<,>));
            cfg.AddOpenBehavior(typeof(AchievementEvaluationBehavior<,>));
        });

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
    public async Task EveryPersistedTurn_CarriesAbsoluteHealthAndEnergySnapshots()
    {
        var (avatarId, sagaRef, battleId, avatar) = await StartTestBattle();

        var turnResult = await _mediator.Send(new ExecuteBattleTurnCommand
        {
            AvatarId = avatarId,
            SagaArcRef = sagaRef,
            BattleInstanceId = battleId,
            AvatarAction = new CombatAction { ActionType = ActionType.Attack },
            Avatar = avatar
        });
        Assert.True(turnResult.Successful, $"Turn failed: {turnResult.ErrorMessage}");

        var turnTxs = (await _repository.GetOrCreateInstanceAsync(avatarId, sagaRef))
            .GetCommittedTransactions()
            .Where(t => t.Type == SagaTransactionType.BattleTurnExecuted &&
                        t.Data[TransactionDataKeys.BattleTransactionId] == battleId.ToString())
            .ToList();

        Assert.NotEmpty(turnTxs);
        foreach (var tx in turnTxs)
        {
            Assert.True(tx.Data.ContainsKey(TransactionDataKeys.AvatarHealthAfterTurn),
                $"Turn {tx.Data[TransactionDataKeys.TurnNumber]} is missing AvatarHealthAfterTurn");
            Assert.True(tx.Data.ContainsKey(TransactionDataKeys.EnemyHealthAfterTurn),
                $"Turn {tx.Data[TransactionDataKeys.TurnNumber]} is missing EnemyHealthAfterTurn");

            Assert.True(BattleTransactionHelper.TryParseFloat(
                tx.Data[TransactionDataKeys.AvatarHealthAfterTurn], out var avatarHealth));
            Assert.True(BattleTransactionHelper.TryParseFloat(
                tx.Data[TransactionDataKeys.EnemyHealthAfterTurn], out _));

            // The avatar fought one turn against a weak enemy — it must not be dead
            Assert.True(avatarHealth > 0f, "Avatar health snapshot must reflect the live avatar");
        }
    }

    [Fact]
    public void Reconstruction_PrefersAbsoluteSnapshots_OverGarbageLegacyFields()
    {
        // Simulate the old corruption vector: a persisted turn whose legacy fields are
        // garbage (empty Target, TargetHealthAfter=0 — a pre-fix stunned turn) but whose
        // absolute snapshots carry the real state. Reconstruction must use the absolutes.
        var avatarState = new Combatant
        {
            RefName = "AvatarRef", DisplayName = "Avatar",
            Health = 0.9f, Stamina = 0.8f, Strength = 0.1f, Defense = 0.1f, Speed = 0.1f, Magic = 0.1f,
            CombatProfile = new Dictionary<string, string>()
        };
        var enemyState = new Combatant
        {
            RefName = "WeakBoss", DisplayName = "Weak Training Dummy",
            Health = 0.7f, Stamina = 0.6f, Strength = 0.05f, Defense = 0.05f, Speed = 0.1f, Magic = 0.1f,
            CombatProfile = new Dictionary<string, string>()
        };

        var instanceId = Guid.NewGuid();
        var enemyInstanceId = Guid.NewGuid();
        var battleStartedTx = BattleTransactionHelper.CreateBattleStartedTransaction(
            Guid.NewGuid().ToString(), "TestSaga", Guid.NewGuid(), enemyInstanceId,
            enemyState.RefName, 42, avatarState, enemyState, new List<string>(), instanceId);

        // A "stunned enemy turn" transaction: garbage legacy fields, real absolute snapshots
        var stunnedTurnTx = BattleTransactionHelper.CreateBattleTurnExecutedTransaction(
            Guid.NewGuid().ToString(), battleStartedTx.TransactionId, turnNumber: 1,
            actorRefName: enemyState.DisplayName, isAvatarTurn: false,
            decisionType: ActionType.Attack, itemRefName: null,
            damageDealt: 0f, healingDone: 0f,
            targetRefName: "",        // legacy garbage: un-stamped events had no target
            targetHealthAfter: 0f,    // legacy garbage: would zero the avatar via parity fallback
            actorEnergyAfter: 0f,
            actorAfterAction: enemyState, world: null!, sagaInstanceId: instanceId,
            avatarState: avatarState, enemyState: enemyState);

        var instance = new SagaInstance { InstanceId = instanceId, SagaRef = "TestSaga" };
        instance.AddTransaction(battleStartedTx);
        instance.AddTransaction(stunnedTurnTx);

        var (avatar, enemy, _, _, _, _) = BattleStateReconstructor.Reconstruct(battleStartedTx, instance, CreateTestWorld());

        // Before the fix: avatar.Health == 0 (parity fallback wrote TargetHealthAfter=0)
        Assert.Equal(0.9f, avatar.Health, 3);
        Assert.Equal(0.8f, avatar.Stamina, 3);
        Assert.Equal(0.7f, enemy.Health, 3);
        Assert.Equal(0.6f, enemy.Stamina, 3);
    }

    [Fact]
    public async Task EndedBattle_DoesNotBlockReengagement()
    {
        var (avatarId, sagaRef, firstBattleId, avatar) = await StartTestBattle();

        // Fight until the battle ends (weak enemy — the avatar wins in a few turns)
        var ended = false;
        for (var i = 0; i < 30 && !ended; i++)
        {
            var result = await _mediator.Send(new ExecuteBattleTurnCommand
            {
                AvatarId = avatarId,
                SagaArcRef = sagaRef,
                BattleInstanceId = firstBattleId,
                AvatarAction = new CombatAction { ActionType = ActionType.Attack },
                Avatar = avatar
            });
            Assert.True(result.Successful, $"Turn {i} failed: {result.ErrorMessage}");

            ended = (await _repository.GetOrCreateInstanceAsync(avatarId, sagaRef))
                .GetCommittedTransactions()
                .Any(t => t.Type == SagaTransactionType.BattleEnded &&
                          t.Data[TransactionDataKeys.BattleTransactionId] == firstBattleId.ToString());
        }
        Assert.True(ended, "Battle against the weak enemy should have ended");

        // Re-engaging the same character must start a NEW battle, not return the ended one
        var secondBattle = await StartBattleForExistingCharacter(avatarId, sagaRef, avatar);
        Assert.True(secondBattle.Successful, $"Re-engagement failed: {secondBattle.ErrorMessage}");

        var battleStartedTxs = (await _repository.GetOrCreateInstanceAsync(avatarId, sagaRef))
            .GetCommittedTransactions()
            .Where(t => t.Type == SagaTransactionType.BattleStarted)
            .ToList();
        Assert.Equal(2, battleStartedTxs.Count);

        var newBattleId = battleStartedTxs.OrderBy(t => t.SequenceNumber).Last().TransactionId;
        Assert.NotEqual(firstBattleId, newBattleId);

        // And the new battle must actually be playable
        var newTurn = await _mediator.Send(new ExecuteBattleTurnCommand
        {
            AvatarId = avatarId,
            SagaArcRef = sagaRef,
            BattleInstanceId = newBattleId,
            AvatarAction = new CombatAction { ActionType = ActionType.Attack },
            Avatar = avatar
        });
        Assert.True(newTurn.Successful, $"Turn in re-engaged battle failed: {newTurn.ErrorMessage}");
    }

    [Fact]
    public async Task Companions_SurviveCommandBoundaries_AndTheirDamageDoesNotFoldIntoAvatar()
    {
        // Regression for A2: companions were only alive for StartBattle's opening turn —
        // every ExecuteBattleTurn rebuilt the engine without them, so they never acted
        // again, and enemy damage dealt to a companion folded into the AVATAR's health.
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
        var battleSetup = new BattleSetup();
        battleSetup.SetupFromWorld(_world);
        battleSetup.SelectedAvatarArchetype = _world.AvatarArchetypesLookup["Warrior"];
        battleSetup.AvatarCapabilities = avatar.Capabilities ?? new ItemCollection();
        battleSetup.AvatarAffinityRefs = new List<string> { "Physical" };
        battleSetup.SelectedOpponentCharacter = weakBoss;
        battleSetup.OpponentCapabilities = weakBoss.Capabilities ?? new ItemCollection();
        var battleEngine = battleSetup.CreateBattleEngine();

        var companion = new Combatant
        {
            RefName = "CompanionBob",
            DisplayName = "Bob",
            Health = 0.9f,
            Stamina = 1.0f,
            Strength = 0.10f,
            Defense = 0.10f,
            Speed = 0.10f,
            Magic = 0.05f,
            CombatProfile = new Dictionary<string, string>()
        };

        var startResult = await _mediator.Send(new StartBattleCommand
        {
            AvatarId = avatarId,
            SagaArcRef = sagaRef,
            EnemyCharacterInstanceId = bossInstanceId,
            AvatarCombatant = battleEngine.GetAvatar(),
            EnemyCombatant = battleEngine.GetEnemy(),
            CompanionCombatants = new List<Combatant> { companion },
            AvatarAffinityRefs = new List<string> { "Physical" },
            EnemyMind = new CombatAI(_world, 777),
            RandomSeed = 777,
            Avatar = avatar
        });
        Assert.True(startResult.Successful, $"Battle start failed: {startResult.ErrorMessage}");

        var battleId = (await _repository.GetOrCreateInstanceAsync(avatarId, sagaRef))
            .GetCommittedTransactions()
            .Where(t => t.Type == SagaTransactionType.BattleStarted)
            .OrderBy(t => t.SequenceNumber)
            .Last();

        // The roster must be recorded for reconstruction
        Assert.True(battleId.Data.ContainsKey(TransactionDataKeys.Companions),
            "BattleStarted must record the companion roster");

        // One command turn — the companion must ACT (a companion turn transaction exists)
        var turnResult = await _mediator.Send(new ExecuteBattleTurnCommand
        {
            AvatarId = avatarId,
            SagaArcRef = sagaRef,
            BattleInstanceId = battleId.TransactionId,
            AvatarAction = new CombatAction { ActionType = ActionType.Defend },
            Avatar = avatar
        });
        Assert.True(turnResult.Successful, $"Turn failed: {turnResult.ErrorMessage}");

        var turnTxs = (await _repository.GetOrCreateInstanceAsync(avatarId, sagaRef))
            .GetCommittedTransactions()
            .Where(t => t.Type == SagaTransactionType.BattleTurnExecuted &&
                        t.Data[TransactionDataKeys.BattleTransactionId] == battleId.TransactionId.ToString())
            .OrderBy(t => t.SequenceNumber)
            .ToList();

        Assert.Contains(turnTxs, t =>
            t.Data.TryGetValue(TransactionDataKeys.Actor, out var actor) && actor == "Bob");

        // And the state query must reconstruct the companion, not drop it
        var state = await _mediator.Send(new Application.Queries.Saga.GetBattleStateQuery
        {
            AvatarId = avatarId,
            SagaRef = sagaRef,
            BattleInstanceId = battleId.TransactionId,
            Avatar = avatar
        });
        var reconstructedCompanion = Assert.Single(state.Companions);
        Assert.Equal("CompanionBob", reconstructedCompanion.RefName);

        // If the enemy hit Bob on any turn, the avatar's absolute health snapshot must
        // still be the avatar's own (Defend all battle: avatar takes at most enemy hits
        // aimed at IT; Bob's health is tracked separately)
        var lastTx = turnTxs.Last();
        Assert.True(BattleTransactionHelper.TryParseFloat(
            lastTx.Data[TransactionDataKeys.AvatarHealthAfterTurn], out var avatarHealth));
        Assert.Equal(state.AvatarCombatant!.Health, avatarHealth, 3);
    }

    [Fact]
    public async Task ConsecutiveTurns_DoNotRepeatIdenticalRolls()
    {
        // Regression for A6: a fresh Random(battleSeed) per command replayed the same
        // roll sequence every turn — every attack dealt bitwise-identical damage.
        var (avatarId, sagaRef, battleId, avatar) = await StartTestBattle();

        var damages = new List<float>();
        for (var i = 0; i < 4; i++)
        {
            var result = await _mediator.Send(new ExecuteBattleTurnCommand
            {
                AvatarId = avatarId,
                SagaArcRef = sagaRef,
                BattleInstanceId = battleId,
                AvatarAction = new CombatAction { ActionType = ActionType.Attack },
                Avatar = avatar
            });
            if (!result.Successful) break; // battle may end early — fine

            var instance = await _repository.GetOrCreateInstanceAsync(avatarId, sagaRef);
            if (instance.GetCommittedTransactions().Any(t =>
                    t.Type == SagaTransactionType.BattleEnded &&
                    t.Data[TransactionDataKeys.BattleTransactionId] == battleId.ToString()))
                break;
        }

        var avatarTurnDamages = (await _repository.GetOrCreateInstanceAsync(avatarId, sagaRef))
            .GetCommittedTransactions()
            .Where(t => t.Type == SagaTransactionType.BattleTurnExecuted &&
                        t.Data[TransactionDataKeys.BattleTransactionId] == battleId.ToString() &&
                        bool.Parse(t.Data[TransactionDataKeys.IsAvatarTurn]))
            .Select(t => t.Data[TransactionDataKeys.DamageDealt])
            .ToList();
        damages.AddRange(avatarTurnDamages.Select(d =>
        {
            BattleTransactionHelper.TryParseFloat(d, out var v);
            return v;
        }));

        var nonZero = damages.Where(d => d > 0f).ToList();
        Assert.True(nonZero.Count >= 2, $"Need at least two landed hits to compare (got {nonZero.Count})");
        Assert.True(nonZero.Distinct().Count() >= 2,
            $"All {nonZero.Count} hits dealt identical damage ({nonZero[0]}) — per-turn RNG derivation is broken");
    }

    [Fact]
    public async Task Flee_RecordsFledOutcome_AndReengagementCarriesEnemyDamage()
    {
        // Regression for A5: fleeing was recorded as a defeat (defeat dialogue fired,
        // state query showed Defeat) and the ended battle blocked all re-engagement.
        var (avatarId, sagaRef, battleId, avatar) = await StartTestBattle();

        // Damage the enemy once so re-engagement has damage to carry
        var attack = await _mediator.Send(new ExecuteBattleTurnCommand
        {
            AvatarId = avatarId,
            SagaArcRef = sagaRef,
            BattleInstanceId = battleId,
            AvatarAction = new CombatAction { ActionType = ActionType.Attack },
            Avatar = avatar
        });
        Assert.True(attack.Successful, $"Attack failed: {attack.ErrorMessage}");

        // Flee until it sticks (flee is a roll; each turn uses a different derived seed)
        SagaTransaction? battleEndedTx = null;
        for (var i = 0; i < 20 && battleEndedTx == null; i++)
        {
            var flee = await _mediator.Send(new ExecuteBattleTurnCommand
            {
                AvatarId = avatarId,
                SagaArcRef = sagaRef,
                BattleInstanceId = battleId,
                AvatarAction = new CombatAction { ActionType = ActionType.Flee },
                Avatar = avatar
            });
            Assert.True(flee.Successful, $"Flee attempt {i} failed: {flee.ErrorMessage}");

            battleEndedTx = (await _repository.GetOrCreateInstanceAsync(avatarId, sagaRef))
                .GetCommittedTransactions()
                .FirstOrDefault(t => t.Type == SagaTransactionType.BattleEnded &&
                                     t.Data[TransactionDataKeys.BattleTransactionId] == battleId.ToString());
        }
        Assert.NotNull(battleEndedTx);

        // The end transaction must record the distinct Fled outcome, not a defeat
        Assert.True(battleEndedTx!.Data.TryGetValue(TransactionDataKeys.BattleOutcome, out var outcome));
        Assert.Equal(nameof(BattleState.Fled), outcome);

        // No CharacterDefeated may exist (the enemy survived; the avatar didn't "lose")
        var instanceAfterFlee = await _repository.GetOrCreateInstanceAsync(avatarId, sagaRef);
        Assert.DoesNotContain(instanceAfterFlee.GetCommittedTransactions(),
            t => t.Type == SagaTransactionType.CharacterDefeated);

        // The state query must report Fled — not Defeat
        var state = await _mediator.Send(new Application.Queries.Saga.GetBattleStateQuery
        {
            AvatarId = avatarId,
            SagaRef = sagaRef,
            BattleInstanceId = battleId,
            Avatar = avatar
        });
        Assert.True(state.HasEnded);
        Assert.Equal(BattleState.Fled, state.BattleState);
        Assert.Null(state.AvatarVictory);

        // Record what the enemy's health was when we fled
        var enemyHealthAtFlee = instanceAfterFlee.GetCommittedTransactions()
            .Where(t => t.Type == SagaTransactionType.BattleTurnExecuted &&
                        t.Data[TransactionDataKeys.BattleTransactionId] == battleId.ToString() &&
                        t.Data.ContainsKey(TransactionDataKeys.EnemyHealthAfterTurn))
            .OrderBy(t => t.SequenceNumber)
            .Last().Data[TransactionDataKeys.EnemyHealthAfterTurn];
        Assert.True(BattleTransactionHelper.TryParseFloat(enemyHealthAtFlee, out var expectedEnemyHealth));
        Assert.True(expectedEnemyHealth > 0f);

        // Re-engage: must start a NEW battle whose enemy still carries the damage
        var secondBattle = await StartBattleForExistingCharacter(avatarId, sagaRef, avatar);
        Assert.True(secondBattle.Successful, $"Re-engagement after fleeing failed: {secondBattle.ErrorMessage}");

        var newBattleStarted = (await _repository.GetOrCreateInstanceAsync(avatarId, sagaRef))
            .GetCommittedTransactions()
            .Where(t => t.Type == SagaTransactionType.BattleStarted)
            .OrderBy(t => t.SequenceNumber)
            .Last();
        Assert.NotEqual(battleId, newBattleStarted.TransactionId);

        Assert.True(BattleTransactionHelper.TryParseFloat(
            newBattleStarted.Data[TransactionDataKeys.EnemyHealth], out var newBattleEnemyHealth));
        Assert.Equal(expectedEnemyHealth, newBattleEnemyHealth, 3);
    }

    public void Dispose()
    {
        _database?.Dispose();
        _serviceProvider?.Dispose();
    }

    #region Setup helpers

    private async Task<(Guid avatarId, string sagaRef, Guid battleId, AvatarEntity avatar)> StartTestBattle()
    {
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

        var startResult = await StartBattleForExistingCharacter(avatarId, sagaRef, avatar);
        Assert.True(startResult.Successful, $"Battle start failed: {startResult.ErrorMessage}");

        var battleId = (await _repository.GetOrCreateInstanceAsync(avatarId, sagaRef))
            .GetCommittedTransactions()
            .Where(t => t.Type == SagaTransactionType.BattleStarted)
            .OrderBy(t => t.SequenceNumber)
            .Last()
            .TransactionId;

        return (avatarId, sagaRef, battleId, avatar);
    }

    private async Task<Application.Results.Saga.SagaCommandResult> StartBattleForExistingCharacter(
        Guid avatarId, string sagaRef, AvatarEntity avatar)
    {
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

        return await _mediator.Send(new StartBattleCommand
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
    }

    private World CreateTestWorld()
    {
        var weakBossSaga = new SagaArc
        {
            RefName = "WeakBossSaga",
            DisplayName = "Weak Boss Arena",
            Latitude = 35.0,
            Longitude = 139.0
        };

        var weakBossTrigger = new SagaTrigger
        {
            RefName = "WeakBossTrigger",
            EnterRadius = 100.0f,
            Spawn = new[] { new CharacterSpawn { CharacterRef = "WeakBoss" } }
        };

        var weakBoss = new Character
        {
            RefName = "WeakBoss",
            DisplayName = "Weak Training Dummy",
            Stats = new CharacterStats
            {
                Health = 0.3f,
                Strength = 0.05f,
                Defense = 0.0f
            }
        };

        var ironSword = new Equipment
        {
            RefName = "IronSword",
            DisplayName = "Iron Sword",
            WholesalePrice = 50,
            SlotRef = "MainHand"
        };

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

        var physicalAffinity = new CharacterAffinity
        {
            RefName = "Physical",
            DisplayName = "Physical",
            Description = "Physical combat affinity"
        };

        var world = new World
        {
            IsProcedural = true,
            WorldConfiguration = new WorldConfiguration
            {
                RefName = "BattleIntegrityTestWorld",
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
                    SagaArcs = new[] { weakBossSaga },
                    Characters = new[] { weakBoss },
                    Equipment = new[] { ironSword },
                    AvatarArchetypes = new[] { warriorArchetype },
                    Achievements = Array.Empty<Achievement>(),
                    CharacterAffinities = new[] { physicalAffinity },
                    DialogueTrees = Array.Empty<DialogueTree>(),
                    Consumables = Array.Empty<Consumable>()
                }
            }
        };

        world.SagaArcLookup[weakBossSaga.RefName] = weakBossSaga;
        world.SagaTriggersLookup[weakBossSaga.RefName] = new List<SagaTrigger> { weakBossTrigger };
        world.CharactersLookup[weakBoss.RefName] = weakBoss;
        world.EquipmentLookup[ironSword.RefName] = ironSword;
        world.AvatarArchetypesLookup[warriorArchetype.RefName] = warriorArchetype;
        world.CharacterAffinitiesLookup["Physical"] = physicalAffinity;

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
                Strength = 0.20f,
                Defense = 0.15f,
                Speed = 0.12f,
                Magic = 0.10f
            },
            Capabilities = new ItemCollection
            {
                Equipment = new[] { new EquipmentEntry { EquipmentRef = "IronSword", Condition = 1.0f } },
                Spells = Array.Empty<SpellEntry>(),
            },
            AffinityRef = "Physical"
        };
    }

    #endregion
}
