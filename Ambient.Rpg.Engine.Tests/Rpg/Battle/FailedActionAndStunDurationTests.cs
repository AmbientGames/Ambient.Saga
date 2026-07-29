using Ambient.Domain;
using Ambient.Domain.Partials;
using Ambient.Rpg.Engine.Domain.Battle;
using Xunit;

namespace Ambient.Rpg.Engine.Tests.Rpg.Battle;

/// <summary>
/// Regression tests for two battle-audit findings (2026-07-12).
///
/// F1 — a FAILED avatar action must still consume the avatar's turn. Per
/// BATTLE.md turn flow (steps 3-4), every avatar action — including failures
/// like a failed flee roll or being stunned — is followed by companion turns
/// and the enemy response. The engine used to advance the state machine only
/// on action.Success, which wedged failures in AvatarTurn: a failed flee was
/// a consequence-free retry (spam until the roll succeeds, the enemy never
/// punishes), and stunning the AVATAR silently cost the ENEMY its turns.
///
/// F2 — a status effect authored DurationTurns=N must block/apply for N of
/// the victim's turns. Duration used to be decremented AND removed in one
/// pass at the start of the victim's turn — BEFORE the stun/silence/root
/// check in ExecuteDecision ever ran — so an effect blocked only N-1 actions
/// and the shipped DurationTurns="1" Stun never stunned anyone. Now the
/// final charge is spent at turn start but the effect stays active through
/// that whole turn; removal happens at the top of the victim's NEXT
/// start-of-turn pass.
/// </summary>
public class FailedActionAndStunDurationTests
{
    // =========================================================================
    // F1 — failed avatar actions yield the turn
    // =========================================================================

    [Fact]
    public void FailedFlee_YieldsTurn_AndEnemyResponseLands()
    {
        // The flee roll is seed-deterministic; scan seeds until one fails
        // (base chance is 50% at Speed=0, so a failing seed is found quickly).
        for (var seed = 0; seed < 200; seed++)
        {
            var avatar = CreateCombatant("Avatar", speed: 0f);
            var enemy = CreateCombatant("Enemy");
            var engine = new BattleEngine(avatar, enemy, world: CreateStunWorld(), randomSeed: seed);

            engine.StartBattle(); // enemy opens; state hands to the avatar
            Assert.Equal(BattleState.AvatarTurn, engine.State);

            var healthAfterOpening = avatar.Health;
            var fleeResult = engine.ExecuteAvatarDecision(new CombatAction { ActionType = ActionType.Flee });
            if (fleeResult.Success)
            {
                continue; // this seed's roll escaped — try the next seed
            }

            // The failed roll consumed the turn: the enemy is up...
            Assert.Equal(BattleState.EnemyTurn, engine.State);

            // ...and its response actually lands on the would-be deserter.
            var enemyResponse = engine.ExecuteEnemyTurn();
            Assert.True(enemyResponse.Success);
            Assert.True(avatar.Health < healthAfterOpening,
                $"Enemy response after a failed flee must damage the avatar (health stayed at {avatar.Health:F3})");
            Assert.Equal(BattleState.AvatarTurn, engine.State);
            return;
        }

        Assert.True(false, "No seed in 0..199 produced a failed flee roll — test setup is broken");
    }

    [Fact]
    public void StunnedAvatar_FailedAction_YieldsTurn_AndEnemyResponds()
    {
        var avatar = CreateCombatant("Avatar");
        var enemy = CreateCombatant("Enemy");
        // RemainingTurns=2 so the stun is unambiguously active for this action
        // regardless of duration bookkeeping (F2 is covered separately below).
        avatar.ActiveStatusEffects.Add(Active("Stun2", remaining: 2));

        var engine = new BattleEngine(avatar, enemy, world: CreateStunWorld(), randomSeed: 7);
        engine.StartBattle();
        Assert.Equal(BattleState.AvatarTurn, engine.State);

        var healthAfterOpening = avatar.Health;
        var enemyHealthBefore = enemy.Health;
        var stunnedAttempt = engine.ExecuteAvatarDecision(new CombatAction { ActionType = ActionType.Attack });

        Assert.False(stunnedAttempt.Success);
        Assert.Equal(enemyHealthBefore, enemy.Health); // the blocked action did nothing
        // The blocked action still consumed the avatar's turn — the enemy is up.
        Assert.Equal(BattleState.EnemyTurn, engine.State);

        var enemyResponse = engine.ExecuteEnemyTurn();
        Assert.True(enemyResponse.Success);
        Assert.True(avatar.Health < healthAfterOpening,
            "The enemy must get (and land) its response while the avatar is stunned");
        Assert.Equal(BattleState.AvatarTurn, engine.State);
    }

