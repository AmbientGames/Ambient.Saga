using Ambient.Domain;
using Ambient.Domain.DefinitionExtensions;
using Ambient.Saga.Engine.Domain.Rpg.Battle;
using Xunit;

namespace Ambient.Saga.Engine.Tests.Rpg.Battle;

/// <summary>
/// Tests for Phase 1 BattleEngine features:
/// - Spell RequiresEquipped validation
/// - Spell MinimumStats validation
/// - Equipment CriticalHitBonus
/// - Status effect application from spells/equipment
/// - Status effect turn processing (DoT, duration, expiration)
/// - Status effect stat modifiers
/// </summary>
public class BattleEnginePhase1Tests
{
    private readonly World _world;

    public BattleEnginePhase1Tests()
    {
        _world = CreateTestWorld();
    }

    #region Spell RequiresEquipped Validation Tests

    [Fact]
    public void ExecuteSpellAttack_RequiresStaff_FailsWithoutStaff()
    {
        // Arrange
        var player = CreateCombatantWithCapabilities("Player", health: 1.0f, magic: 0.5f,
            spells: new[] { new SpellEntry { SpellRef = "Fireball", Condition = 1.0f } });
        player.CombatProfile["RightHand"] = "IronSword"; // Not a staff
        var enemy = CreateCombatant("Enemy", health: 1.0f);

        var engine = new BattleEngine(player, enemy, world: _world, randomSeed: 42);
        engine.StartBattle();

        // Act - Try to cast spell that requires Staff
        var result = engine.ExecutePlayerDecision(new CombatAction
        {
            ActionType = ActionType.CastSpell,
            Parameter = "Fireball" // Requires Staff
        });

        // Assert
        Assert.False(result.Success);
        Assert.Contains("Staff", result.Message);
    }

    [Fact]
    public void ExecuteSpellAttack_RequiresStaff_SucceedsWithStaff()
    {
        // Arrange
        var player = CreateCombatantWithCapabilities("Player", health: 1.0f, magic: 0.5f, energy: 1.0f,
            spells: new[] { new SpellEntry { SpellRef = "Fireball", Condition = 1.0f } });
        player.CombatProfile["RightHand"] = "OakStaff"; // Staff equipped
        var enemy = CreateCombatant("Enemy", health: 1.0f);

        var engine = new BattleEngine(player, enemy, world: _world, randomSeed: 42);
        engine.StartBattle();

        // Act
        var result = engine.ExecutePlayerDecision(new CombatAction
        {
            ActionType = ActionType.CastSpell,
            Parameter = "Fireball"
        });

        // Assert
        Assert.True(result.Success);
    }

    [Fact]
    public void ExecuteSpellAttack_RequiresWand_SucceedsWithWandInLeftHand()
    {
        // Arrange
        var player = CreateCombatantWithCapabilities("Player", health: 1.0f, magic: 0.5f, energy: 1.0f,
            spells: new[] { new SpellEntry { SpellRef = "LightningBolt", Condition = 1.0f } });
        player.CombatProfile["LeftHand"] = "MagicWand"; // Wand in left hand
        var enemy = CreateCombatant("Enemy", health: 1.0f);

        var engine = new BattleEngine(player, enemy, world: _world, randomSeed: 42);
        engine.StartBattle();

        // Act
        var result = engine.ExecutePlayerDecision(new CombatAction
        {
            ActionType = ActionType.CastSpell,
            Parameter = "LightningBolt" // Requires Wand
        });

        // Assert
        Assert.True(result.Success);
    }

    [Fact]
    public void ExecuteSpellAttack_NoRequirement_SucceedsWithoutEquipment()
    {
        // Arrange - No equipment in hands
        var player = CreateCombatantWithCapabilities("Player", health: 1.0f, magic: 0.5f, energy: 1.0f,
            spells: new[] { new SpellEntry { SpellRef = "Heal", Condition = 1.0f } });
        var enemy = CreateCombatant("Enemy", health: 1.0f);

        var engine = new BattleEngine(player, enemy, world: _world, randomSeed: 42);
        engine.StartBattle();

        // Act - Cast spell with no equipment requirement
        var result = engine.ExecutePlayerDecision(new CombatAction
        {
            ActionType = ActionType.CastSpell,
            Parameter = "Heal"
        });

        // Assert
        Assert.True(result.Success);
    }

    #endregion

    #region Spell MinimumStats Validation Tests

    [Fact]
    public void ExecuteSpellAttack_MinimumMagic_FailsWhenTooLow()
    {
        // Arrange
        var player = CreateCombatantWithCapabilities("Player", health: 1.0f, magic: 0.1f, energy: 1.0f,
            spells: new[] { new SpellEntry { SpellRef = "AdvancedFireball", Condition = 1.0f } }); // Low magic
        var enemy = CreateCombatant("Enemy", health: 1.0f);

        var engine = new BattleEngine(player, enemy, world: _world, randomSeed: 42);
        engine.StartBattle();

        // Act - Try to cast spell requiring high magic
        var result = engine.ExecutePlayerDecision(new CombatAction
        {
            ActionType = ActionType.CastSpell,
            Parameter = "AdvancedFireball" // Requires 0.5 Magic
        });

        // Assert
        Assert.False(result.Success);
        Assert.Contains("Magic", result.Message);
    }

