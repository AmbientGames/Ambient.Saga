using Ambient.Domain;
using Ambient.Domain.Contracts;
using Ambient.Domain.Partials;
using Ambient.Domain.Entities;
using Ambient.Rpg.Engine.Application.Behaviors;
using Ambient.Rpg.Engine.Application.Commands.Arcs;
using Ambient.Rpg.Engine.Application.Queries.Arcs;
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
/// Handler-level tell-EXPIRY coverage. Every other reaction test authors a 60s
/// window so the server timeout can never fire; here the persisted tell is
/// backdated past window+grace so the expiry consumption paths actually execute:
///
/// - SubmitReaction on an expired tell: the server forces the un-reacted (None)
///   outcome regardless of the client's chosen reaction (no post-hoc cherry-picking
///   of a good outcome after walking away).
/// - ExecuteBattleTurn on an expired tell: the abandoned tell is auto-resolved as an
///   un-reacted hit first (TimedOut=true), then the avatar's action proceeds.
/// - Un-reacted LETHAL hit: auto-resolve kills the avatar -> Defeat -> BattleEnded,
///   and the avatar's submitted action never executes.
/// - ReactionWindowMs=0 means NO time limit (XSD contract / M14): the tell never
///   expires server-side — turns stay blocked and a late reaction is honored as chosen.
/// </summary>
[Collection("Sequential CQRS Tests")]
public class TellExpiryIntegrationTests
{
    private readonly ITestOutputHelper _output;

    public TellExpiryIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    // ===== Expired positive window: SubmitReaction is forced to None =====

    [Fact]
    public async Task ExpiredTell_SubmitReaction_ForcesUnreactedOutcome()
    {
        using var stack = CreateStack(reactionWindowMs: 1500);
        var (avatarId, arcRef, battleId, avatar) = await StartBattleAgainst(stack, "TestEnemy", "TestArchetype");

        var baseDamage = GetPendingTellBaseDamage(stack, avatarId, arcRef, battleId);
        BackdateUnresolvedTell(stack, avatarId, arcRef, battleId, TimeSpan.FromMinutes(10));

        // The client claims a perfect parry — the window lapsed, so the server must
        // resolve the un-reacted hit instead
        var result = await stack.Mediator.Send(new SubmitReactionCommand
        {
            AvatarId = avatarId,
            ArcRef = arcRef,
            BattleInstanceId = battleId,
            Reaction = AvatarDefenseType.Parry,
            Avatar = avatar
        });
        Assert.True(result.Successful, $"Reaction failed: {result.ErrorMessage}");

        var reactionTx = GetSingleReactionTx(stack, avatarId, arcRef, battleId);
        Assert.Equal(nameof(AvatarDefenseType.None), reactionTx.Data[TransactionDataKeys.ReactionType]);
        Assert.Equal(bool.TrueString, reactionTx.Data[TransactionDataKeys.TimedOut]);

        // None outcome is authored with DamageMultiplier 1.0 and no counter
        Assert.True(BattleTransactionHelper.TryParseFloat(
            reactionTx.Data[TransactionDataKeys.DamageDealt], out var damage));
        Assert.Equal(baseDamage, damage, 3);
        Assert.True(BattleTransactionHelper.TryParseFloat(
            reactionTx.Data[TransactionDataKeys.CounterDamage], out var counter));
        Assert.Equal(0f, counter, 3);
    }

    // ===== Expired positive window: ExecuteBattleTurn auto-resolves, then acts =====