    // =========================================================================
    // F2 — DurationTurns=N blocks exactly N of the victim's actions
    // =========================================================================

    [Fact]
    public void Stun_DurationOne_BlocksExactlyOneEnemyAction()
    {
        var world = CreateStunWorld();
        var avatar = CreateCombatant("Avatar");
        var enemy = CreateCombatant("Enemy");
        enemy.ActiveStatusEffects.Add(Active("Stun1", remaining: 1));

        var engine = new BattleEngine(avatar, enemy, new CombatAI(world, 11), world, randomSeed: 11);
        engine.ResumeBattle(1); // AvatarTurn, no opening enemy turn

        // Avatar turn 1: defend (deterministic, no RNG), handing off to the stunned enemy
        Assert.True(engine.ExecuteAvatarDecision(new CombatAction { ActionType = ActionType.Defend }).Success);
        Assert.Equal(BattleState.EnemyTurn, engine.State);

        var healthBeforeEnemyTurn1 = avatar.Health;
        var enemyTurn1 = engine.ExecuteEnemyTurn();

        // Enemy action 1: the 1-turn stun blocks it completely
        Assert.False(enemyTurn1.Success);
        Assert.Equal(healthBeforeEnemyTurn1, avatar.Health);
        Assert.Equal(BattleState.AvatarTurn, engine.State); // lost turn still hands off

        // Avatar turn 2: defend again
        Assert.True(engine.ExecuteAvatarDecision(new CombatAction { ActionType = ActionType.Defend }).Success);
        var healthBeforeEnemyTurn2 = avatar.Health;
        var enemyTurn2 = engine.ExecuteEnemyTurn();

        // Enemy action 2: the stun is spent — the enemy acts and lands damage
        Assert.True(enemyTurn2.Success);
        Assert.True(avatar.Health < healthBeforeEnemyTurn2,
            "The stun lasted one turn — the enemy's second action must land");
        Assert.Empty(enemy.ActiveStatusEffects);
    }

    [Fact]
    public void Stun_DurationTwo_BlocksExactlyTwoEnemyActions()
    {
        var world = CreateStunWorld();
        var avatar = CreateCombatant("Avatar");
        var enemy = CreateCombatant("Enemy");
        enemy.ActiveStatusEffects.Add(Active("Stun2", remaining: 2));

        var engine = new BattleEngine(avatar, enemy, new CombatAI(world, 11), world, randomSeed: 11);
        engine.ResumeBattle(1);

        // Enemy action 1: blocked
        Assert.True(engine.ExecuteAvatarDecision(new CombatAction { ActionType = ActionType.Defend }).Success);
        var healthBeforeEnemyTurn1 = avatar.Health;
        var enemyTurn1 = engine.ExecuteEnemyTurn();
        Assert.False(enemyTurn1.Success);
        Assert.Equal(healthBeforeEnemyTurn1, avatar.Health);

        // Enemy action 2: still blocked (a 2-turn stun covers TWO enemy actions)
        Assert.True(engine.ExecuteAvatarDecision(new CombatAction { ActionType = ActionType.Defend }).Success);
        var healthBeforeEnemyTurn2 = avatar.Health;
        var enemyTurn2 = engine.ExecuteEnemyTurn();
        Assert.False(enemyTurn2.Success);
        Assert.Equal(healthBeforeEnemyTurn2, avatar.Health);

        // Enemy action 3: stun spent — the attack lands
        Assert.True(engine.ExecuteAvatarDecision(new CombatAction { ActionType = ActionType.Defend }).Success);
        var healthBeforeEnemyTurn3 = avatar.Health;
        var enemyTurn3 = engine.ExecuteEnemyTurn();
        Assert.True(enemyTurn3.Success);
        Assert.True(avatar.Health < healthBeforeEnemyTurn3,
            "The stun lasted two turns — the enemy's third action must land");
        Assert.Empty(enemy.ActiveStatusEffects);
    }