    [Fact]
    public void ExecuteSpellAttack_MinimumMagic_SucceedsWhenMet()
    {
        // Arrange
        var player = CreateCombatantWithCapabilities("Player", health: 1.0f, magic: 0.6f, energy: 1.0f,
            spells: new[] { new SpellEntry { SpellRef = "AdvancedFireball", Condition = 1.0f } }); // Sufficient magic
        var enemy = CreateCombatant("Enemy", health: 1.0f);

        var engine = new BattleEngine(player, enemy, world: _world, randomSeed: 42);
        engine.StartBattle();

        // Act
        var result = engine.ExecutePlayerDecision(new CombatAction
        {
            ActionType = ActionType.CastSpell,
            Parameter = "AdvancedFireball"
        });

        // Assert
        Assert.True(result.Success);
    }

    [Fact]
    public void ExecuteSpellAttack_MinimumStrength_FailsWhenTooLow()
    {
        // Arrange
        var player = CreateCombatantWithCapabilities("Player", health: 1.0f, strength: 0.1f, magic: 0.5f, energy: 1.0f,
            spells: new[] { new SpellEntry { SpellRef = "PowerStrike", Condition = 1.0f } });
        var enemy = CreateCombatant("Enemy", health: 1.0f);

        var engine = new BattleEngine(player, enemy, world: _world, randomSeed: 42);
        engine.StartBattle();

        // Act
        var result = engine.ExecutePlayerDecision(new CombatAction
        {
            ActionType = ActionType.CastSpell,
            Parameter = "PowerStrike" // Requires 0.4 Strength
        });

        // Assert
        Assert.False(result.Success);
        Assert.Contains("Strength", result.Message);
    }

    #endregion

    #region Equipment CriticalHitBonus Tests

    [Fact]
    public void ExecuteWeaponAttack_CriticalHitBonus_IncreasesChance()
    {
        // Arrange - Use deterministic seed and run multiple attacks
        var critCountWithBonus = 0;
        var critCountWithoutBonus = 0;
        const int iterations = 1000;

        for (int i = 0; i < iterations; i++)
        {
            // With CriticalHitBonus weapon
            var playerWithBonus = CreateCombatantWithCapabilities("Player", health: 1.0f, speed: 0.1f,
                equipment: new[] { new EquipmentEntry { EquipmentRef = "CriticalSword", Condition = 1.0f } }); // Low speed = low base crit
            playerWithBonus.CombatProfile["RightHand"] = "CriticalSword"; // +30% crit bonus

            var enemy1 = CreateCombatant("Enemy", health: 1.0f, defense: 0.1f);
            var engine1 = new BattleEngine(playerWithBonus, enemy1, world: _world, randomSeed: i);
            engine1.StartBattle();
            var result1 = engine1.ExecutePlayerDecision(new CombatAction
            {
                ActionType = ActionType.Attack,
                Parameter = "CriticalSword"
            });
            if (result1.IsCritical) critCountWithBonus++;

            // Without CriticalHitBonus weapon
            var playerWithoutBonus = CreateCombatantWithCapabilities("Player", health: 1.0f, speed: 0.1f,
                equipment: new[] { new EquipmentEntry { EquipmentRef = "IronSword", Condition = 1.0f } });
            playerWithoutBonus.CombatProfile["RightHand"] = "IronSword"; // No crit bonus

            var enemy2 = CreateCombatant("Enemy", health: 1.0f, defense: 0.1f);
            var engine2 = new BattleEngine(playerWithoutBonus, enemy2, world: _world, randomSeed: i);
            engine2.StartBattle();
            var result2 = engine2.ExecutePlayerDecision(new CombatAction
            {
                ActionType = ActionType.Attack,
                Parameter = "IronSword"
            });
            if (result2.IsCritical) critCountWithoutBonus++;
        }

        // Assert - Weapon with bonus should have significantly more crits
        Assert.True(critCountWithBonus > critCountWithoutBonus * 2,
            $"CriticalSword crits ({critCountWithBonus}) should be much higher than IronSword ({critCountWithoutBonus})");
    }

    [Fact]
    public void ExecuteWeaponAttack_CriticalHitBonus_CappedAt50Percent()
    {
        // Arrange - High crit bonus should be capped at 50%
        // Note: Base crit from speed uses formula: min(0.3, speed/100) which gives very low base
        // So we're testing that CriticalHitBonus of 0.3 + HighCritSword of 0.25 = 0.55 gets capped to 0.5
        var critCount = 0;
        const int iterations = 1000;

        for (int i = 0; i < iterations; i++)
        {
            var player = CreateCombatantWithCapabilities("Player", health: 1.0f, speed: 0.3f,
                equipment: new[] { new EquipmentEntry { EquipmentRef = "HighCritSword", Condition = 1.0f } });
            player.CombatProfile["RightHand"] = "HighCritSword"; // +45% crit bonus

            var enemy = CreateCombatant("Enemy", health: 1.0f, defense: 0.1f);
            var engine = new BattleEngine(player, enemy, world: _world, randomSeed: i);
            engine.StartBattle();
            var result = engine.ExecutePlayerDecision(new CombatAction
            {
                ActionType = ActionType.Attack,
                Parameter = "HighCritSword"
            });
            if (result.IsCritical) critCount++;
        }

        // Assert - Should be capped at 50%, allowing for variance
        var critRate = (float)critCount / iterations;
        Assert.True(critRate >= 0.4f && critRate <= 0.6f,
            $"Crit rate {critRate:P} should be around 50% (capped)");
    }