    [Fact]
    public async Task ExpiredTell_ExecuteBattleTurn_AutoResolvesAsUnreactedHit_ThenExecutesAction()
    {
        using var stack = CreateStack(reactionWindowMs: 1500);
        var (avatarId, arcRef, battleId, avatar) = await StartBattleAgainst(stack, "TestEnemy", "TestArchetype");

        var baseDamage = GetPendingTellBaseDamage(stack, avatarId, arcRef, battleId);
        BackdateUnresolvedTell(stack, avatarId, arcRef, battleId, TimeSpan.FromMinutes(10));

        var result = await stack.Mediator.Send(new ExecuteBattleTurnCommand
        {
            AvatarId = avatarId,
            ArcRef = arcRef,
            BattleInstanceId = battleId,
            AvatarAction = new CombatAction { ActionType = ActionType.Defend },
            Avatar = avatar
        });
        Assert.True(result.Successful, $"Turn failed: {result.ErrorMessage}");

        var committed = GetCommittedBattleTurns(stack, avatarId, arcRef, battleId);

        // 1) The abandoned tell was consumed as a server-resolved un-reacted hit.
        // (TryGetValue, not the raw indexer: only "Reaction"/"Tell" turns carry an
        // ActionType key — ordinary avatar/enemy action turns use DecisionType, so a
        // raw index over the whole committed set throws on them.)
        var reactionTx = Assert.Single(committed, t =>
            t.Data.TryGetValue(TransactionDataKeys.ActionType, out var action) && action == "Reaction");
        Assert.Equal(bool.TrueString, reactionTx.Data[TransactionDataKeys.TimedOut]);
        Assert.Equal(nameof(AvatarDefenseType.None), reactionTx.Data[TransactionDataKeys.ReactionType]);
        Assert.True(BattleTransactionHelper.TryParseFloat(
            reactionTx.Data[TransactionDataKeys.DamageDealt], out var damage));
        Assert.Equal(baseDamage, damage, 3);

        // 2) The avatar's own action executed AFTER the auto-resolve
        var defendTx = Assert.Single(committed, t =>
            t.Data.TryGetValue(TransactionDataKeys.DecisionType, out var decision) &&
            decision == ActionType.Defend.ToString() &&
            (!t.Data.TryGetValue(TransactionDataKeys.ActionType, out var at) || at != "Reaction"));
        Assert.True(
            int.Parse(defendTx.Data[TransactionDataKeys.TurnNumber]) >
            int.Parse(reactionTx.Data[TransactionDataKeys.TurnNumber]),
            "Avatar action must resolve after the abandoned tell");

        _output.WriteLine($"Auto-resolved hit for {damage:F3}, then Defend executed");
    }

    // ===== Expired tell whose un-reacted hit is lethal: Defeat -> BattleEnded =====

    [Fact]
    public async Task ExpiredTell_UnreactedLethalHit_EndsBattleInDefeat()
    {
        using var stack = CreateStack(reactionWindowMs: 1500);
        var (avatarId, arcRef, battleId, avatar) = await StartBattleAgainst(stack, "BruteEnemy", "FragileArchetype");

        var baseDamage = GetPendingTellBaseDamage(stack, avatarId, arcRef, battleId);
        Assert.True(baseDamage > 0.05f,
            $"Precondition: the brute's telegraphed hit ({baseDamage:F3}) must exceed the fragile avatar's 0.05 health");

        BackdateUnresolvedTell(stack, avatarId, arcRef, battleId, TimeSpan.FromMinutes(10));

        var result = await stack.Mediator.Send(new ExecuteBattleTurnCommand
        {
            AvatarId = avatarId,
            ArcRef = arcRef,
            BattleInstanceId = battleId,
            AvatarAction = new CombatAction { ActionType = ActionType.Defend },
            Avatar = avatar
        });
        Assert.True(result.Successful, $"Turn failed: {result.ErrorMessage}");

        var instance = await stack.Repository.GetOrCreateInstanceAsync(avatarId, arcRef);
        var committed = instance.GetCommittedTransactions();

        // The un-reacted hit was persisted and killed the avatar
        var reactionTx = Assert.Single(committed, t =>
            t.Type == ArcTransactionType.BattleTurnExecuted &&
            t.Data.TryGetValue(TransactionDataKeys.ActionType, out var action) &&
            action == "Reaction");
        Assert.Equal(bool.TrueString, reactionTx.Data[TransactionDataKeys.TimedOut]);
        Assert.True(BattleTransactionHelper.TryParseFloat(
            reactionTx.Data[TransactionDataKeys.AvatarHealthAfterTurn], out var avatarHealth));
        Assert.Equal(0f, avatarHealth, 3);

        // Defeat sealed the battle
        var battleEnded = committed.FirstOrDefault(t =>
            t.Type == ArcTransactionType.BattleEnded &&
            t.Data[TransactionDataKeys.BattleTransactionId] == battleId.ToString());
        Assert.NotNull(battleEnded);
        Assert.Equal(bool.FalseString, battleEnded!.Data[TransactionDataKeys.AvatarVictory]);

        // The avatar died before acting: the submitted Defend must NOT have executed
        Assert.DoesNotContain(committed, t =>
            t.Type == ArcTransactionType.BattleTurnExecuted &&
            t.Data.TryGetValue(TransactionDataKeys.ActionType, out var action) &&
            action != "Reaction" && action != "Tell" &&
            t.Data.TryGetValue(TransactionDataKeys.IsAvatarTurn, out var isAvatar) &&
            isAvatar == bool.TrueString &&
            t.Data.TryGetValue(TransactionDataKeys.DecisionType, out var decision) &&
            decision == ActionType.Defend.ToString());
    }

