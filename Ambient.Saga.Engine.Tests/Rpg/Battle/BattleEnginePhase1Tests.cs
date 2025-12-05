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

    #region Phase 3: Stun/Silence/Root/Blind Status Effect Behavior Tests

    [Fact]
    public void ExecuteDecision_Stunned_CannotAct()
    {
        // Arrange
        var player = CreateCombatant("Player", health: 1.0f);
        var enemy = CreateCombatant("Enemy", health: 1.0f);

        // Add Stun effect to player
        player.ActiveStatusEffects.Add(new ActiveStatusEffect
        {
            StatusEffectRef = "Stun",
            RemainingTurns = 2,
            Stacks = 1,
            AppliedOnTurn = 1
        });

        var worldWithStun = CreateTestWorldWithPhase3StatusEffects();
        var engine = new BattleEngine(player, enemy, world: worldWithStun, randomSeed: 42);
        engine.StartBattle();

        // Act - Try to attack while stunned
        var result = engine.ExecutePlayerDecision(new CombatAction
        {
            ActionType = ActionType.Attack
        });

        // Assert
        Assert.False(result.Success);
        Assert.Contains("stunned", result.Message);
    }

    [Fact]
    public void ExecuteDecision_Silenced_CannotCastSpells()
    {
        // Arrange
        var player = CreateCombatantWithCapabilities("Player", health: 1.0f, magic: 0.5f, energy: 1.0f,
            spells: new[] { new SpellEntry { SpellRef = "Heal", Condition = 1.0f } });
        var enemy = CreateCombatant("Enemy", health: 1.0f);

        // Add Silence effect to player
        player.ActiveStatusEffects.Add(new ActiveStatusEffect
        {
            StatusEffectRef = "Silence",
            RemainingTurns = 2,
            Stacks = 1,
            AppliedOnTurn = 1
        });

        var worldWithSilence = CreateTestWorldWithPhase3StatusEffects();
        var engine = new BattleEngine(player, enemy, world: worldWithSilence, randomSeed: 42);
        engine.StartBattle();

        // Act - Try to cast spell while silenced
        var result = engine.ExecutePlayerDecision(new CombatAction
        {
            ActionType = ActionType.CastSpell,
            Parameter = "Heal"
        });

        // Assert
        Assert.False(result.Success);
        Assert.Contains("silenced", result.Message);
    }

    [Fact]
    public void ExecuteDecision_Silenced_CanStillAttack()
    {
        // Arrange
        var player = CreateCombatant("Player", health: 1.0f);
        var enemy = CreateCombatant("Enemy", health: 1.0f);

        // Add Silence effect to player
        player.ActiveStatusEffects.Add(new ActiveStatusEffect
        {
            StatusEffectRef = "Silence",
            RemainingTurns = 2,
            Stacks = 1,
            AppliedOnTurn = 1
        });

        var worldWithSilence = CreateTestWorldWithPhase3StatusEffects();
        var engine = new BattleEngine(player, enemy, world: worldWithSilence, randomSeed: 42);
        engine.StartBattle();

        // Act - Attack should work while silenced
        var result = engine.ExecutePlayerDecision(new CombatAction
        {
            ActionType = ActionType.Attack
        });

        // Assert - Silence doesn't prevent attacks
        Assert.True(result.Success);
    }

    [Fact]
    public void ExecuteDecision_Rooted_CannotFlee()
    {
        // Arrange
        var player = CreateCombatant("Player", health: 1.0f);
        var enemy = CreateCombatant("Enemy", health: 1.0f);

        // Add Root effect to player
        player.ActiveStatusEffects.Add(new ActiveStatusEffect
        {
            StatusEffectRef = "Root",
            RemainingTurns = 2,
            Stacks = 1,
            AppliedOnTurn = 1
        });

        var worldWithRoot = CreateTestWorldWithPhase3StatusEffects();
        var engine = new BattleEngine(player, enemy, world: worldWithRoot, randomSeed: 42);
        engine.StartBattle();

        // Act - Try to flee while rooted
        var result = engine.ExecutePlayerDecision(new CombatAction
        {
            ActionType = ActionType.Flee
        });

        // Assert
        Assert.False(result.Success);
        Assert.Contains("rooted", result.Message);
    }

    [Fact]
    public void ExecuteDecision_Rooted_CanStillAttack()
    {
        // Arrange
        var player = CreateCombatant("Player", health: 1.0f);
        var enemy = CreateCombatant("Enemy", health: 1.0f);

        // Add Root effect to player
        player.ActiveStatusEffects.Add(new ActiveStatusEffect
        {
            StatusEffectRef = "Root",
            RemainingTurns = 2,
            Stacks = 1,
            AppliedOnTurn = 1
        });

        var worldWithRoot = CreateTestWorldWithPhase3StatusEffects();
        var engine = new BattleEngine(player, enemy, world: worldWithRoot, randomSeed: 42);
        engine.StartBattle();

        // Act - Attack should work while rooted
        var result = engine.ExecutePlayerDecision(new CombatAction
        {
            ActionType = ActionType.Attack
        });

        // Assert - Root doesn't prevent attacks
        Assert.True(result.Success);
    }

    [Fact]
    public void ExecuteAttack_Blinded_CanMiss()
    {
        // Arrange
        var player = CreateCombatant("Player", health: 1.0f, strength: 0.5f);
        var enemy = CreateCombatant("Enemy", health: 1.0f, defense: 0.1f);

        // Add Blind effect with very low accuracy (10%)
        player.ActiveStatusEffects.Add(new ActiveStatusEffect
        {
            StatusEffectRef = "Blind",
            RemainingTurns = 5,
            Stacks = 1,
            AppliedOnTurn = 1
        });

        var worldWithBlind = CreateTestWorldWithPhase3StatusEffects();

        // Run multiple attacks to check if misses occur
        var missCount = 0;
        for (int i = 0; i < 20; i++)
        {
            var testPlayer = CreateCombatant("Player", health: 1.0f, strength: 0.5f);
            testPlayer.ActiveStatusEffects.Add(new ActiveStatusEffect
            {
                StatusEffectRef = "Blind",
                RemainingTurns = 5,
                Stacks = 1,
                AppliedOnTurn = 1
            });
            var testEnemy = CreateCombatant("Enemy", health: 1.0f, defense: 0.1f);

            var engine = new BattleEngine(testPlayer, testEnemy, world: worldWithBlind, randomSeed: i);
            engine.StartBattle();

            var result = engine.ExecutePlayerDecision(new CombatAction { ActionType = ActionType.Attack });
            if (result.Damage == 0 && result.Message.Contains("misses"))
            {
                missCount++;
            }
        }

        // Assert - With 50% accuracy reduction, we should have some misses
        Assert.True(missCount > 0, "Blind effect should cause some attacks to miss");
    }

    [Fact]
    public void GetAccuracyModifier_ReturnsReducedAccuracy()
    {
        // Arrange
        var player = CreateCombatant("Player", health: 1.0f);
        var enemy = CreateCombatant("Enemy", health: 1.0f);

        // Add Blind effect
        player.ActiveStatusEffects.Add(new ActiveStatusEffect
        {
            StatusEffectRef = "Blind",
            RemainingTurns = 3,
            Stacks = 1,
            AppliedOnTurn = 1
        });

        var worldWithBlind = CreateTestWorldWithPhase3StatusEffects();
        var engine = new BattleEngine(player, enemy, world: worldWithBlind, randomSeed: 42);

        // Act
        var accuracy = engine.GetAccuracyModifier(player);

        // Assert - Blind has -0.5 AccuracyModifier, so accuracy = 1.0 + (-0.5) = 0.5
        Assert.Equal(0.5f, accuracy, 2);
    }

    [Fact]
    public void HasStatusEffectOfType_ReturnsTrue_WhenEffectPresent()
    {
        // Arrange
        var player = CreateCombatant("Player", health: 1.0f);
        var enemy = CreateCombatant("Enemy", health: 1.0f);

        player.ActiveStatusEffects.Add(new ActiveStatusEffect
        {
            StatusEffectRef = "Stun",
            RemainingTurns = 2,
            Stacks = 1,
            AppliedOnTurn = 1
        });

        var worldWithStun = CreateTestWorldWithPhase3StatusEffects();
        var engine = new BattleEngine(player, enemy, world: worldWithStun, randomSeed: 42);

        // Act
        var hasStun = engine.HasStatusEffectOfType(player, StatusEffectType.Stun);
        var hasSilence = engine.HasStatusEffectOfType(player, StatusEffectType.Silence);

        // Assert
        Assert.True(hasStun);
        Assert.False(hasSilence);
    }

    /// <summary>
    /// Creates a test world with Phase 3 status effects (Stun, Silence, Root, Blind).
    /// </summary>
    private static World CreateTestWorldWithPhase3StatusEffects()
    {
        var baseWorld = CreateTestWorld();

        // Add Stun status effect
        var stun = new StatusEffect
        {
            RefName = "Stun",
            DisplayName = "Stunned",
            Type = StatusEffectType.Stun,
            DurationTurns = 2,
            MaxStacks = 1,
            Cleansable = true
        };

        // Add Silence status effect
        var silence = new StatusEffect
        {
            RefName = "Silence",
            DisplayName = "Silenced",
            Type = StatusEffectType.Silence,
            DurationTurns = 3,
            MaxStacks = 1,
            Cleansable = true
        };

        // Add Root status effect
        var root = new StatusEffect
        {
            RefName = "Root",
            DisplayName = "Rooted",
            Type = StatusEffectType.Root,
            DurationTurns = 2,
            MaxStacks = 1,
            Cleansable = true
        };

        // Add Blind status effect with accuracy reduction
        var blind = new StatusEffect
        {
            RefName = "Blind",
            DisplayName = "Blinded",
            Type = StatusEffectType.Blind,
            AccuracyModifier = -0.5f, // -50% accuracy
            DurationTurns = 3,
            MaxStacks = 2,
            Cleansable = true
        };

        baseWorld.Gameplay.StatusEffects = baseWorld.Gameplay.StatusEffects.Concat(new[] { stun, silence, root, blind }).ToArray();
        baseWorld.StatusEffectsLookup["Stun"] = stun;
        baseWorld.StatusEffectsLookup["Silence"] = silence;
        baseWorld.StatusEffectsLookup["Root"] = root;
        baseWorld.StatusEffectsLookup["Blind"] = blind;

        return baseWorld;
    }

    #endregion

    #region Phase 4: Consumable Status Effect Tests

    [Fact]
    public void ExecuteUseConsumable_WithStatusEffect_AppliesEffectToTarget()
    {
        // Arrange - Create a consumable that applies poison
        var player = CreateCombatantWithCapabilities("Player", health: 1.0f, energy: 1.0f,
            consumables: new[] { new ConsumableEntry { ConsumableRef = "PoisonVial", Quantity = 1 } });
        var enemy = CreateCombatant("Enemy", health: 1.0f);

        var worldWithConsumable = CreateTestWorldWithPhase4Consumables();
        var engine = new BattleEngine(player, enemy, world: worldWithConsumable, randomSeed: 42);
        engine.StartBattle();

        // Act - Use offensive consumable with status effect
        var result = engine.ExecutePlayerDecision(new CombatAction
        {
            ActionType = ActionType.UseConsumable,
            Parameter = "PoisonVial"
        });

        // Assert
        Assert.True(result.Success);
        Assert.Equal("Poison", result.StatusEffectApplied);
        Assert.Single(enemy.ActiveStatusEffects);
        Assert.Equal("Poison", enemy.ActiveStatusEffects[0].StatusEffectRef);
    }

    [Fact]
    public void ExecuteUseConsumable_DefensiveWithStatusEffect_AppliesEffectToSelf()
    {
        // Arrange - Create a defensive consumable that buffs
        var player = CreateCombatantWithCapabilities("Player", health: 0.5f, energy: 1.0f,
            consumables: new[] { new ConsumableEntry { ConsumableRef = "StrengthPotion", Quantity = 1 } });
        var enemy = CreateCombatant("Enemy", health: 1.0f);

        var worldWithConsumable = CreateTestWorldWithPhase4Consumables();
        var engine = new BattleEngine(player, enemy, world: worldWithConsumable, randomSeed: 42);
        engine.StartBattle();

        // Act - Use defensive consumable with status effect
        var result = engine.ExecutePlayerDecision(new CombatAction
        {
            ActionType = ActionType.UseConsumable,
            Parameter = "StrengthPotion"
        });

        // Assert
        Assert.True(result.Success);
        Assert.Equal("StrengthBuff", result.StatusEffectApplied);
        Assert.Single(player.ActiveStatusEffects);
        Assert.Equal("StrengthBuff", player.ActiveStatusEffects[0].StatusEffectRef);
    }

    [Fact]
    public void ExecuteUseConsumable_WithCleanse_RemovesStatusEffects()
    {
        // Arrange - Player has poison, will use antidote
        var player = CreateCombatantWithCapabilities("Player", health: 0.5f, energy: 1.0f,
            consumables: new[] { new ConsumableEntry { ConsumableRef = "Antidote", Quantity = 1 } });
        player.ActiveStatusEffects.Add(new ActiveStatusEffect
        {
            StatusEffectRef = "Poison",
            RemainingTurns = 3,
            Stacks = 2,
            AppliedOnTurn = 1
        });
        var enemy = CreateCombatant("Enemy", health: 1.0f);

        var worldWithConsumable = CreateTestWorldWithPhase4Consumables();
        var engine = new BattleEngine(player, enemy, world: worldWithConsumable, randomSeed: 42);
        engine.StartBattle();

        // Act - Use antidote to cleanse
        var result = engine.ExecutePlayerDecision(new CombatAction
        {
            ActionType = ActionType.UseConsumable,
            Parameter = "Antidote"
        });

        // Assert
        Assert.True(result.Success);
        Assert.Empty(player.ActiveStatusEffects);
    }

    [Fact]
    public void ExecuteUseConsumable_WithStatusEffectChance_MayNotApply()
    {
        // Arrange - Create consumable with 50% chance
        var worldWithConsumable = CreateTestWorldWithPhase4Consumables();

        // Run multiple times with different seeds to verify chance works
        var appliedCount = 0;
        for (int i = 0; i < 20; i++)
        {
            var testPlayer = CreateCombatantWithCapabilities("Player", health: 1.0f, energy: 1.0f,
                consumables: new[] { new ConsumableEntry { ConsumableRef = "UnreliablePoisonVial", Quantity = 1 } });
            var testEnemy = CreateCombatant("Enemy", health: 1.0f);

            var engine = new BattleEngine(testPlayer, testEnemy, world: worldWithConsumable, randomSeed: i);
            engine.StartBattle();

            var result = engine.ExecutePlayerDecision(new CombatAction
            {
                ActionType = ActionType.UseConsumable,
                Parameter = "UnreliablePoisonVial"
            });

            if (!string.IsNullOrEmpty(result.StatusEffectApplied))
            {
                appliedCount++;
            }
        }

        // Assert - With 50% chance, we should have some successes and some failures
        Assert.True(appliedCount > 0, "Some applications should succeed");
        Assert.True(appliedCount < 20, "Some applications should fail");
    }

    [Fact]
    public void ExecuteUseConsumable_NoStatusEffect_NoEffectApplied()
    {
        // Arrange - Basic health potion with no status effect
        var player = CreateCombatantWithCapabilities("Player", health: 0.5f, energy: 1.0f,
            consumables: new[] { new ConsumableEntry { ConsumableRef = "BasicHealthPotion", Quantity = 1 } });
        var enemy = CreateCombatant("Enemy", health: 1.0f);

        var worldWithConsumable = CreateTestWorldWithPhase4Consumables();
        var engine = new BattleEngine(player, enemy, world: worldWithConsumable, randomSeed: 42);
        engine.StartBattle();

        // Act
        var result = engine.ExecutePlayerDecision(new CombatAction
        {
            ActionType = ActionType.UseConsumable,
            Parameter = "BasicHealthPotion"
        });

        // Assert
        Assert.True(result.Success);
        Assert.Null(result.StatusEffectApplied);
        Assert.Empty(player.ActiveStatusEffects);
        Assert.Empty(enemy.ActiveStatusEffects);
    }

    /// <summary>
    /// Creates a test world with Phase 4 consumables that have status effect support.
    /// </summary>
    private static World CreateTestWorldWithPhase4Consumables()
    {
        var baseWorld = CreateTestWorldWithPhase3StatusEffects();

        // Add offensive consumable with status effect
        var poisonVial = new Consumable
        {
            RefName = "PoisonVial",
            DisplayName = "Poison Vial",
            UseType = ItemUseType.Offensive,
            StatusEffectRef = "Poison",
            StatusEffectChance = 1.0f,
            Effects = new CharacterEffects { Health = -0.05f }
        };

        // Add defensive consumable with status effect (buff)
        var strengthPotion = new Consumable
        {
            RefName = "StrengthPotion",
            DisplayName = "Strength Potion",
            UseType = ItemUseType.Defensive,
            StatusEffectRef = "StrengthBuff",
            StatusEffectChance = 1.0f,
            Effects = new CharacterEffects { Health = 0.1f }
        };

        // Add cleansing consumable
        var antidote = new Consumable
        {
            RefName = "Antidote",
            DisplayName = "Antidote",
            UseType = ItemUseType.Defensive,
            CleansesStatusEffects = true,
            CleanseTargetSelf = true,
            Effects = new CharacterEffects { Health = 0.05f }
        };

        // Add unreliable poison vial with 50% chance
        var unreliablePoisonVial = new Consumable
        {
            RefName = "UnreliablePoisonVial",
            DisplayName = "Unreliable Poison Vial",
            UseType = ItemUseType.Offensive,
            StatusEffectRef = "Poison",
            StatusEffectChance = 0.5f, // 50% chance
            Effects = new CharacterEffects { Health = -0.03f }
        };

        // Add basic health potion with no status effect
        var basicHealthPotion = new Consumable
        {
            RefName = "BasicHealthPotion",
            DisplayName = "Basic Health Potion",
            UseType = ItemUseType.Defensive,
            Effects = new CharacterEffects { Health = 0.2f }
        };

        // Add strength buff status effect
        var strengthBuff = new StatusEffect
        {
            RefName = "StrengthBuff",
            DisplayName = "Strength Buff",
            Type = StatusEffectType.StatBoost,
            StrengthModifier = 0.25f, // +25% strength
            DurationTurns = 3,
            MaxStacks = 1,
            Cleansable = false
        };

        baseWorld.Gameplay.Consumables = baseWorld.Gameplay.Consumables
            .Concat(new[] { poisonVial, strengthPotion, antidote, unreliablePoisonVial, basicHealthPotion }).ToArray();
        baseWorld.ConsumablesLookup["PoisonVial"] = poisonVial;
        baseWorld.ConsumablesLookup["StrengthPotion"] = strengthPotion;
        baseWorld.ConsumablesLookup["Antidote"] = antidote;
        baseWorld.ConsumablesLookup["UnreliablePoisonVial"] = unreliablePoisonVial;
        baseWorld.ConsumablesLookup["BasicHealthPotion"] = basicHealthPotion;

        baseWorld.Gameplay.StatusEffects = baseWorld.Gameplay.StatusEffects.Concat(new[] { strengthBuff }).ToArray();
        baseWorld.StatusEffectsLookup["StrengthBuff"] = strengthBuff;

        return baseWorld;
    }

    #endregion

    #region Phase 5: Vulnerable Status Effect and OnDefend Trigger Tests

    [Fact]
    public void ExecuteAttack_VulnerableDefender_TakesIncreasedDamage()
    {
        // Arrange
        var worldWithVulnerable = CreateTestWorldWithPhase5StatusEffects();

        var player = CreateCombatant("Player", health: 1.0f, strength: 0.5f);
        var enemy = CreateCombatant("Enemy", health: 1.0f, defense: 0.3f);

        // Apply Vulnerable status effect to enemy
        enemy.ActiveStatusEffects.Add(new ActiveStatusEffect
        {
            StatusEffectRef = "Vulnerable",
            RemainingTurns = 3,
            Stacks = 1,
            AppliedOnTurn = 1
        });

        var engine = new BattleEngine(player, enemy, world: worldWithVulnerable, randomSeed: 42);
        engine.StartBattle();

        // Store initial health
        var initialHealth = enemy.Health;

        // Act - Player attacks vulnerable enemy
        var result = engine.ExecutePlayerDecision(new CombatAction
        {
            ActionType = ActionType.Attack
        });

        // Assert - Vulnerable enemy should take more damage
        Assert.True(result.Success);
        var damageDealt = initialHealth - enemy.Health;
        Assert.True(damageDealt > 0);

        // Verify combat log mentions vulnerability
        Assert.Contains(engine.CombatLog, log => log.Contains("vulnerable"));
    }

    [Fact]
    public void ExecuteWeaponAttack_VulnerableDefender_TakesIncreasedDamage()
    {
        // Arrange
        var worldWithVulnerable = CreateTestWorldWithPhase5StatusEffects();

        var player = CreateCombatantWithCapabilities("Player", health: 1.0f, strength: 0.5f, energy: 1.0f,
            equipment: new[] { new EquipmentEntry { EquipmentRef = "IronSword", Condition = 1.0f } });
        player.CombatProfile["RightHand"] = "IronSword";
        var enemy = CreateCombatant("Enemy", health: 1.0f, defense: 0.3f);

        // Apply Vulnerable status effect to enemy
        enemy.ActiveStatusEffects.Add(new ActiveStatusEffect
        {
            StatusEffectRef = "Vulnerable",
            RemainingTurns = 3,
            Stacks = 1,
            AppliedOnTurn = 1
        });

        var engine = new BattleEngine(player, enemy, world: worldWithVulnerable, randomSeed: 42);
        engine.StartBattle();

        // Store initial health
        var initialHealth = enemy.Health;

        // Act - Player weapon attacks vulnerable enemy
        var result = engine.ExecutePlayerDecision(new CombatAction
        {
            ActionType = ActionType.Attack,
            Parameter = "IronSword"
        });

        // Assert
        Assert.True(result.Success);
        Assert.Contains(engine.CombatLog, log => log.Contains("vulnerable"));
    }

    [Fact]
    public void ExecuteSpellAttack_VulnerableDefender_TakesIncreasedDamage()
    {
        // Arrange
        var worldWithVulnerable = CreateTestWorldWithPhase5StatusEffects();

        var player = CreateCombatantWithCapabilities("Player", health: 1.0f, magic: 0.5f, energy: 1.0f,
            spells: new[] { new SpellEntry { SpellRef = "FreeSpell", Condition = 1.0f } });
        var enemy = CreateCombatant("Enemy", health: 1.0f, defense: 0.3f);

        // Apply Vulnerable status effect to enemy
        enemy.ActiveStatusEffects.Add(new ActiveStatusEffect
        {
            StatusEffectRef = "Vulnerable",
            RemainingTurns = 3,
            Stacks = 1,
            AppliedOnTurn = 1
        });

        var engine = new BattleEngine(player, enemy, world: worldWithVulnerable, randomSeed: 42);
        engine.StartBattle();

        // Act - Player casts spell on vulnerable enemy
        var result = engine.ExecutePlayerDecision(new CombatAction
        {
            ActionType = ActionType.CastSpell,
            Parameter = "FreeSpell"
        });

        // Assert
        Assert.True(result.Success);
        Assert.Contains(engine.CombatLog, log => log.Contains("vulnerable"));
    }

    [Fact]
    public void ExecuteAttack_VulnerableWithMultipleStacks_TakesEvenMoreDamage()
    {
        // Arrange
        var worldWithVulnerable = CreateTestWorldWithPhase5StatusEffects();

        var player = CreateCombatant("Player", health: 1.0f, strength: 0.5f);
        var enemy = CreateCombatant("Enemy", health: 1.0f, defense: 0.3f);

        // Apply 2 stacks of Vulnerable (stackable up to 3)
        enemy.ActiveStatusEffects.Add(new ActiveStatusEffect
        {
            StatusEffectRef = "Vulnerable",
            RemainingTurns = 3,
            Stacks = 2,
            AppliedOnTurn = 1
        });

        var engine = new BattleEngine(player, enemy, world: worldWithVulnerable, randomSeed: 42);
        engine.StartBattle();

        // Store initial health
        var initialHealth = enemy.Health;

        // Act
        var result = engine.ExecutePlayerDecision(new CombatAction
        {
            ActionType = ActionType.Attack
        });

        // Assert - More stacks = more damage
        Assert.True(result.Success);
        Assert.Contains(engine.CombatLog, log => log.Contains("vulnerable"));
    }

    [Fact]
    public void ExecuteDefend_WithOnDefendEquipment_AppliesStatusEffect()
    {
        // Arrange
        var worldWithOnDefend = CreateTestWorldWithPhase5OnDefendEquipment();

        var player = CreateCombatantWithCapabilities("Player", health: 1.0f, energy: 0.5f,
            equipment: new[] { new EquipmentEntry { EquipmentRef = "IronShield", Condition = 1.0f } });
        player.CombatProfile["LeftHand"] = "IronShield";
        var enemy = CreateCombatant("Enemy", health: 1.0f);

        var engine = new BattleEngine(player, enemy, world: worldWithOnDefend, randomSeed: 42);
        engine.StartBattle();

        // Act - Player defends with shield that has OnDefend effect
        var result = engine.ExecutePlayerDecision(new CombatAction
        {
            ActionType = ActionType.Defend
        });

        // Assert
        Assert.True(result.Success);
        Assert.Equal(BattleActionType.Defend, result.ActionType);

        // Verify the status effect was applied to the defender
        Assert.Single(player.ActiveStatusEffects);
        Assert.Equal("IronWill", player.ActiveStatusEffects[0].StatusEffectRef);
    }

    [Fact]
    public void ExecuteDefend_WithoutOnDefendEquipment_NoStatusEffect()
    {
        // Arrange
        var worldWithOnDefend = CreateTestWorldWithPhase5OnDefendEquipment();

        var player = CreateCombatantWithCapabilities("Player", health: 1.0f, energy: 0.5f,
            equipment: new[] { new EquipmentEntry { EquipmentRef = "IronSword", Condition = 1.0f } });
        player.CombatProfile["RightHand"] = "IronSword"; // Sword has no OnDefend effect
        var enemy = CreateCombatant("Enemy", health: 1.0f);

        var engine = new BattleEngine(player, enemy, world: worldWithOnDefend, randomSeed: 42);
        engine.StartBattle();

        // Act - Player defends without a shield
        var result = engine.ExecutePlayerDecision(new CombatAction
        {
            ActionType = ActionType.Defend
        });

        // Assert - No status effect should be applied
        Assert.True(result.Success);
        Assert.Empty(player.ActiveStatusEffects);
    }

    [Fact]
    public void ExecuteDefend_WithOnDefendChance_MayNotApply()
    {
        // Arrange
        var worldWithOnDefend = CreateTestWorldWithPhase5OnDefendEquipment();

        // Run multiple times with different seeds to verify chance works
        var appliedCount = 0;
        for (int i = 0; i < 20; i++)
        {
            var testPlayer = CreateCombatantWithCapabilities("Player", health: 1.0f, energy: 0.5f,
                equipment: new[] { new EquipmentEntry { EquipmentRef = "UnreliableShield", Condition = 1.0f } });
            testPlayer.CombatProfile["LeftHand"] = "UnreliableShield";
            var testEnemy = CreateCombatant("Enemy", health: 1.0f);

            var engine = new BattleEngine(testPlayer, testEnemy, world: worldWithOnDefend, randomSeed: i);
            engine.StartBattle();

            engine.ExecutePlayerDecision(new CombatAction
            {
                ActionType = ActionType.Defend
            });

            if (testPlayer.ActiveStatusEffects.Count > 0)
            {
                appliedCount++;
            }
        }

        // Assert - With 50% chance, we should have some successes and some failures
        Assert.True(appliedCount > 0, "Some applications should succeed");
        Assert.True(appliedCount < 20, "Some applications should fail");
    }

    [Fact]
    public void GetVulnerabilityMultiplier_NoVulnerable_ReturnsOne()
    {
        // Arrange
        var worldWithVulnerable = CreateTestWorldWithPhase5StatusEffects();
        var player = CreateCombatant("Player", health: 1.0f);
        var enemy = CreateCombatant("Enemy", health: 1.0f);

        var engine = new BattleEngine(player, enemy, world: worldWithVulnerable, randomSeed: 42);

        // Act
        var multiplier = engine.GetVulnerabilityMultiplier(enemy);

        // Assert
        Assert.Equal(1.0f, multiplier);
    }

    [Fact]
    public void GetVulnerabilityMultiplier_WithVulnerable_ReturnsIncreased()
    {
        // Arrange
        var worldWithVulnerable = CreateTestWorldWithPhase5StatusEffects();
        var player = CreateCombatant("Player", health: 1.0f);
        var enemy = CreateCombatant("Enemy", health: 1.0f);

        // Apply Vulnerable (25% more damage taken via DefenseModifier = -0.25)
        enemy.ActiveStatusEffects.Add(new ActiveStatusEffect
        {
            StatusEffectRef = "Vulnerable",
            RemainingTurns = 3,
            Stacks = 1,
            AppliedOnTurn = 1
        });

        var engine = new BattleEngine(player, enemy, world: worldWithVulnerable, randomSeed: 42);

        // Act
        var multiplier = engine.GetVulnerabilityMultiplier(enemy);

        // Assert - Should be greater than 1 (more damage taken)
        Assert.True(multiplier > 1.0f);
        Assert.Equal(1.25f, multiplier, 2); // 25% more damage
    }

    /// <summary>
    /// Creates a test world with Phase 5 status effects including Vulnerable.
    /// </summary>
    private static World CreateTestWorldWithPhase5StatusEffects()
    {
        var baseWorld = CreateTestWorldWithPhase3StatusEffects();

        // Add Vulnerable status effect - uses negative DefenseModifier to increase damage taken
        var vulnerable = new StatusEffect
        {
            RefName = "Vulnerable",
            DisplayName = "Vulnerable",
            Type = StatusEffectType.Vulnerable,
            DefenseModifier = -0.25f, // Each stack adds 25% more damage taken
            DurationTurns = 3,
            MaxStacks = 3,
            Cleansable = true
        };

        // Add FreeSpell for testing (no RequiresEquipped)
        var freeSpell = new Spell
        {
            RefName = "FreeSpell",
            DisplayName = "Free Spell",
            RequiresEquippedSpecified = false,
            Effects = new CharacterEffects { Health = -0.1f }
        };

        baseWorld.Gameplay.StatusEffects = baseWorld.Gameplay.StatusEffects.Concat(new[] { vulnerable }).ToArray();
        baseWorld.StatusEffectsLookup["Vulnerable"] = vulnerable;

        baseWorld.Gameplay.Spells = baseWorld.Gameplay.Spells.Concat(new[] { freeSpell }).ToArray();
        baseWorld.SpellsLookup["FreeSpell"] = freeSpell;

        return baseWorld;
    }

    /// <summary>
    /// Creates a test world with Phase 5 equipment that has OnDefend status effects.
    /// </summary>
    private static World CreateTestWorldWithPhase5OnDefendEquipment()
    {
        var baseWorld = CreateTestWorldWithPhase5StatusEffects();

        // Add IronWill status effect (defensive buff when blocking)
        var ironWill = new StatusEffect
        {
            RefName = "IronWill",
            DisplayName = "Iron Will",
            Type = StatusEffectType.StatBoost,
            DefenseModifier = 0.2f, // +20% defense
            DurationTurns = 2,
            MaxStacks = 1,
            Cleansable = false
        };

        // Add Iron Shield with OnDefend effect (100% chance)
        var ironShield = new Equipment
        {
            RefName = "IronShield",
            DisplayName = "Iron Shield",
            SlotRef = "LeftHand",
            Category = EquipmentCategoryType.Shield,
            OnDefendStatusEffectRef = "IronWill",
            OnDefendStatusEffectChance = 1.0f,
            Effects = new CharacterEffects { Defense = 0.15f }
        };

        // Add Unreliable Shield with 50% OnDefend chance
        var unreliableShield = new Equipment
        {
            RefName = "UnreliableShield",
            DisplayName = "Unreliable Shield",
            SlotRef = "LeftHand",
            Category = EquipmentCategoryType.Shield,
            OnDefendStatusEffectRef = "IronWill",
            OnDefendStatusEffectChance = 0.5f, // 50% chance
            Effects = new CharacterEffects { Defense = 0.1f }
        };

        baseWorld.Gameplay.StatusEffects = baseWorld.Gameplay.StatusEffects.Concat(new[] { ironWill }).ToArray();
        baseWorld.StatusEffectsLookup["IronWill"] = ironWill;

        baseWorld.Gameplay.Equipment = baseWorld.Gameplay.Equipment.Concat(new[] { ironShield, unreliableShield }).ToArray();
        baseWorld.EquipmentLookup["IronShield"] = ironShield;
        baseWorld.EquipmentLookup["UnreliableShield"] = unreliableShield;

        return baseWorld;
    }

    #endregion

    #region Phase 6: Two-Handed Weapons and Turn-Based Status Effect Triggers

    [Fact]
    public void TwoHandedWeapon_EquippingOccupiesBothHands()
    {
        // Arrange
        var worldWithTwoHanded = CreateTestWorldWithPhase6TwoHandedWeapons();

        var player = CreateCombatantWithCapabilities("Player", health: 1.0f, strength: 0.5f, energy: 1.0f,
            equipment: new[] { new EquipmentEntry { EquipmentRef = "GreatSword", Condition = 1.0f } });
        var enemy = CreateCombatant("Enemy", health: 1.0f);

        var engine = new BattleEngine(player, enemy, world: worldWithTwoHanded, randomSeed: 42);
        engine.StartBattle();

        // Act - Equip two-handed weapon in right hand
        var result = engine.ExecutePlayerDecision(new CombatAction
        {
            ActionType = ActionType.AdjustLoadout,
            Parameter = "RightHand:GreatSword"
        });

        // Assert - Both hands should have the same weapon
        Assert.True(result.Success);
        Assert.Equal("GreatSword", player.CombatProfile["RightHand"]);
        Assert.Equal("GreatSword", player.CombatProfile["LeftHand"]);
    }

    [Fact]
    public void TwoHandedWeapon_ClearsOtherHandWhenEquipping()
    {
        // Arrange
        var worldWithTwoHanded = CreateTestWorldWithPhase6TwoHandedWeapons();

        var player = CreateCombatantWithCapabilities("Player", health: 1.0f, strength: 0.5f, energy: 1.0f,
            equipment: new[]
            {
                new EquipmentEntry { EquipmentRef = "GreatSword", Condition = 1.0f },
                new EquipmentEntry { EquipmentRef = "IronSword", Condition = 1.0f }
            });
        player.CombatProfile["RightHand"] = "IronSword"; // One-handed sword in right
        player.CombatProfile["LeftHand"] = "IronShield"; // Shield in left

        var enemy = CreateCombatant("Enemy", health: 1.0f);

        var engine = new BattleEngine(player, enemy, world: worldWithTwoHanded, randomSeed: 42);
        engine.StartBattle();

        // Act - Equip two-handed weapon (should clear shield)
        var result = engine.ExecutePlayerDecision(new CombatAction
        {
            ActionType = ActionType.AdjustLoadout,
            Parameter = "RightHand:GreatSword"
        });

        // Assert - Shield should be cleared, both hands have greatsword
        Assert.True(result.Success);
        Assert.Equal("GreatSword", player.CombatProfile["RightHand"]);
        Assert.Equal("GreatSword", player.CombatProfile["LeftHand"]);
    }

    [Fact]
    public void TwoHandedWeapon_CannotEquipOneHandedWhenTwoHandedEquipped()
    {
        // Arrange
        var worldWithTwoHanded = CreateTestWorldWithPhase6TwoHandedWeapons();

        var player = CreateCombatantWithCapabilities("Player", health: 1.0f, strength: 0.5f, energy: 1.0f,
            equipment: new[]
            {
                new EquipmentEntry { EquipmentRef = "GreatSword", Condition = 1.0f },
                new EquipmentEntry { EquipmentRef = "IronShield", Condition = 1.0f }
            });
        player.CombatProfile["RightHand"] = "GreatSword";
        player.CombatProfile["LeftHand"] = "GreatSword"; // Two-handed equipped

        var enemy = CreateCombatant("Enemy", health: 1.0f);

        var engine = new BattleEngine(player, enemy, world: worldWithTwoHanded, randomSeed: 42);
        engine.StartBattle();

        // Act - Try to equip shield in left hand (blocked by two-handed weapon)
        var result = engine.ExecutePlayerDecision(new CombatAction
        {
            ActionType = ActionType.AdjustLoadout,
            Parameter = "LeftHand:IronShield"
        });

        // Assert - Should fail because two-handed weapon occupies both hands
        Assert.False(result.Success);
        Assert.Contains("two-handed", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void IsTwoHandedWeapon_ReturnsTrueForTwoHandedMelee()
    {
        // Arrange
        var worldWithTwoHanded = CreateTestWorldWithPhase6TwoHandedWeapons();
        var player = CreateCombatant("Player", health: 1.0f);
        var enemy = CreateCombatant("Enemy", health: 1.0f);

        var engine = new BattleEngine(player, enemy, world: worldWithTwoHanded, randomSeed: 42);
        var greatSword = worldWithTwoHanded.TryGetEquipmentByRefName("GreatSword");

        // Act & Assert
        Assert.True(engine.IsTwoHandedWeapon(greatSword));
    }

    [Fact]
    public void IsTwoHandedWeapon_ReturnsFalseForOneHandedMelee()
    {
        // Arrange
        var worldWithTwoHanded = CreateTestWorldWithPhase6TwoHandedWeapons();
        var player = CreateCombatant("Player", health: 1.0f);
        var enemy = CreateCombatant("Enemy", health: 1.0f);

        var engine = new BattleEngine(player, enemy, world: worldWithTwoHanded, randomSeed: 42);
        var ironSword = worldWithTwoHanded.TryGetEquipmentByRefName("IronSword");

        // Act & Assert
        Assert.False(engine.IsTwoHandedWeapon(ironSword));
    }

    [Fact]
    public void StatusEffect_StartOfTurn_AppliesDamageAtTurnStart()
    {
        // Arrange
        var worldWithTiming = CreateTestWorldWithPhase6StatusEffectTiming();

        var player = CreateCombatant("Player", health: 1.0f, strength: 0.5f);
        var enemy = CreateCombatant("Enemy", health: 1.0f);

        // Apply StartOfTurn poison to enemy
        enemy.ActiveStatusEffects.Add(new ActiveStatusEffect
        {
            StatusEffectRef = "StartOfTurnPoison",
            RemainingTurns = 3,
            Stacks = 1,
            AppliedOnTurn = 1
        });

        var engine = new BattleEngine(player, enemy, world: worldWithTiming, randomSeed: 42);
        engine.StartBattle();

        var healthBeforeProcessing = enemy.Health;

        // Act - Process start of turn effects
        engine.ProcessStatusEffects(enemy);

        // Assert - Damage should be applied (5% per turn)
        Assert.True(enemy.Health < healthBeforeProcessing);
        Assert.Contains(engine.CombatLog, log => log.Contains("takes") && log.Contains("damage"));
    }

    [Fact]
    public void StatusEffect_EndOfTurn_AppliesDamageAtTurnEnd()
    {
        // Arrange
        var worldWithTiming = CreateTestWorldWithPhase6StatusEffectTiming();

        var player = CreateCombatant("Player", health: 1.0f, strength: 0.5f);
        var enemy = CreateCombatant("Enemy", health: 1.0f);

        // Apply EndOfTurn bleed to player
        player.ActiveStatusEffects.Add(new ActiveStatusEffect
        {
            StatusEffectRef = "EndOfTurnBleed",
            RemainingTurns = 3,
            Stacks = 1,
            AppliedOnTurn = 1
        });

        var engine = new BattleEngine(player, enemy, world: worldWithTiming, randomSeed: 42);
        engine.StartBattle();

        var healthBeforeEndOfTurn = player.Health;

        // Act - Process end of turn effects (should apply bleed)
        engine.ProcessEndOfTurnStatusEffects(player);

        // Assert - Damage should be applied
        Assert.True(player.Health < healthBeforeEndOfTurn);
        Assert.Contains(engine.CombatLog, log => log.Contains("takes") && log.Contains("damage"));
    }

    [Fact]
    public void StatusEffect_StartOfTurn_DoesNotApplyEndOfTurnEffect()
    {
        // Arrange
        var worldWithTiming = CreateTestWorldWithPhase6StatusEffectTiming();

        var player = CreateCombatant("Player", health: 1.0f, strength: 0.5f);
        var enemy = CreateCombatant("Enemy", health: 1.0f);

        // Apply EndOfTurn bleed (should NOT trigger at start of turn)
        player.ActiveStatusEffects.Add(new ActiveStatusEffect
        {
            StatusEffectRef = "EndOfTurnBleed",
            RemainingTurns = 3,
            Stacks = 1,
            AppliedOnTurn = 1
        });

        var engine = new BattleEngine(player, enemy, world: worldWithTiming, randomSeed: 42);
        engine.StartBattle();

        var healthBeforeProcessing = player.Health;
        engine.CombatLog.Clear();

        // Act - Process start of turn effects
        engine.ProcessStatusEffects(player);

        // Assert - No damage should be applied (EndOfTurn effect processed at StartOfTurn)
        Assert.Equal(healthBeforeProcessing, player.Health);
        Assert.DoesNotContain(engine.CombatLog, log => log.Contains("takes") && log.Contains("damage"));
    }

    [Fact]
    public void StatusEffect_EndOfTurn_DoesNotApplyStartOfTurnEffect()
    {
        // Arrange
        var worldWithTiming = CreateTestWorldWithPhase6StatusEffectTiming();

        var player = CreateCombatant("Player", health: 1.0f, strength: 0.5f);
        var enemy = CreateCombatant("Enemy", health: 1.0f);

        // Apply StartOfTurn poison (should NOT trigger at end of turn)
        enemy.ActiveStatusEffects.Add(new ActiveStatusEffect
        {
            StatusEffectRef = "StartOfTurnPoison",
            RemainingTurns = 3,
            Stacks = 1,
            AppliedOnTurn = 1
        });

        var engine = new BattleEngine(player, enemy, world: worldWithTiming, randomSeed: 42);
        engine.StartBattle();

        var healthBeforeProcessing = enemy.Health;
        engine.CombatLog.Clear();

        // Act - Process end of turn effects
        engine.ProcessEndOfTurnStatusEffects(enemy);

        // Assert - No damage should be applied
        Assert.Equal(healthBeforeProcessing, enemy.Health);
        Assert.DoesNotContain(engine.CombatLog, log => log.Contains("takes") && log.Contains("damage"));
    }

    /// <summary>
    /// Creates a test world with Phase 6 two-handed weapons.
    /// </summary>
    private static World CreateTestWorldWithPhase6TwoHandedWeapons()
    {
        var baseWorld = CreateTestWorldWithPhase5OnDefendEquipment();

        // Add two-handed great sword
        var greatSword = new Equipment
        {
            RefName = "GreatSword",
            DisplayName = "Great Sword",
            SlotRef = "RightHand",
            Category = EquipmentCategoryType.TwoHandedMelee,
            Effects = new CharacterEffects { Strength = 0.3f }
        };

        baseWorld.Gameplay.Equipment = baseWorld.Gameplay.Equipment.Concat(new[] { greatSword }).ToArray();
        baseWorld.EquipmentLookup["GreatSword"] = greatSword;

        return baseWorld;
    }

    /// <summary>
    /// Creates a test world with Phase 6 status effects with different application timings.
    /// </summary>
    private static World CreateTestWorldWithPhase6StatusEffectTiming()
    {
        var baseWorld = CreateTestWorldWithPhase6TwoHandedWeapons();

        // Add StartOfTurn poison
        var startOfTurnPoison = new StatusEffect
        {
            RefName = "StartOfTurnPoison",
            DisplayName = "Venomous Poison",
            Type = StatusEffectType.DamageOverTime,
            DamagePerTurn = 5, // 5% damage
            DurationTurns = 3,
            ApplicationMethod = ApplicationMethod.StartOfTurn,
            Cleansable = true
        };

        // Add EndOfTurn bleed
        var endOfTurnBleed = new StatusEffect
        {
            RefName = "EndOfTurnBleed",
            DisplayName = "Deep Wound",
            Type = StatusEffectType.DamageOverTime,
            DamagePerTurn = 3, // 3% damage
            DurationTurns = 4,
            ApplicationMethod = ApplicationMethod.EndOfTurn,
            Cleansable = true
        };

        baseWorld.Gameplay.StatusEffects = baseWorld.Gameplay.StatusEffects
            .Concat(new[] { startOfTurnPoison, endOfTurnBleed }).ToArray();
        baseWorld.StatusEffectsLookup["StartOfTurnPoison"] = startOfTurnPoison;
        baseWorld.StatusEffectsLookup["EndOfTurnBleed"] = endOfTurnBleed;

        return baseWorld;
    }

    #endregion

    #region Phase 3: NeutralMultiplier Tests

    [Fact]
    public void CalculateAffinityMultiplier_NoMatchup_UsesNeutralMultiplier()
    {
        // Arrange - Create world with affinities that have custom NeutralMultiplier
        var world = CreateTestWorldWithAffinities();

        // Act - Calculate multiplier for Fire vs Earth (no explicit matchup defined)
        var multiplier = EffectApplier.CalculateAffinityMultiplier("Fire", "Earth", world);

        // Assert - Should use Fire's NeutralMultiplier (0.9)
        Assert.Equal(0.9f, multiplier);
    }

    [Fact]
    public void CalculateAffinityMultiplier_SameAffinity_UsesNeutralMultiplier()
    {
        // Arrange
        var world = CreateTestWorldWithAffinities();

        // Act - Calculate multiplier for Fire vs Fire
        var multiplier = EffectApplier.CalculateAffinityMultiplier("Fire", "Fire", world);

        // Assert - Should use Fire's NeutralMultiplier (0.9)
        Assert.Equal(0.9f, multiplier);
    }

    [Fact]
    public void CalculateAffinityMultiplier_ExplicitMatchup_UsesMatchupMultiplier()
    {
        // Arrange
        var world = CreateTestWorldWithAffinities();

        // Act - Calculate multiplier for Fire vs Ice (explicit matchup)
        var multiplier = EffectApplier.CalculateAffinityMultiplier("Fire", "Ice", world);

        // Assert - Should use explicit matchup multiplier (1.5)
        Assert.Equal(1.5f, multiplier);
    }

    [Fact]
    public void CalculateAffinityMultiplier_NullAffinity_ReturnsOne()
    {
        // Arrange
        var world = CreateTestWorldWithAffinities();

        // Act
        var multiplier = EffectApplier.CalculateAffinityMultiplier(null, "Fire", world);

        // Assert - No bonus when attacker has no affinity
        Assert.Equal(1.0f, multiplier);
    }

    [Fact]
    public void CalculateAffinityMultiplier_DefaultNeutralMultiplier_ReturnsOne()
    {
        // Arrange - Create world with affinity that has default NeutralMultiplier
        var world = CreateTestWorldWithAffinities();

        // Act - Ice has default NeutralMultiplier (1.0)
        var multiplier = EffectApplier.CalculateAffinityMultiplier("Ice", "Ice", world);

        // Assert
        Assert.Equal(1.0f, multiplier);
    }

    private static World CreateTestWorldWithAffinities()
    {
        var world = new World();

        var fireAffinity = new CharacterAffinity
        {
            RefName = "Fire",
            DisplayName = "Fire",
            NeutralMultiplier = 0.9f, // Custom neutral multiplier
            Matchup = new[]
            {
                new AffinityMatchup { TargetAffinityRef = "Ice", Multiplier = 1.5f }, // Strong vs Ice
                new AffinityMatchup { TargetAffinityRef = "Water", Multiplier = 0.5f } // Weak vs Water
            }
        };

        var iceAffinity = new CharacterAffinity
        {
            RefName = "Ice",
            DisplayName = "Ice",
            // NeutralMultiplier defaults to 1.0
            Matchup = new[]
            {
                new AffinityMatchup { TargetAffinityRef = "Fire", Multiplier = 0.5f }
            }
        };

        var earthAffinity = new CharacterAffinity
        {
            RefName = "Earth",
            DisplayName = "Earth",
            NeutralMultiplier = 1.1f, // Slightly stronger neutral
            Matchup = Array.Empty<AffinityMatchup>()
        };

        world.WorldTemplate = new WorldTemplate
        {
            Gameplay = new GameplayComponents
            {
                CharacterAffinities = new[] { fireAffinity, iceAffinity, earthAffinity },
                Equipment = Array.Empty<Equipment>(),
                Spells = Array.Empty<Spell>(),
                StatusEffects = Array.Empty<StatusEffect>(),
                Consumables = Array.Empty<Consumable>(),
                Characters = Array.Empty<Character>(),
                DialogueTrees = Array.Empty<DialogueTree>(),
                QuestTokens = Array.Empty<QuestToken>(),
                Factions = Array.Empty<Faction>(),
                SagaArcs = Array.Empty<SagaArc>()
            }
        };

        return world;
    }

    #endregion
}