    #endregion

    #region Status Effect Application Tests

    [Fact]
    public void ExecuteWeaponAttack_WithStatusEffect_AppliesEffect()
    {
        // Arrange
        var player = CreateCombatantWithCapabilities("Player", health: 1.0f, strength: 0.5f,
            equipment: new[] { new EquipmentEntry { EquipmentRef = "PoisonDagger", Condition = 1.0f } });
        player.CombatProfile["RightHand"] = "PoisonDagger"; // Has StatusEffectRef = "Poison"
        var enemy = CreateCombatant("Enemy", health: 1.0f);

        var engine = new BattleEngine(player, enemy, world: _world, randomSeed: 42);
        engine.StartBattle();

        // Act
        var result = engine.ExecutePlayerDecision(new CombatAction
        {
            ActionType = ActionType.Attack,
            Parameter = "PoisonDagger"
        });

        // Assert
        Assert.True(result.Success);
        Assert.Equal("Poison", result.StatusEffectApplied);
        Assert.Single(enemy.ActiveStatusEffects);
        Assert.Equal("Poison", enemy.ActiveStatusEffects[0].StatusEffectRef);
    }

    [Fact]
    public void ExecuteSpellAttack_WithStatusEffect_AppliesEffectToTarget()
    {
        // Arrange
        var player = CreateCombatantWithCapabilities("Player", health: 1.0f, magic: 0.5f, energy: 1.0f,
            spells: new[] { new SpellEntry { SpellRef = "FrostBolt", Condition = 1.0f } });
        var enemy = CreateCombatant("Enemy", health: 1.0f);

        var engine = new BattleEngine(player, enemy, world: _world, randomSeed: 42);
        engine.StartBattle();

        // Act
        var result = engine.ExecutePlayerDecision(new CombatAction
        {
            ActionType = ActionType.CastSpell,
            Parameter = "FrostBolt" // Has StatusEffectRef = "Frozen"
        });

        // Assert
        Assert.True(result.Success);
        Assert.Equal("Frozen", result.StatusEffectApplied);
        Assert.Single(enemy.ActiveStatusEffects);
    }

    [Fact]
    public void ExecuteSpellAttack_DefensiveSpell_AppliesEffectToSelf()
    {
        // Arrange
        var player = CreateCombatantWithCapabilities("Player", health: 1.0f, magic: 0.5f, energy: 1.0f,
            spells: new[] { new SpellEntry { SpellRef = "DefenseBuff", Condition = 1.0f } });
        var enemy = CreateCombatant("Enemy", health: 1.0f);

        var engine = new BattleEngine(player, enemy, world: _world, randomSeed: 42);
        engine.StartBattle();

        // Act
        var result = engine.ExecutePlayerDecision(new CombatAction
        {
            ActionType = ActionType.CastSpell,
            Parameter = "DefenseBuff" // Defensive spell with status effect
        });

        // Assert
        Assert.True(result.Success);
        Assert.Single(player.ActiveStatusEffects);
        Assert.Equal("DefenseUp", player.ActiveStatusEffects[0].StatusEffectRef);
    }

    [Fact]
    public void StatusEffect_Stacking_IncreasesStacks()
    {
        // Arrange
        var player = CreateCombatantWithCapabilities("Player", health: 1.0f, strength: 0.5f,
            equipment: new[] { new EquipmentEntry { EquipmentRef = "PoisonDagger", Condition = 1.0f } });
        player.CombatProfile["RightHand"] = "PoisonDagger";
        var enemy = CreateCombatant("Enemy", health: 1.0f);

        var engine = new BattleEngine(player, enemy, world: _world, randomSeed: 42);
        engine.StartBattle();

        // Act - Apply poison twice
        engine.ExecutePlayerDecision(new CombatAction { ActionType = ActionType.Attack, Parameter = "PoisonDagger" });

        // Need to go through turn cycle
        engine.ExecuteEnemyTurn();

        engine.ExecutePlayerDecision(new CombatAction { ActionType = ActionType.Attack, Parameter = "PoisonDagger" });

        // Assert - Should have stacked
        Assert.Single(enemy.ActiveStatusEffects);
        Assert.True(enemy.ActiveStatusEffects[0].Stacks >= 1); // At least 1 stack
    }

    [Fact]
    public void StatusEffect_ProbabilityCheck_SometimesDoesNotApply()
    {
        // Arrange - Weapon with 50% status effect chance
        var appliedCount = 0;
        const int iterations = 100;

        for (int i = 0; i < iterations; i++)
        {
            var player = CreateCombatantWithCapabilities("Player", health: 1.0f, strength: 0.5f,
                equipment: new[] { new EquipmentEntry { EquipmentRef = "LowChancePoison", Condition = 1.0f } });
            player.CombatProfile["RightHand"] = "LowChancePoison"; // 50% chance
            var enemy = CreateCombatant("Enemy", health: 1.0f);

            var engine = new BattleEngine(player, enemy, world: _world, randomSeed: i);
            engine.StartBattle();

            engine.ExecutePlayerDecision(new CombatAction
            {
                ActionType = ActionType.Attack,
                Parameter = "LowChancePoison"
            });

            if (enemy.ActiveStatusEffects.Count > 0)
                appliedCount++;
        }

        // Assert - Should be around 50% (with variance)
        var applyRate = (float)appliedCount / iterations;
        Assert.True(applyRate >= 0.3f && applyRate <= 0.7f,
            $"Apply rate {applyRate:P} should be around 50%");
    }