    [Fact]
    public void Stun_DurationOne_OnAvatar_BlocksExactlyOneAvatarAction()
    {
        var world = CreateStunWorld();
        var avatar = CreateCombatant("Avatar");
        var enemy = CreateCombatant("Enemy");
        avatar.ActiveStatusEffects.Add(Active("Stun1", remaining: 1));

        var engine = new BattleEngine(avatar, enemy, new CombatAI(world, 3), world, randomSeed: 3);
        engine.ResumeBattle(1);

        // Command 1 (mirrors ExecuteBattleTurnHandler): tick avatar effects, then act.
        engine.ProcessAvatarTurnStart();
        var enemyHealthBefore = enemy.Health;
        var attempt1 = engine.ExecuteAvatarDecision(new CombatAction { ActionType = ActionType.Attack });

        Assert.False(attempt1.Success); // the 1-turn stun must block this action
        Assert.Equal(enemyHealthBefore, enemy.Health);
        Assert.Equal(BattleState.EnemyTurn, engine.State); // F1: the blocked action consumed the turn

        var enemyResponse = engine.ExecuteEnemyTurn();
        Assert.True(enemyResponse.Success);
        Assert.Equal(BattleState.AvatarTurn, engine.State);

        // Command 2: the stun is spent and removed — the avatar acts freely.
        engine.ProcessAvatarTurnStart();
        Assert.Empty(avatar.ActiveStatusEffects);
        var attempt2 = engine.ExecuteAvatarDecision(new CombatAction { ActionType = ActionType.Attack });
        Assert.True(attempt2.Success);
        Assert.True(enemy.Health < enemyHealthBefore,
            "The stun lasted one turn — the avatar's second action must land");
    }

    // =========================================================================
    // helpers
    // =========================================================================

    private static ActiveStatusEffect Active(string refName, int remaining) => new()
    {
        StatusEffectRef = refName,
        RemainingTurns = remaining,
        Stacks = 1,
        AppliedOnTurn = 1
    };

    private static Combatant CreateCombatant(string name, float speed = 0.3f) => new()
    {
        RefName = name,
        DisplayName = name,
        Health = 1.0f,
        Stamina = 1.0f,
        Strength = 0.3f,
        Defense = 0.3f,
        Speed = speed,
        Magic = 0.3f,
        Capabilities = new ItemCollection(),
        CombatProfile = new Dictionary<string, string>()
    };

    private static World CreateStunWorld()
    {
        // Stun1 mirrors the shipped content authoring (DurationTurns="1") that the
        // old decrement-then-remove ordering turned into a complete no-op.
        var stun1 = new StatusEffect
        {
            RefName = "Stun1",
            DisplayName = "Stun (1 turn)",
            Type = StatusEffectType.Stun,
            DurationTurns = 1,
            MaxStacks = 0,
            Cleansable = true
        };

        var stun2 = new StatusEffect
        {
            RefName = "Stun2",
            DisplayName = "Stun (2 turns)",
            Type = StatusEffectType.Stun,
            DurationTurns = 2,
            MaxStacks = 0,
            Cleansable = true
        };

        var world = new World
        {
            WorldTemplate = new WorldTemplate
            {
                Gameplay = new GameplayComponents
                {
                    StatusEffects = new[] { stun1, stun2 }
                }
            }
        };

        world.StatusEffectsLookup["Stun1"] = stun1;
        world.StatusEffectsLookup["Stun2"] = stun2;

        return world;
    }
}
