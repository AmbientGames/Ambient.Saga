using Ambient.Domain;
using Ambient.Domain.Contracts;
using Ambient.Domain.Partials;
using Ambient.Domain.Entities;
using Ambient.Rpg.Engine.Application.Behaviors;
using Ambient.Rpg.Engine.Application.Commands.Arcs;
using Ambient.Rpg.Engine.Application.Handlers.Arcs;
using Ambient.Rpg.Engine.Application.ReadModels;
using Ambient.Rpg.Engine.Contracts;
using Ambient.Rpg.Engine.Contracts.Cqrs;
using Ambient.Rpg.Engine.Contracts.Persistence;
using Ambient.Rpg.Engine.Contracts.Services;
using Ambient.Rpg.Engine.Tests.Helpers;
using Ambient.Rpg.Engine.Domain;
using Ambient.Rpg.Engine.Domain.Battle;
using Ambient.Rpg.Engine.Domain.Arcs.TransactionLog;
using Ambient.Rpg.Engine.Infrastructure.Persistence;
using LiteDB;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace Ambient.Rpg.Engine.Tests.IntegrationTests.Cqrs;

/// <summary>
/// Integration tests for SubmitReactionCommand - the active defense system.
///
/// Round-3 contract: the telegraphed enemy attack (tell) is PERSISTED as a turn
/// transaction, and SubmitReaction resolves it SERVER-side — the client-computed
/// damage numbers on the command are ignored. Reacting with no pending tell fails.
/// </summary>
[Collection("Sequential CQRS Tests")]
public class SubmitReactionCommandTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly ServiceProvider _serviceProvider;
    private readonly IMediator _mediator;
    private readonly IWorld _world;
    private readonly LiteDatabase _database;
    private readonly IArcInstanceRepository _repository;

    public SubmitReactionCommandTests(ITestOutputHelper output)
    {
        _output = output;
        _database = new LiteDatabase(new MemoryStream());
        _world = CreateTestWorld();

        var services = new ServiceCollection();

        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssemblyContaining<SubmitReactionCommand>();
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

    [Fact]
    public async Task StartBattle_WithTells_PersistsTellTransaction_AndReactionResolvesIt()
    {
        var (avatarId, arcRef, battleId, avatar) = await StartBattleAgainst("TestEnemy");

        // The enemy's opening telegraph must be persisted (it used to live only in the
        // discarded engine instance — quitting mid-tell made the attack evaporate)
        var instance = await _repository.GetOrCreateInstanceAsync(avatarId, arcRef);
        var tellTx = ReactionTransactionFactory.FindUnresolvedTell(instance, battleId);
        Assert.NotNull(tellTx);
        Assert.True(BattleTransactionHelper.TryParseFloat(
            tellTx!.Data[TransactionDataKeys.BaseDamage], out var baseDamage));
        Assert.True(baseDamage > 0f, "Telegraphed attack must carry its base damage");

        // React — the server resolves the outcome
        var reactionResult = await _mediator.Send(new SubmitReactionCommand
        {
            AvatarId = avatarId,
            ArcRef = arcRef,
            BattleInstanceId = battleId,
            Reaction = AvatarDefenseType.Parry,
            Avatar = avatar
        });
        Assert.True(reactionResult.Successful, $"Reaction failed: {reactionResult.ErrorMessage}");

        var finalInstance = await _repository.GetOrCreateInstanceAsync(avatarId, arcRef);
        var reactionTx = finalInstance.GetCommittedTransactions()
            .Single(t => t.Type == ArcTransactionType.BattleTurnExecuted &&
                         t.Data.TryGetValue(TransactionDataKeys.ActionType, out var action) &&
                         action == "Reaction");

        Assert.Equal("Parry", reactionTx.Data[TransactionDataKeys.ReactionType]);
        Assert.Equal(battleId.ToString(), reactionTx.Data[TransactionDataKeys.BattleTransactionId]);

        // Server-computed damage: parry outcome is authored with DamageMultiplier 0.3
        Assert.True(BattleTransactionHelper.TryParseFloat(
            reactionTx.Data[TransactionDataKeys.DamageDealt], out var finalDamage));
        Assert.Equal(baseDamage * 0.3f, finalDamage, 3);

        // The tell is resolved — no pending tell remains
        Assert.Null(ReactionTransactionFactory.FindUnresolvedTell(finalInstance, battleId));
    }

    [Fact]
    public async Task SubmitReaction_IgnoresClientSuppliedNumbers()
    {
        var (avatarId, arcRef, battleId, avatar) = await StartBattleAgainst("TestEnemy");

        var instance = await _repository.GetOrCreateInstanceAsync(avatarId, arcRef);
        var tellTx = ReactionTransactionFactory.FindUnresolvedTell(instance, battleId);
        Assert.NotNull(tellTx);
        BattleTransactionHelper.TryParseFloat(tellTx!.Data[TransactionDataKeys.BaseDamage], out var baseDamage);

        // A cheating client claims the attack dealt nothing and the enemy is nearly dead
        var reactionResult = await _mediator.Send(new SubmitReactionCommand
        {
            AvatarId = avatarId,
            ArcRef = arcRef,
            BattleInstanceId = battleId,
            Reaction = AvatarDefenseType.Brace,
            Avatar = avatar,
            FinalDamage = 0f,          // lie
            EnemyHealthAfter = 0.001f, // lie
            CounterDamage = 0.9f,      // lie — brace has no counter
            StaminaGained = 1f         // lie
        });
        Assert.True(reactionResult.Successful, $"Reaction failed: {reactionResult.ErrorMessage}");

        var finalInstance = await _repository.GetOrCreateInstanceAsync(avatarId, arcRef);
        var reactionTx = finalInstance.GetCommittedTransactions()
            .Single(t => t.Type == ArcTransactionType.BattleTurnExecuted &&
                         t.Data.TryGetValue(TransactionDataKeys.ActionType, out var action) &&
                         action == "Reaction");

        // Brace is authored with DamageMultiplier 0.6 and no counter — server values win
        BattleTransactionHelper.TryParseFloat(reactionTx.Data[TransactionDataKeys.DamageDealt], out var damage);
        Assert.Equal(baseDamage * 0.6f, damage, 3);
        BattleTransactionHelper.TryParseFloat(reactionTx.Data[TransactionDataKeys.CounterDamage], out var counter);
        Assert.Equal(0f, counter, 3);

        // Enemy health snapshot must be untouched by the client's lie (no counter happened)
        BattleTransactionHelper.TryParseFloat(reactionTx.Data[TransactionDataKeys.EnemyHealthAfterTurn], out var enemyHealth);
        Assert.True(enemyHealth > 0.4f, $"Enemy health should be near its starting 0.5, was {enemyHealth}");
    }

    [Theory]
    [InlineData(AvatarDefenseType.Dodge)]
    [InlineData(AvatarDefenseType.Block)]
    [InlineData(AvatarDefenseType.Parry)]
    [InlineData(AvatarDefenseType.Brace)]
    [InlineData(AvatarDefenseType.None)]
    public async Task SubmitReaction_AllDefenseTypes_ResolvedAndRecorded(AvatarDefenseType defenseType)
    {
        var (avatarId, arcRef, battleId, avatar) = await StartBattleAgainst("TestEnemy");

        var reactionResult = await _mediator.Send(new SubmitReactionCommand
        {
            AvatarId = avatarId,
            ArcRef = arcRef,
            BattleInstanceId = battleId,
            Reaction = defenseType,
            Avatar = avatar
        });
        Assert.True(reactionResult.Successful, $"Reaction failed: {reactionResult.ErrorMessage}");

        var finalInstance = await _repository.GetOrCreateInstanceAsync(avatarId, arcRef);
        var reactionTx = finalInstance.GetCommittedTransactions()
            .Single(t => t.Type == ArcTransactionType.BattleTurnExecuted &&
                        t.Data.TryGetValue(TransactionDataKeys.ActionType, out var action) &&
                        action == "Reaction");

        Assert.Equal(defenseType.ToString(), reactionTx.Data[TransactionDataKeys.ReactionType]);
        _output.WriteLine($"Defense type {defenseType} resolved server-side");
    }

    [Fact]
    public async Task SubmitReaction_FailsForNonexistentBattle()
    {
        var avatarId = Guid.NewGuid();
        var avatar = CreateTestAvatar(avatarId);
        var arcRef = "TestArc";
        var fakeBattleId = Guid.NewGuid();

        await _repository.GetOrCreateInstanceAsync(avatarId, arcRef);

        var result = await _mediator.Send(new SubmitReactionCommand
        {
            AvatarId = avatarId,
            ArcRef = arcRef,
            BattleInstanceId = fakeBattleId,
            Reaction = AvatarDefenseType.Dodge,
            Avatar = avatar
        });

        Assert.False(result.Successful);
        Assert.Contains("not found", result.ErrorMessage);
    }

    [Fact]
    public async Task SubmitReaction_FailsWhenNoPendingTell()
    {
        var (avatarId, arcRef, battleId, avatar) = await StartBattleAgainst("TestEnemy");

        // Resolve the opening tell legitimately
        var first = await _mediator.Send(new SubmitReactionCommand
        {
            AvatarId = avatarId,
            ArcRef = arcRef,
            BattleInstanceId = battleId,
            Reaction = AvatarDefenseType.Dodge,
            Avatar = avatar
        });
        Assert.True(first.Successful);

        // A second reaction with nothing pending must be rejected — replaying reactions
        // used to inject arbitrary client-authored turns into the log
        var second = await _mediator.Send(new SubmitReactionCommand
        {
            AvatarId = avatarId,
            ArcRef = arcRef,
            BattleInstanceId = battleId,
            Reaction = AvatarDefenseType.Block,
            Avatar = avatar
        });

        Assert.False(second.Successful);
        Assert.Contains("No pending enemy attack", second.ErrorMessage);
    }

    [Fact]
    public async Task ReactionCounterKill_EndsBattle_WithVictoryAndDefeatedCharacter()
    {
        // The parry counter (multiplier 50 on base damage) is guaranteed lethal
        var (avatarId, arcRef, battleId, avatar) = await StartBattleAgainst("FragileEnemy");

        var reactionResult = await _mediator.Send(new SubmitReactionCommand
        {
            AvatarId = avatarId,
            ArcRef = arcRef,
            BattleInstanceId = battleId,
            Reaction = AvatarDefenseType.Parry,
            Avatar = avatar
        });
        Assert.True(reactionResult.Successful, $"Reaction failed: {reactionResult.ErrorMessage}");

        var finalInstance = await _repository.GetOrCreateInstanceAsync(avatarId, arcRef);
        var committed = finalInstance.GetCommittedTransactions();

        // The counter-kill must END the battle — it used to leave the battle "active"
        // against a 0-HP enemy with victory processing skipped
        var battleEnded = committed.FirstOrDefault(t =>
            t.Type == ArcTransactionType.BattleEnded &&
            t.Data[TransactionDataKeys.BattleTransactionId] == battleId.ToString());
        Assert.NotNull(battleEnded);
        Assert.Equal(bool.TrueString, battleEnded!.Data[TransactionDataKeys.AvatarVictory]);

        Assert.Contains(committed, t => t.Type == ArcTransactionType.CharacterDefeated);
    }

    [Fact]
    public async Task ExecuteBattleTurn_WhileTellPending_IsRejected()
    {
        var (avatarId, arcRef, battleId, avatar) = await StartBattleAgainst("TestEnemy");

        // Skipping the reaction and attacking used to make the telegraphed attack
        // silently evaporate (a repeatable free dodge)
        var turnResult = await _mediator.Send(new ExecuteBattleTurnCommand
        {
            AvatarId = avatarId,
            ArcRef = arcRef,
            BattleInstanceId = battleId,
            AvatarAction = new CombatAction { ActionType = ActionType.Attack },
            Avatar = avatar
        });

        Assert.False(turnResult.Successful);
        Assert.Contains("awaiting your reaction", turnResult.ErrorMessage);
    }

    public void Dispose()
    {
        _database?.Dispose();
        _serviceProvider?.Dispose();
    }

    #region Setup helpers

    private async Task<(Guid avatarId, string arcRef, Guid battleId, AvatarEntity avatar)> StartBattleAgainst(string enemyRef)
    {
        var avatarId = Guid.NewGuid();
        var avatar = CreateTestAvatar(avatarId);
        var arcRef = "TestArc";

        await _mediator.Send(new UpdateAvatarPositionCommand
        {
            AvatarId = avatarId,
            ArcRef = arcRef,
            Latitude = 35.0,
            Longitude = 139.0,
            Avatar = avatar
        });

        var instance = await _repository.GetOrCreateInstanceAsync(avatarId, arcRef);
        var enemyInstanceId = Guid.Parse(
            instance.GetCommittedTransactions()
                .First(t => t.Type == ArcTransactionType.CharacterSpawned &&
                            t.Data[TransactionDataKeys.CharacterRef] == enemyRef)
                .Data[TransactionDataKeys.CharacterInstanceId]);

        var battleSetup = new BattleSetup();
        battleSetup.SetupFromWorld(_world);
        battleSetup.SelectedAvatarArchetype = _world.AvatarArchetypesLookup["TestArchetype"];
        battleSetup.AvatarCapabilities = avatar.Capabilities ?? new ItemCollection();
        battleSetup.AvatarAffinityRefs = new List<string> { "Physical" };
        battleSetup.SelectedOpponentCharacter = _world.CharactersLookup[enemyRef];
        battleSetup.OpponentCapabilities = new ItemCollection();

        var battleEngine = battleSetup.CreateBattleEngine();

        var startResult = await _mediator.Send(new StartBattleCommand
        {
            AvatarId = avatarId,
            ArcRef = arcRef,
            EnemyCharacterInstanceId = enemyInstanceId,
            AvatarCombatant = battleEngine.GetAvatar(),
            EnemyCombatant = battleEngine.GetEnemy(),
            AvatarAffinityRefs = new List<string> { "Physical" },
            EnemyMind = new CombatAI(_world, 12345),
            RandomSeed = 12345,
            Avatar = avatar
        });
        Assert.True(startResult.Successful, $"Battle start failed: {startResult.ErrorMessage}");

        var battleId = startResult.TransactionIds.First();
        return (avatarId, arcRef, battleId, avatar);
    }

    private World CreateTestWorld()
    {
        var arc = new Arc
        {
            RefName = "TestArc",
            DisplayName = "Test Arena",
            Latitude = 35.0,
            Longitude = 139.0
        };

        var trigger = new ArcTrigger
        {
            RefName = "TestTrigger",
            EnterRadius = 100.0f,
            Spawn = new[]
            {
                new CharacterSpawn { CharacterRef = "TestEnemy" },
                new CharacterSpawn { CharacterRef = "FragileEnemy" }
            }
        };

        var enemy = new Character
        {
            RefName = "TestEnemy",
            DisplayName = "Test Enemy",
            Stats = new CharacterStats
            {
                Health = 0.5f,
                Strength = 0.1f,
                Defense = 0.1f
            }
        };

        var fragileEnemy = new Character
        {
            RefName = "FragileEnemy",
            DisplayName = "Fragile Enemy",
            Stats = new CharacterStats
            {
                // Healthy enough that its AI opens with an attack (and telegraphs),
                // but the parry counter (50x base damage) is still guaranteed lethal
                Health = 0.5f,
                Strength = 0.1f,
                Defense = 0.0f
            }
        };

        // Universal tell (no weapon restriction) — the enemy's opening attack telegraphs
        var testTell = new Ambient.Domain.AttackTell
        {
            RefName = "TestTell",
            TellText = "The enemy winds up a heavy swing!",
            ReactionWindowMs = 60000, // generous: tests must never hit the server timeout
            OptimalDefense = DefenseReactionType.Dodge,
            Pattern = Ambient.Domain.AttackPatternType.Slash,
            Outcome = new[]
            {
                new Ambient.Domain.DefenseOutcome { Reaction = DefenseReactionType.Dodge, DamageMultiplier = 0.0f },
                new Ambient.Domain.DefenseOutcome { Reaction = DefenseReactionType.Block, DamageMultiplier = 0.4f },
                new Ambient.Domain.DefenseOutcome { Reaction = DefenseReactionType.Parry, DamageMultiplier = 0.3f, EnablesCounter = true, CounterMultiplier = 50f },
                new Ambient.Domain.DefenseOutcome { Reaction = DefenseReactionType.Brace, DamageMultiplier = 0.6f },
                new Ambient.Domain.DefenseOutcome { Reaction = DefenseReactionType.None, DamageMultiplier = 1.0f }
            }
        };

        var archetype = new AvatarArchetype
        {
            RefName = "TestArchetype",
            DisplayName = "Test Archetype",
            AffinityRef = "Physical",
            SpawnStats = new CharacterStats
            {
                Health = 1.0f,
                Stamina = 1.0f,
                Mana = 1.0f,
                Strength = 0.2f,
                Defense = 0.15f,
                Speed = 0.12f,
                Magic = 0.1f
            },
            SpawnCapabilities = new ItemCollection
            {
                Equipment = Array.Empty<EquipmentEntry>(),
                Spells = Array.Empty<SpellEntry>(),
            }
        };

        var affinity = new CharacterAffinity
        {
            RefName = "Physical",
            DisplayName = "Physical"
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
                }
            },
            WorldTemplate = new WorldTemplate
            {
                Gameplay = new GameplayComponents
                {
                    Saga = new[] { arc },
                    Characters = new[] { enemy, fragileEnemy },
                    Equipment = Array.Empty<Equipment>(),
                    AvatarArchetypes = new[] { archetype },
                    Achievements = Array.Empty<Achievement>(),
                    CharacterAffinities = new[] { affinity },
                    DialogueTrees = Array.Empty<DialogueTree>(),
                    Consumables = Array.Empty<Consumable>(),
                    AttackTells = new[] { testTell }
                }
            }
        };

        world.ArcLookup[arc.RefName] = arc;
        world.ArcTriggersLookup[arc.RefName] = new List<ArcTrigger> { trigger };
        world.CharactersLookup[enemy.RefName] = enemy;
        world.CharactersLookup[fragileEnemy.RefName] = fragileEnemy;
        world.AvatarArchetypesLookup[archetype.RefName] = archetype;
        world.CharacterAffinitiesLookup[affinity.RefName] = affinity;

        return world;
    }

    private AvatarEntity CreateTestAvatar(Guid avatarId)
    {
        return new AvatarEntity
        {
            Id = avatarId,
            AvatarId = avatarId,
            DisplayName = "Test Avatar",
            ArchetypeRef = "TestArchetype",
            Stats = new CharacterStats
            {
                Health = 1.0f,
                Stamina = 1.0f,
                Mana = 1.0f,
                Strength = 0.2f,
                Defense = 0.15f,
                Speed = 0.12f,
                Magic = 0.1f
            },
            Capabilities = new ItemCollection
            {
                Equipment = Array.Empty<EquipmentEntry>(),
                Spells = Array.Empty<SpellEntry>(),
            },
            AffinityRef = "Physical"
        };
    }

    #endregion
}