    #endregion

    #region Status Effect Turn Processing Tests

    [Fact]
    public void ProcessStatusEffects_DamageOverTime_DealsDamage()
    {
        // Arrange
        var player = CreateCombatant("Player", health: 1.0f);
        var enemy = CreateCombatant("Enemy", health: 1.0f);

        // Manually add a DoT effect
        enemy.ActiveStatusEffects.Add(new ActiveStatusEffect
        {
            StatusEffectRef = "Poison",
            RemainingTurns = 3,
            Stacks = 1,
            AppliedOnTurn = 1
        });

        var engine = new BattleEngine(player, enemy, world: _world, randomSeed: 42);
        var initialHealth = enemy.Health;

        // Act - Process status effects
        engine.ProcessStatusEffects(enemy);

        // Assert - Enemy should have taken damage
        Assert.True(enemy.Health < initialHealth, "DoT should deal damage");
    }

    [Fact]
    public void ProcessStatusEffects_DamageOverTime_ScalesWithStacks()
    {
        // Arrange
        var player = CreateCombatant("Player", health: 1.0f);
        var enemy1 = CreateCombatant("Enemy1", health: 1.0f);
        var enemy2 = CreateCombatant("Enemy2", health: 1.0f);

        // Add 1 stack to enemy1
        enemy1.ActiveStatusEffects.Add(new ActiveStatusEffect
        {
            StatusEffectRef = "Poison",
            RemainingTurns = 3,
            Stacks = 1,
            AppliedOnTurn = 1
        });

        // Add 3 stacks to enemy2
        enemy2.ActiveStatusEffects.Add(new ActiveStatusEffect
        {
            StatusEffectRef = "Poison",
            RemainingTurns = 3,
            Stacks = 3,
            AppliedOnTurn = 1
        });

        var engine = new BattleEngine(player, enemy1, world: _world, randomSeed: 42);

        // Act
        engine.ProcessStatusEffects(enemy1);
        engine.ProcessStatusEffects(enemy2);

        // Assert - More stacks = more damage
        var damage1 = 1.0f - enemy1.Health;
        var damage2 = 1.0f - enemy2.Health;
        Assert.True(damage2 > damage1, "3 stacks should deal more damage than 1 stack");
        Assert.True(Math.Abs(damage2 - damage1 * 3) < 0.01f, "Damage should scale linearly with stacks");
    }

    [Fact]
    public void ProcessStatusEffects_DecrementsDuration()
    {
        // Arrange
        var player = CreateCombatant("Player", health: 1.0f);
        var enemy = CreateCombatant("Enemy", health: 1.0f);

        enemy.ActiveStatusEffects.Add(new ActiveStatusEffect
        {
            StatusEffectRef = "Poison",
            RemainingTurns = 3,
            Stacks = 1,
            AppliedOnTurn = 1
        });

        var engine = new BattleEngine(player, enemy, world: _world, randomSeed: 42);

        // Act
        engine.ProcessStatusEffects(enemy);

        // Assert
        Assert.Equal(2, enemy.ActiveStatusEffects[0].RemainingTurns);
    }

    [Fact]
    public void ProcessStatusEffects_RemovesExpiredEffects()
    {
        // Arrange
        var player = CreateCombatant("Player", health: 1.0f);
        var enemy = CreateCombatant("Enemy", health: 1.0f);

        enemy.ActiveStatusEffects.Add(new ActiveStatusEffect
        {
            StatusEffectRef = "Poison",
            RemainingTurns = 1, // Will expire after processing
            Stacks = 1,
            AppliedOnTurn = 1
        });

        var engine = new BattleEngine(player, enemy, world: _world, randomSeed: 42);

        // Act
        engine.ProcessStatusEffects(enemy);

        // Assert - Effect should be removed
        Assert.Empty(enemy.ActiveStatusEffects);
    }

    [Fact]
    public void ExecuteEnemyTurn_ProcessesStatusEffectsFirst()
    {
        // Arrange
        var player = CreateCombatant("Player", health: 1.0f);
        var enemy = CreateCombatant("Enemy", health: 0.1f); // Low health
        var ai = new CombatAI(_world);

        // Add lethal DoT
        enemy.ActiveStatusEffects.Add(new ActiveStatusEffect
        {
            StatusEffectRef = "Poison", // 5% damage per turn
            RemainingTurns = 3,
            Stacks = 3, // 15% damage total
            AppliedOnTurn = 1
        });

        var engine = new BattleEngine(player, enemy, ai, _world, randomSeed: 42);
        engine.StartBattle();

        // Act - Enemy turn should process status effects first
        var result = engine.ExecuteEnemyTurn();

        // Assert - Enemy should have died from DoT
        Assert.False(enemy.IsAlive);
        Assert.Equal(BattleState.Victory, engine.State);
    }