    // ===== ReactionWindowMs = 0: no time limit (post-M14 semantics) =====

    [Fact]
    public async Task ZeroWindow_TellNeverExpires_TurnsStayBlocked()
    {
        using var stack = CreateStack(reactionWindowMs: 0);
        var (avatarId, arcRef, battleId, avatar) = await StartBattleAgainst(stack, "TestEnemy", "TestArchetype");

        // Even 10 minutes after the tell (far past the 3s grace), a 0-window tell
        // must NOT be treated as abandoned — 0 means "no time limit" per the XSD
        BackdateUnresolvedTell(stack, avatarId, arcRef, battleId, TimeSpan.FromMinutes(10));

        var result = await stack.Mediator.Send(new ExecuteBattleTurnCommand
        {
            AvatarId = avatarId,
            ArcRef = arcRef,
            BattleInstanceId = battleId,
            AvatarAction = new CombatAction { ActionType = ActionType.Attack },
            Avatar = avatar
        });

        Assert.False(result.Successful);
        Assert.Contains("awaiting your reaction", result.ErrorMessage);
    }

    [Fact]
    public async Task ZeroWindow_LateReaction_IsHonoredAsChosen()
    {
        using var stack = CreateStack(reactionWindowMs: 0);
        var (avatarId, arcRef, battleId, avatar) = await StartBattleAgainst(stack, "TestEnemy", "TestArchetype");

        var baseDamage = GetPendingTellBaseDamage(stack, avatarId, arcRef, battleId);
        BackdateUnresolvedTell(stack, avatarId, arcRef, battleId, TimeSpan.FromMinutes(10));

        var result = await stack.Mediator.Send(new SubmitReactionCommand
        {
            AvatarId = avatarId,
            ArcRef = arcRef,
            BattleInstanceId = battleId,
            Reaction = AvatarDefenseType.Block,
            Avatar = avatar
        });
        Assert.True(result.Successful, $"Reaction failed: {result.ErrorMessage}");

        var reactionTx = GetSingleReactionTx(stack, avatarId, arcRef, battleId);
        Assert.Equal(nameof(AvatarDefenseType.Block), reactionTx.Data[TransactionDataKeys.ReactionType]);
        Assert.NotEqual(bool.TrueString, reactionTx.Data[TransactionDataKeys.TimedOut]);

        // Block outcome authored with DamageMultiplier 0.4 — chosen reaction honored
        Assert.True(BattleTransactionHelper.TryParseFloat(
            reactionTx.Data[TransactionDataKeys.DamageDealt], out var damage));
        Assert.Equal(baseDamage * 0.4f, damage, 3);
    }

