using Ambient.Domain;
using Ambient.Domain.Contracts;
using Ambient.Domain.Partials;
using Ambient.Rpg.Engine.Domain.Battle;
using Xunit;

namespace Ambient.Rpg.Engine.Tests.Rpg.Battle;

/// <summary>
/// Regression tests for battle event stamping (round-3 audit finding A1).
///
/// Every CombatEvent that the CQRS handlers persist as a BattleTurnExecuted
/// transaction MUST carry the transaction-logging fields (TargetHealthAfter,
/// ActorEnergyAfter, TargetName, TurnNumber, IsAvatarTurn). The stun/silence/root
/// early returns and the died-from-status-effects paths used to skip stamping,
/// which persisted TargetHealthAfter=0 with an empty Target — state reconstruction
/// then zeroed a LIVING combatant's health (stunning the enemy insta-killed the
/// player on the next command).
/// </summary>
public class BattleEventStampingTests
{
    [Fact]
    public void ExecuteAvatarDecision_WhileStunned_FailureEventIsFullyStamped()
    {
        var avatar = CreateCombatant("Avatar", health: 1.0f);
        var enemy = CreateCombatant("Enemy", health: 0.8f);

        avatar.ActiveStatusEffects.Add(new ActiveStatusEffect
        {
            StatusEffectRef = "Stun",
            RemainingTurns = 2,
            Stacks = 1,
            AppliedOnTurn = 1
        });

        var engine = new BattleEngine(avatar, enemy, world: CreateStatusEffectWorld(), randomSeed: 42);
        engine.StartBattle();
        Assert.Equal(BattleState.AvatarTurn, engine.State);

        var result = engine.ExecuteAvatarDecision(new CombatAction { ActionType = ActionType.Attack });

        Assert.False(result.Success);
        // The failure event must carry real state, not defaults — it gets persisted
        Assert.Equal(enemy.Health, result.TargetHealthAfter, 3);
        Assert.True(result.TargetHealthAfter > 0f, "Stunned-turn event must not report the living target at 0 health");
        Assert.Equal(avatar.Stamina, result.ActorEnergyAfter, 3);
        Assert.False(string.IsNullOrEmpty(result.TargetName));
        Assert.True(result.IsAvatarTurn);
        Assert.True(result.TurnNumber > 0);
    }

    [Fact]
    public void ExecuteAvatarDecision_WhileSilenced_FailureEventIsFullyStamped()
    {
        var avatar = CreateCombatant("Avatar", health: 1.0f);
        var enemy = CreateCombatant("Enemy", health: 0.8f);

        avatar.ActiveStatusEffects.Add(new ActiveStatusEffect
        {
            StatusEffectRef = "Silence",
            RemainingTurns = 2,
            Stacks = 1,
            AppliedOnTurn = 1
        });

        var engine = new BattleEngine(avatar, enemy, world: CreateStatusEffectWorld(), randomSeed: 42);
        engine.StartBattle();

        var result = engine.ExecuteAvatarDecision(new CombatAction
        {
            ActionType = ActionType.CastSpell,
            Parameter = "AnySpell"
        });

        Assert.False(result.Success);
        Assert.Equal(enemy.Health, result.TargetHealthAfter, 3);
        Assert.True(result.TargetHealthAfter > 0f);
        Assert.Equal(avatar.Stamina, result.ActorEnergyAfter, 3);
        Assert.False(string.IsNullOrEmpty(result.TargetName));
        Assert.True(result.IsAvatarTurn);
    }

    [Fact]
    public void ExecuteEnemyTurn_EnemyDiesFromDoT_EventIsFullyStamped()
    {
        var avatar = CreateCombatant("Avatar", health: 1.0f);
        var enemy = CreateCombatant("Enemy", health: 1.0f);

        var engine = new BattleEngine(avatar, enemy, world: CreateStatusEffectWorld(), randomSeed: 42);
        engine.StartBattle();

        // Poison the enemy lethally AFTER the opening turn, then hand the turn back:
        // the DoT tick at the start of the enemy's next turn kills it
        enemy.Health = 0.02f;
        enemy.ActiveStatusEffects.Add(new ActiveStatusEffect
        {
            StatusEffectRef = "LethalPoison",
            RemainingTurns = 3,
            Stacks = 1,
            AppliedOnTurn = 1
        });

        var defend = engine.ExecuteAvatarDecision(new CombatAction { ActionType = ActionType.Defend });
        Assert.True(defend.Success);
        Assert.Equal(BattleState.EnemyTurn, engine.State);

        var result = engine.ExecuteEnemyTurn();

        Assert.True(result.Success);
        Assert.False(enemy.IsAlive);
        // Died-from-DoT event must be stamped: 0 health here is REAL (the enemy died),
        // and the actor/energy fields must reflect the enemy, not defaults
        Assert.False(string.IsNullOrEmpty(result.ActorName));
        Assert.False(string.IsNullOrEmpty(result.TargetName));
        Assert.Equal(enemy.Health, result.TargetHealthAfter, 3);
        Assert.Equal(enemy.Stamina, result.ActorEnergyAfter, 3);
        Assert.False(result.IsAvatarTurn);
        Assert.Equal(BattleState.Victory, engine.State);
    }

    private static Combatant CreateCombatant(string name, float health)
    {
        return new Combatant
        {
            RefName = name,
            DisplayName = name,
            Health = health,
            Stamina = 1.0f,
            Strength = 0.10f,
            Defense = 0.10f,
            Speed = 0.10f,
            Magic = 0.10f,
            Capabilities = new ItemCollection(),
            CombatProfile = new Dictionary<string, string>()
        };
    }

    private static World CreateStatusEffectWorld()
    {
        var stun = new StatusEffect
        {
            RefName = "Stun",
            DisplayName = "Stunned",
            Type = StatusEffectType.Stun,
            DurationTurns = 2,
            MaxStacks = 1,
            Cleansable = true
        };

        var silence = new StatusEffect
        {
            RefName = "Silence",
            DisplayName = "Silenced",
            Type = StatusEffectType.Silence,
            DurationTurns = 2,
            MaxStacks = 1,
            Cleansable = true
        };

        var lethalPoison = new StatusEffect
        {
            RefName = "LethalPoison",
            DisplayName = "Lethal Poison",
            Type = StatusEffectType.DamageOverTime,
            DamagePerTurn = 50, // 50% per turn — lethal against a 0.02 health target
            DurationTurns = 3,
            MaxStacks = 1,
            Cleansable = true
        };

        var world = new World
        {
            WorldTemplate = new WorldTemplate
            {
                Gameplay = new GameplayComponents
                {
                    StatusEffects = new[] { stun, silence, lethalPoison }
                }
            }
        };

        world.StatusEffectsLookup["Stun"] = stun;
        world.StatusEffectsLookup["Silence"] = silence;
        world.StatusEffectsLookup["LethalPoison"] = lethalPoison;

        return world;
    }
}