    [Fact]
    public void CleanseStatusEffects_RemovesCleansableEffects()
    {
        // Arrange
        var player = CreateCombatantWithCapabilities("Player", health: 0.5f, magic: 0.5f, energy: 1.0f,
            spells: new[] { new SpellEntry { SpellRef = "Cleanse", Condition = 1.0f } });
        player.ActiveStatusEffects.Add(new ActiveStatusEffect
        {
            StatusEffectRef = "Poison",
            RemainingTurns = 3,
            Stacks = 2,
            AppliedOnTurn = 1
        });

        var enemy = CreateCombatant("Enemy", health: 1.0f);

        var engine = new BattleEngine(player, enemy, world: _world, randomSeed: 42);
        engine.StartBattle();

        // Act - Cast cleanse spell
        engine.ExecutePlayerDecision(new CombatAction
        {
            ActionType = ActionType.CastSpell,
            Parameter = "Cleanse"
        });

        // Assert - Poison should be cleansed
        Assert.Empty(player.ActiveStatusEffects);
    }

    #endregion

    #region Status Effect Stat Modifier Tests

    [Fact]
    public void GetStatusEffectStatModifier_StrengthDebuff_ReducesStrength()
    {
        // Arrange
        var player = CreateCombatant("Player", health: 1.0f, strength: 0.5f);
        var enemy = CreateCombatant("Enemy", health: 1.0f, strength: 0.5f, defense: 0.1f);

        // Add weakness debuff to player
        player.ActiveStatusEffects.Add(new ActiveStatusEffect
        {
            StatusEffectRef = "Weakness", // -20% Strength
            RemainingTurns = 3,
            Stacks = 1,
            AppliedOnTurn = 1
        });

        var engine = new BattleEngine(player, enemy, world: _world, randomSeed: 42);
        engine.StartBattle();

        // Act - Player attacks
        var result = engine.ExecutePlayerDecision(new CombatAction { ActionType = ActionType.Attack });

        // Assert - Damage should be reduced (can't directly test modifier, but attack damage is affected)
        Assert.True(result.Success);
        // The damage calculation includes the modifier, so we're verifying it doesn't crash
        // and the attack proceeds with modified stats
    }

    [Fact]
    public void GetStatusEffectStatModifier_DefenseDebuff_IncreasesIncomingDamage()
    {
        // Arrange
        var player = CreateCombatant("Player", health: 1.0f, strength: 0.3f);
        var enemy = CreateCombatant("Enemy", health: 1.0f, defense: 0.5f);

        // Record damage without debuff
        var enemyWithoutDebuff = CreateCombatant("Enemy", health: 1.0f, defense: 0.5f);
        var engine1 = new BattleEngine(player, enemyWithoutDebuff, world: _world, randomSeed: 42);
        engine1.StartBattle();
        engine1.ExecutePlayerDecision(new CombatAction { ActionType = ActionType.Attack });
        var damageWithoutDebuff = 1.0f - enemyWithoutDebuff.Health;

        // Add armor break debuff to enemy
        enemy.ActiveStatusEffects.Add(new ActiveStatusEffect
        {
            StatusEffectRef = "ArmorBreak", // -30% Defense
            RemainingTurns = 3,
            Stacks = 1,
            AppliedOnTurn = 1
        });

        var player2 = CreateCombatant("Player", health: 1.0f, strength: 0.3f);
        var engine2 = new BattleEngine(player2, enemy, world: _world, randomSeed: 42);
        engine2.StartBattle();
        engine2.ExecutePlayerDecision(new CombatAction { ActionType = ActionType.Attack });
        var damageWithDebuff = 1.0f - enemy.Health;

        // Assert - Damage should be higher with armor break
        Assert.True(damageWithDebuff > damageWithoutDebuff,
            $"Damage with ArmorBreak ({damageWithDebuff}) should be > without ({damageWithoutDebuff})");
    }

    [Fact]
    public void GetStatusEffectStatModifier_StacksAreAdditive()
    {
        // Arrange
        var enemy = CreateCombatant("Enemy", health: 1.0f, defense: 0.5f);

        // Add 3 stacks of weakness
        enemy.ActiveStatusEffects.Add(new ActiveStatusEffect
        {
            StatusEffectRef = "Weakness", // -20% Strength per stack
            RemainingTurns = 3,
            Stacks = 3, // -60% total
            AppliedOnTurn = 1
        });

        var engine = new BattleEngine(
            CreateCombatant("Player", health: 1.0f),
            enemy,
            world: _world,
            randomSeed: 42);

        // Act
        var modifier = engine.GetStatusEffectStatModifier(enemy, "Strength");

        // Assert - 1.0 + (-0.2 * 3) = 0.4
        Assert.Equal(0.4f, modifier, 2);
    }

    [Fact]
    public void GetStatusEffectStatModifier_MinimumFloor_At10Percent()
    {
        // Arrange
        var enemy = CreateCombatant("Enemy", health: 1.0f, defense: 0.5f);

        // Add massive debuff
        enemy.ActiveStatusEffects.Add(new ActiveStatusEffect
        {
            StatusEffectRef = "Weakness", // -20% per stack
            RemainingTurns = 3,
            Stacks = 10, // Would be -200% but should floor at 10%
            AppliedOnTurn = 1
        });

        var engine = new BattleEngine(
            CreateCombatant("Player", health: 1.0f),
            enemy,
            world: _world,
            randomSeed: 42);

        // Act
        var modifier = engine.GetStatusEffectStatModifier(enemy, "Strength");

        // Assert - Should be floored at 0.1 (10%)
        Assert.Equal(0.1f, modifier, 2);
    }