    // ===== Suspend mid-tell (window closed), then re-engage: reaction prompt is restored =====

    [Fact]
    public async Task SuspendedMidTell_ReEngage_RestoresAwaitingReactionForUi()
    {
        // A distinctive positive window so we can assert it round-trips to the UI.
        using var stack = CreateStack(reactionWindowMs: 4200);
        var (avatarId, arcRef, battleId, avatar) = await StartBattleAgainst(stack, "TestEnemy", "TestArchetype");

        // The enemy opened with a telegraph and the player CLOSED the battle window
        // without reacting (BattleModalAdapter.OnClosed sends no flee/end — a window-close
        // must not apply the Disengaged truce). The battle is therefore left un-ended with
        // a pending, unresolved tell — the exact wedge state.
        var suspended = await stack.Repository.GetOrCreateInstanceAsync(avatarId, arcRef);
        Assert.NotNull(ReactionTransactionFactory.FindUnresolvedTell(suspended, battleId));

        // --- RE-ENGAGE: a fresh StartBattleCommand for the same enemy hits the resume path.
        // It must return the EXISTING battle (not start a new one) AND re-present the pending
        // telegraph in the same shape the fresh-start path returns, so the UI's
        // TryStartReactionFromResult can rebuild the reaction phase.
        var enemyInstanceId = GetEnemyInstanceId(stack, avatarId, arcRef, "TestEnemy");
        var reBattleSetup = new BattleSetup();
        reBattleSetup.SetupFromWorld(stack.World);
        reBattleSetup.SelectedAvatarArchetype = stack.World.AvatarArchetypesLookup["TestArchetype"];
        reBattleSetup.AvatarCapabilities = avatar.Capabilities ?? new ItemCollection();
        reBattleSetup.AvatarAffinityRefs = new List<string> { "Physical" };
        reBattleSetup.SelectedOpponentCharacter = stack.World.CharactersLookup["TestEnemy"];
        reBattleSetup.OpponentCapabilities = new ItemCollection();
        var reEngine = reBattleSetup.CreateBattleEngine();

        var resume = await stack.Mediator.Send(new StartBattleCommand
        {
            AvatarId = avatarId,
            ArcRef = arcRef,
            EnemyCharacterInstanceId = enemyInstanceId,
            AvatarCombatant = reEngine.GetAvatar(),
            EnemyCombatant = reEngine.GetEnemy(),
            AvatarAffinityRefs = new List<string> { "Physical" },
            EnemyMind = new CombatAI(stack.World, 12345),
            RandomSeed = 12345,
            Avatar = avatar
        });
        Assert.True(resume.Successful, $"Re-engage failed: {resume.ErrorMessage}");
        Assert.Equal(battleId, (Guid)resume.Data[TransactionDataKeys.BattleInstanceId]);
        Assert.True(
            resume.Data.TryGetValue(TransactionDataKeys.AwaitingReaction, out var awaitingObj) && (bool)awaitingObj,
            "resume result must carry AwaitingReaction so the client rebuilds the reaction engine");
        Assert.False(string.IsNullOrEmpty(resume.Data[TransactionDataKeys.TellText] as string),
            "resume result must carry the tell narrative text");

        // Re-engaging must NOT spawn a second battle: still exactly one BattleStarted.
        var afterResume = await stack.Repository.GetOrCreateInstanceAsync(avatarId, arcRef);
        Assert.Equal(1, afterResume.GetCommittedTransactions()
            .Count(t => t.Type == ArcTransactionType.BattleStarted));

        // --- RESUME QUERY: BattleModal.RefreshBattleStateAsync re-queries state on re-open.
        // The reconstructed result must expose the awaiting-reaction fields the UI reads to
        // render the reaction prompt. Before the fix these were never populated, so the modal
        // showed "Enemy's turn..." with no buttons and no way to advance — the soft-lock.
        var state = await stack.Mediator.Send(new GetBattleStateQuery
        {
            AvatarId = avatarId,
            ArcRef = arcRef,
            BattleInstanceId = battleId,
            Avatar = avatar
        });

        Assert.False(state.HasEnded);
        Assert.Equal(BattleState.AwaitingReaction, state.BattleState);
        Assert.True(state.IsAwaitingReaction,
            "resumed mid-tell battle must report awaiting-reaction to the UI");
        Assert.False(string.IsNullOrEmpty(state.PendingTellText),
            "the reaction prompt needs the tell narrative text");
        Assert.Equal(4200, state.PendingReactionWindowMs);
        Assert.False(string.IsNullOrEmpty(state.PendingTellRefName),
            "the tell ref must survive so the reaction resolves against real content");

        _output.WriteLine(
            $"Re-engaged suspended tell: state={state.BattleState}, tell='{state.PendingTellText}', window={state.PendingReactionWindowMs}ms");
    }