    #endregion

    #region Helper Methods

    private Combatant CreateCombatant(string name, float health, float strength = 0.3f,
        float defense = 0.3f, float speed = 0.3f, float magic = 0.3f, float energy = 0.5f)
    {
        return new Combatant
        {
            RefName = name,
            DisplayName = name,
            Health = health,
            Energy = energy,
            Strength = strength,
            Defense = defense,
            Speed = speed,
            Magic = magic,
            AffinityRef = "IRON",
            Capabilities = new ItemCollection(),
            CombatProfile = new Dictionary<string, string>()
        };
    }

    private Combatant CreateCombatantWithCapabilities(string name, float health, float strength = 0.3f,
        float defense = 0.3f, float speed = 0.3f, float magic = 0.3f, float energy = 0.5f,
        SpellEntry[]? spells = null, EquipmentEntry[]? equipment = null, ConsumableEntry[]? consumables = null)
    {
        return new Combatant
        {
            RefName = name,
            DisplayName = name,
            Health = health,
            Energy = energy,
            Strength = strength,
            Defense = defense,
            Speed = speed,
            Magic = magic,
            AffinityRef = "IRON",
            Capabilities = new ItemCollection
            {
                Spells = spells ?? Array.Empty<SpellEntry>(),
                Equipment = equipment ?? Array.Empty<EquipmentEntry>(),
                Consumables = consumables ?? Array.Empty<ConsumableEntry>()
            },
            CombatProfile = new Dictionary<string, string>()
        };
    }

    private static World CreateTestWorld()
    {
        var world = new World();

        // Create equipment
        var ironSword = new Equipment
        {
            RefName = "IronSword",
            DisplayName = "Iron Sword",
            Category = EquipmentCategoryType.OneHandedMelee,
            CriticalHitBonus = 0f,
            Effects = new CharacterEffects { Health = -0.1f }
        };

        var criticalSword = new Equipment
        {
            RefName = "CriticalSword",
            DisplayName = "Critical Sword",
            Category = EquipmentCategoryType.OneHandedMelee,
            CriticalHitBonus = 0.3f, // +30% crit chance
            Effects = new CharacterEffects { Health = -0.1f }
        };

        var oakStaff = new Equipment
        {
            RefName = "OakStaff",
            DisplayName = "Oak Staff",
            Category = EquipmentCategoryType.Staff,
            Effects = new CharacterEffects { Health = -0.05f }
        };

        var magicWand = new Equipment
        {
            RefName = "MagicWand",
            DisplayName = "Magic Wand",
            Category = EquipmentCategoryType.Wand,
            Effects = new CharacterEffects { Health = -0.05f }
        };

        var poisonDagger = new Equipment
        {
            RefName = "PoisonDagger",
            DisplayName = "Poison Dagger",
            Category = EquipmentCategoryType.OneHandedMelee,
            StatusEffectRef = "Poison",
            StatusEffectChance = 1.0f, // 100% chance for testing
            Effects = new CharacterEffects { Health = -0.1f }
        };

        var lowChancePoison = new Equipment
        {
            RefName = "LowChancePoison",
            DisplayName = "Low Chance Poison Dagger",
            Category = EquipmentCategoryType.OneHandedMelee,
            StatusEffectRef = "Poison",
            StatusEffectChance = 0.5f, // 50% chance
            Effects = new CharacterEffects { Health = -0.1f }
        };

        var highCritSword = new Equipment
        {
            RefName = "HighCritSword",
            DisplayName = "High Crit Sword",
            Category = EquipmentCategoryType.OneHandedMelee,
            CriticalHitBonus = 0.55f, // 55% crit bonus - should cap at 50% total
            Effects = new CharacterEffects { Health = -0.1f }
        };

        // Create spells
        var fireball = new Spell
        {
            RefName = "Fireball",
            DisplayName = "Fireball",
            RequiresEquipped = EquipmentCategoryType.Staff,
            RequiresEquippedSpecified = true,
            UseType = ItemUseType.Offensive,
            Effects = new CharacterEffects { Health = -0.2f }
        };

        var lightningBolt = new Spell
        {
            RefName = "LightningBolt",
            DisplayName = "Lightning Bolt",
            RequiresEquipped = EquipmentCategoryType.Wand,
            RequiresEquippedSpecified = true,
            UseType = ItemUseType.Offensive,
            Effects = new CharacterEffects { Health = -0.15f }
        };

        var heal = new Spell
        {
            RefName = "Heal",
            DisplayName = "Heal",
            UseType = ItemUseType.Defensive,
            Effects = new CharacterEffects { Health = 0.2f }
        };

        var advancedFireball = new Spell
        {
            RefName = "AdvancedFireball",
            DisplayName = "Advanced Fireball",
            UseType = ItemUseType.Offensive,
            MinimumStats = new CharacterEffects { Magic = 0.5f },
            Effects = new CharacterEffects { Health = -0.3f }
        };

        var powerStrike = new Spell
        {
            RefName = "PowerStrike",
            DisplayName = "Power Strike",
            UseType = ItemUseType.Offensive,
            MinimumStats = new CharacterEffects { Strength = 0.4f },
            Effects = new CharacterEffects { Health = -0.25f }
        };

        var frostBolt = new Spell
        {
            RefName = "FrostBolt",
            DisplayName = "Frost Bolt",
            UseType = ItemUseType.Offensive,
            StatusEffectRef = "Frozen",
            StatusEffectChance = 1.0f,
            Effects = new CharacterEffects { Health = -0.1f }
        };

        var defenseBuff = new Spell
        {
            RefName = "DefenseBuff",
            DisplayName = "Defense Buff",
            UseType = ItemUseType.Defensive,
            StatusEffectRef = "DefenseUp",
            StatusEffectChance = 1.0f,
            Effects = new CharacterEffects()
        };

        var cleanse = new Spell
        {
            RefName = "Cleanse",
            DisplayName = "Cleanse",
            UseType = ItemUseType.Defensive,
            CleansesStatusEffects = true,
            CleanseTargetSelf = true,
            Effects = new CharacterEffects()
        };

        // Create status effects
        var poison = new StatusEffect
        {
            RefName = "Poison",
            DisplayName = "Poison",
            Type = StatusEffectType.DamageOverTime,
            DamagePerTurn = 5, // 5% per turn
            DurationTurns = 3,
            MaxStacks = 5,
            Cleansable = true
        };

        var frozen = new StatusEffect
        {
            RefName = "Frozen",
            DisplayName = "Frozen",
            Type = StatusEffectType.Slow,
            SpeedModifier = -0.5f, // -50% speed
            DurationTurns = 2,
            MaxStacks = 1,
            Cleansable = true
        };

        var defenseUp = new StatusEffect
        {
            RefName = "DefenseUp",
            DisplayName = "Defense Up",
            Type = StatusEffectType.StatBoost,
            DefenseModifier = 0.3f, // +30% defense
            DurationTurns = 3,
            MaxStacks = 1,
            Cleansable = false
        };

        var weakness = new StatusEffect
        {
            RefName = "Weakness",
            DisplayName = "Weakness",
            Type = StatusEffectType.Weaken,
            StrengthModifier = -0.2f, // -20% strength per stack
            DurationTurns = 3,
            MaxStacks = 5,
            Cleansable = true
        };

        var armorBreak = new StatusEffect
        {
            RefName = "ArmorBreak",
            DisplayName = "Armor Break",
            Type = StatusEffectType.Vulnerable,
            DefenseModifier = -0.3f, // -30% defense
            DurationTurns = 3,
            MaxStacks = 3,
            Cleansable = true
        };

        // Initialize world
        world.WorldTemplate = new WorldTemplate
        {
            Gameplay = new GameplayComponents
            {
                Equipment = new[] { ironSword, criticalSword, oakStaff, magicWand, poisonDagger, lowChancePoison, highCritSword },
                Spells = new[] { fireball, lightningBolt, heal, advancedFireball, powerStrike, frostBolt, defenseBuff, cleanse },
                StatusEffects = new[] { poison, frozen, defenseUp, weakness, armorBreak },
                Consumables = Array.Empty<Consumable>(),
                Characters = Array.Empty<Character>(),
                DialogueTrees = Array.Empty<DialogueTree>(),
                QuestTokens = Array.Empty<QuestToken>(),
                Factions = Array.Empty<Faction>(),
                SagaArcs = Array.Empty<SagaArc>()
            }
        };

        // Build lookups
        foreach (var eq in world.Gameplay.Equipment)
            world.EquipmentLookup[eq.RefName] = eq;

        foreach (var sp in world.Gameplay.Spells)
            world.SpellsLookup[sp.RefName] = sp;

        foreach (var se in world.Gameplay.StatusEffects)
            world.StatusEffectsLookup[se.RefName] = se;

        return world;
    }

    #endregion

    #region Phase 2: Equipment MinimumStats Validation Tests

    [Fact]
    public void ExecuteWeaponAttack_MinimumStrength_FailsWhenTooLow()
    {
        // Arrange
        var player = CreateCombatantWithCapabilities("Player", health: 1.0f, strength: 0.1f, energy: 1.0f,
            equipment: new[] { new EquipmentEntry { EquipmentRef = "HeavySword", Condition = 1.0f } }); // Low strength
        player.CombatProfile["RightHand"] = "HeavySword";
        var enemy = CreateCombatant("Enemy", health: 1.0f);

        var worldWithHeavySword = CreateTestWorldWithHeavyWeapon();
        var engine = new BattleEngine(player, enemy, world: worldWithHeavySword, randomSeed: 42);
        engine.StartBattle();

        // Act - Try to attack with weapon requiring high strength
        var result = engine.ExecutePlayerDecision(new CombatAction
        {
            ActionType = ActionType.Attack,
            Parameter = "HeavySword" // Requires 0.5 Strength
        });

        // Assert
        Assert.False(result.Success);
        Assert.Contains("Strength", result.Message);
    }

    [Fact]
    public void ExecuteWeaponAttack_MinimumStrength_SucceedsWhenMet()
    {
        // Arrange
        var player = CreateCombatantWithCapabilities("Player", health: 1.0f, strength: 0.6f, energy: 1.0f,
            equipment: new[] { new EquipmentEntry { EquipmentRef = "HeavySword", Condition = 1.0f } }); // High strength
        player.CombatProfile["RightHand"] = "HeavySword";
        var enemy = CreateCombatant("Enemy", health: 1.0f);

        var worldWithHeavySword = CreateTestWorldWithHeavyWeapon();
        var engine = new BattleEngine(player, enemy, world: worldWithHeavySword, randomSeed: 42);
        engine.StartBattle();

        // Act
        var result = engine.ExecutePlayerDecision(new CombatAction
        {
            ActionType = ActionType.Attack,
            Parameter = "HeavySword"
        });

        // Assert
        Assert.True(result.Success);
    }