    #region Stack / world / battle setup

    /// <summary>Per-test service stack — each test authors its own tell window.</summary>
    private sealed class TestStack : IDisposable
    {
        public required ServiceProvider Services { get; init; }
        public required IMediator Mediator { get; init; }
        public required LiteDatabase Database { get; init; }
        public required IArcInstanceRepository Repository { get; init; }
        public required World World { get; init; }

        public void Dispose()
        {
            Database.Dispose();
            Services.Dispose();
        }
    }

    private static TestStack CreateStack(int reactionWindowMs)
    {
        var database = new LiteDatabase(new MemoryStream());
        var world = CreateTestWorld(reactionWindowMs);

        var services = new ServiceCollection();
        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssemblyContaining<SubmitReactionCommand>();
            cfg.AddOpenBehavior(typeof(LoggingBehavior<,>));
            cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));
            cfg.AddOpenBehavior(typeof(AchievementEvaluationBehavior<,>));
        });

        services.AddSingleton<IWorld>(world);
        services.AddSingleton<IArcInstanceRepository>(new ArcInstanceRepository(database));
        services.AddSingleton<IAvatarProgressRepository>(new AvatarProgressRepository(database));
        services.AddSingleton<IArcReadModelRepository, InMemoryArcReadModelRepository>();
        services.AddSingleton<IAvatarUpdateService, StubAvatarUpdateService>();
        services.AddSingleton<IWorldStateRepository, StubWorldStateRepository>();

        var provider = services.BuildServiceProvider();
        return new TestStack
        {
            Services = provider,
            Mediator = provider.GetRequiredService<IMediator>(),
            Database = database,
            Repository = provider.GetRequiredService<IArcInstanceRepository>(),
            World = world
        };
    }

    private static async Task<(Guid avatarId, string arcRef, Guid battleId, AvatarEntity avatar)> StartBattleAgainst(
        TestStack stack, string enemyRef, string archetypeRef)
    {
        var avatarId = Guid.NewGuid();
        var avatar = CreateTestAvatar(avatarId, archetypeRef);
        var arcRef = "TestArc";

        await stack.Mediator.Send(new UpdateAvatarPositionCommand
        {
            AvatarId = avatarId,
            ArcRef = arcRef,
            Latitude = 35.0,
            Longitude = 139.0,
            Avatar = avatar
        });

        var instance = await stack.Repository.GetOrCreateInstanceAsync(avatarId, arcRef);
        var enemyInstanceId = Guid.Parse(
            instance.GetCommittedTransactions()
                .First(t => t.Type == ArcTransactionType.CharacterSpawned &&
                            t.Data[TransactionDataKeys.CharacterRef] == enemyRef)
                .Data[TransactionDataKeys.CharacterInstanceId]);

        var battleSetup = new BattleSetup();
        battleSetup.SetupFromWorld(stack.World);
        battleSetup.SelectedAvatarArchetype = stack.World.AvatarArchetypesLookup[archetypeRef];
        battleSetup.AvatarCapabilities = avatar.Capabilities ?? new ItemCollection();
        battleSetup.AvatarAffinityRefs = new List<string> { "Physical" };
        battleSetup.SelectedOpponentCharacter = stack.World.CharactersLookup[enemyRef];
        battleSetup.OpponentCapabilities = new ItemCollection();

        var battleEngine = battleSetup.CreateBattleEngine();

        var startResult = await stack.Mediator.Send(new StartBattleCommand
        {
            AvatarId = avatarId,
            ArcRef = arcRef,
            EnemyCharacterInstanceId = enemyInstanceId,
            AvatarCombatant = battleEngine.GetAvatar(),
            EnemyCombatant = battleEngine.GetEnemy(),
            AvatarAffinityRefs = new List<string> { "Physical" },
            EnemyMind = new CombatAI(stack.World, 12345),
            RandomSeed = 12345,
            Avatar = avatar
        });
        Assert.True(startResult.Successful, $"Battle start failed: {startResult.ErrorMessage}");

        return (avatarId, arcRef, startResult.TransactionIds.First(), avatar);
    }

    private static Guid GetEnemyInstanceId(TestStack stack, Guid avatarId, string arcRef, string enemyRef)
    {
        var instance = stack.Repository.GetOrCreateInstanceAsync(avatarId, arcRef).GetAwaiter().GetResult();
        return Guid.Parse(instance.GetCommittedTransactions()
            .First(t => t.Type == ArcTransactionType.CharacterSpawned &&
                        t.Data[TransactionDataKeys.CharacterRef] == enemyRef)
            .Data[TransactionDataKeys.CharacterInstanceId]);
    }

    private static float GetPendingTellBaseDamage(TestStack stack, Guid avatarId, string arcRef, Guid battleId)
    {
        var instance = stack.Repository.GetOrCreateInstanceAsync(avatarId, arcRef).GetAwaiter().GetResult();
        var tellTx = ReactionTransactionFactory.FindUnresolvedTell(instance, battleId);
        Assert.NotNull(tellTx);
        Assert.True(BattleTransactionHelper.TryParseFloat(
            tellTx!.Data[TransactionDataKeys.BaseDamage], out var baseDamage));
        Assert.True(baseDamage > 0f, "Telegraphed attack must carry its base damage");
        return baseDamage;
    }

    /// <summary>
    /// Rewinds the persisted tell's issue timestamp so the reaction window (plus
    /// the server's grace) has lapsed — the deterministic stand-in for a player who
    /// crashed or walked away mid-tell. Updates both the cached instance and the
    /// durable LiteDB record so the handlers see the backdated tell regardless of
    /// whether they hit the cache or reload.
    /// </summary>
    private static void BackdateUnresolvedTell(TestStack stack, Guid avatarId, string arcRef, Guid battleId, TimeSpan by)
    {
        var instance = stack.Repository.GetOrCreateInstanceAsync(avatarId, arcRef).GetAwaiter().GetResult();
        var tellTx = ReactionTransactionFactory.FindUnresolvedTell(instance, battleId);
        Assert.NotNull(tellTx);

        var backdated = DateTime.UtcNow - by;
        tellTx!.LocalTimestamp = backdated;

        var records = stack.Database.GetCollection<ArcTransactionRecord>("ArcTransactions");
        var record = records.FindOne(r => r.TransactionId == tellTx.TransactionId);
        Assert.NotNull(record);
        record.LocalTimestamp = backdated;
        records.Update(record);
    }

    private static ArcTransaction GetSingleReactionTx(TestStack stack, Guid avatarId, string arcRef, Guid battleId)
    {
        var instance = stack.Repository.GetOrCreateInstanceAsync(avatarId, arcRef).GetAwaiter().GetResult();
        return instance.GetCommittedTransactions()
            .Single(t => t.Type == ArcTransactionType.BattleTurnExecuted &&
                         t.Data.TryGetValue(TransactionDataKeys.BattleTransactionId, out var id) &&
                         id == battleId.ToString() &&
                         t.Data.TryGetValue(TransactionDataKeys.ActionType, out var action) &&
                         action == "Reaction");
    }

    private static List<ArcTransaction> GetCommittedBattleTurns(TestStack stack, Guid avatarId, string arcRef, Guid battleId)
    {
        var instance = stack.Repository.GetOrCreateInstanceAsync(avatarId, arcRef).GetAwaiter().GetResult();
        return instance.GetCommittedTransactions()
            .Where(t => t.Type == ArcTransactionType.BattleTurnExecuted &&
                        t.Data.TryGetValue(TransactionDataKeys.BattleTransactionId, out var id) &&
                        id == battleId.ToString())
            .ToList();
    }

    private static World CreateTestWorld(int reactionWindowMs)
    {
        var arc = new Arc
        {
            RefName = "TestArc",
            DisplayName = "Tell Expiry Arena",
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
                new CharacterSpawn { CharacterRef = "BruteEnemy" }
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

        // Hits hard enough that its un-reacted telegraphed attack kills the fragile avatar
        var bruteEnemy = new Character
        {
            RefName = "BruteEnemy",
            DisplayName = "Brute Enemy",
            Stats = new CharacterStats
            {
                Health = 0.9f,
                Strength = 0.9f,
                Defense = 0.1f
            }
        };

        // Universal tell (no weapon restriction) — the window under test is authored here
        var testTell = new Ambient.Domain.AttackTell
        {
            RefName = "ExpiryTell",
            TellText = "The enemy winds up a heavy swing!",
            ReactionWindowMs = reactionWindowMs,
            OptimalDefense = DefenseReactionType.Dodge,
            Pattern = Ambient.Domain.AttackPatternType.Slash,
            Outcome = new[]
            {
                new Ambient.Domain.DefenseOutcome { Reaction = DefenseReactionType.Dodge, DamageMultiplier = 0.0f },
                new Ambient.Domain.DefenseOutcome { Reaction = DefenseReactionType.Block, DamageMultiplier = 0.4f },
                new Ambient.Domain.DefenseOutcome { Reaction = DefenseReactionType.Parry, DamageMultiplier = 0.3f, EnablesCounter = true, CounterMultiplier = 2f },
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

        // One un-reacted hit from the brute is guaranteed lethal
        var fragileArchetype = new AvatarArchetype
        {
            RefName = "FragileArchetype",
            DisplayName = "Fragile Archetype",
            AffinityRef = "Physical",
            SpawnStats = new CharacterStats
            {
                Health = 0.05f,
                Stamina = 1.0f,
                Mana = 1.0f,
                Strength = 0.2f,
                Defense = 0.1f,
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
                    Characters = new[] { enemy, bruteEnemy },
                    Equipment = Array.Empty<Equipment>(),
                    AvatarArchetypes = new[] { archetype, fragileArchetype },
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
        world.CharactersLookup[bruteEnemy.RefName] = bruteEnemy;
        world.AvatarArchetypesLookup[archetype.RefName] = archetype;
        world.AvatarArchetypesLookup[fragileArchetype.RefName] = fragileArchetype;
        world.CharacterAffinitiesLookup[affinity.RefName] = affinity;

        return world;
    }

    private static AvatarEntity CreateTestAvatar(Guid avatarId, string archetypeRef)
    {
        return new AvatarEntity
        {
            Id = avatarId,
            AvatarId = avatarId,
            DisplayName = "Test Avatar",
            ArchetypeRef = archetypeRef,
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