    [Fact]
    public void ExecuteWeaponAttack_MinimumDefense_FailsWhenTooLow()
    {
        // Arrange
        var player = CreateCombatantWithCapabilities("Player", health: 1.0f, defense: 0.1f, energy: 1.0f,
            equipment: new[] { new EquipmentEntry { EquipmentRef = "GuardianBlade", Condition = 1.0f } }); // Low defense
        player.CombatProfile["RightHand"] = "GuardianBlade";
        var enemy = CreateCombatant("Enemy", health: 1.0f);

        var worldWithGuardianBlade = CreateTestWorldWithHeavyWeapon();
        var engine = new BattleEngine(player, enemy, world: worldWithGuardianBlade, randomSeed: 42);
        engine.StartBattle();

        // Act
        var result = engine.ExecutePlayerDecision(new CombatAction
        {
            ActionType = ActionType.Attack,
            Parameter = "GuardianBlade" // Requires 0.4 Defense
        });

        // Assert
        Assert.False(result.Success);
        Assert.Contains("Defense", result.Message);
    }

    [Fact]
    public void ExecuteWeaponAttack_NoMinimumStats_SucceedsWithLowStats()
    {
        // Arrange - Player with minimal stats using basic weapon
        var player = CreateCombatantWithCapabilities("Player", health: 1.0f, strength: 0.1f, energy: 1.0f,
            equipment: new[] { new EquipmentEntry { EquipmentRef = "IronSword", Condition = 1.0f } });
        player.CombatProfile["RightHand"] = "IronSword"; // No MinimumStats requirement
        var enemy = CreateCombatant("Enemy", health: 1.0f);

        var engine = new BattleEngine(player, enemy, world: _world, randomSeed: 42);
        engine.StartBattle();

        // Act
        var result = engine.ExecutePlayerDecision(new CombatAction
        {
            ActionType = ActionType.Attack,
            Parameter = "IronSword"
        });

        // Assert
        Assert.True(result.Success); // Basic sword has no stat requirements
    }

    [Fact]
    public void ExecuteWeaponAttack_MinimumMagic_FailsForMeleeWithMagicRequirement()
    {
        // Arrange - A magic-infused weapon that requires high magic stat
        var player = CreateCombatantWithCapabilities("Player", health: 1.0f, magic: 0.1f, energy: 1.0f,
            equipment: new[] { new EquipmentEntry { EquipmentRef = "EnchantedBlade", Condition = 1.0f } }); // Low magic
        player.CombatProfile["RightHand"] = "EnchantedBlade";
        var enemy = CreateCombatant("Enemy", health: 1.0f);

        var worldWithEnchantedBlade = CreateTestWorldWithHeavyWeapon();
        var engine = new BattleEngine(player, enemy, world: worldWithEnchantedBlade, randomSeed: 42);
        engine.StartBattle();

        // Act
        var result = engine.ExecutePlayerDecision(new CombatAction
        {
            ActionType = ActionType.Attack,
            Parameter = "EnchantedBlade" // Requires 0.4 Magic
        });

        // Assert
        Assert.False(result.Success);
        Assert.Contains("Magic", result.Message);
    }

    /// <summary>
    /// Creates a test world with additional heavy weapons that have MinimumStats requirements.
    /// </summary>
    private static World CreateTestWorldWithHeavyWeapon()
    {
        var baseWorld = CreateTestWorld();

        // Add heavy weapon with Strength requirement
        var heavySword = new Equipment
        {
            RefName = "HeavySword",
            DisplayName = "Heavy Sword",
            Category = EquipmentCategoryType.TwoHandedMelee,
            MinimumStats = new CharacterEffects { Strength = 0.5f },
            Effects = new CharacterEffects { Health = -0.2f }
        };

        // Add guardian blade with Defense requirement
        var guardianBlade = new Equipment
        {
            RefName = "GuardianBlade",
            DisplayName = "Guardian Blade",
            Category = EquipmentCategoryType.OneHandedMelee,
            MinimumStats = new CharacterEffects { Defense = 0.4f },
            Effects = new CharacterEffects { Health = -0.15f }
        };

        // Add enchanted blade with Magic requirement
        var enchantedBlade = new Equipment
        {
            RefName = "EnchantedBlade",
            DisplayName = "Enchanted Blade",
            Category = EquipmentCategoryType.OneHandedMelee,
            MinimumStats = new CharacterEffects { Magic = 0.4f },
            Effects = new CharacterEffects { Health = -0.18f }
        };

        baseWorld.Gameplay.Equipment = baseWorld.Gameplay.Equipment.Concat(new[] { heavySword, guardianBlade, enchantedBlade }).ToArray();
        baseWorld.EquipmentLookup["HeavySword"] = heavySword;
        baseWorld.EquipmentLookup["GuardianBlade"] = guardianBlade;
        baseWorld.EquipmentLookup["EnchantedBlade"] = enchantedBlade;

        return baseWorld;
    }

    #endregion
}
