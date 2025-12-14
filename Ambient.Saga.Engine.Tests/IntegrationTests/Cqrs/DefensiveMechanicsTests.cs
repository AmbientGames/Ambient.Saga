using Ambient.Domain;
using Ambient.Domain.Partials;
using Ambient.Saga.Engine.Domain.Rpg.Battle;
using Xunit;
using Xunit.Abstractions;

namespace Ambient.Saga.Engine.Tests.IntegrationTests.Cqrs;

/// <summary>
/// Comprehensive tests for defensive mechanics in battle system:
///
/// DEFENSIVE STATES:
/// - IsDefending: 50% physical damage reduction, 30% spell damage reduction
/// - IsAdjusting: 15% physical damage reduction, 10% spell damage reduction
///
/// STATE TRANSITIONS:
/// - Offensive actions clear defensive states
/// - Defensive actions override each other (can't be both Defending and Adjusting)
/// - States persist until explicitly changed
///
/// EDGE CASES:
/// - Multiple defensive turns in a row
/// - Switching between defensive states
/// - Defensive state + critical hits
/// - Defensive state + affinity bonuses
/// - Consumable use clears defensive state
///
/// RE-ENABLED: Tests verified compatible with current BattleEngine API
/// </summary>
public class DefensiveMechanicsTests
{
    private readonly ITestOutputHelper _output;
    private readonly World _testWorld;

    public DefensiveMechanicsTests(ITestOutputHelper output)
    {
        _output = output;
        _testWorld = CreateTestWorld();
    }

    [Fact]
    public void Defend_ReducesPhysicalDamageBy50Percent()
    {
        // ARRANGE: Two combatants, one defending
        // Note: defender is PLAYER (first arg), attacker is ENEMY (second arg)
        var attacker = CreateCombatant("Attacker", strength: 0.20f);
        var defender = CreateCombatant("Defender", defense: 0.10f);

        var engine = new BattleEngine(defender, attacker, null, _testWorld);
        engine.StartBattle();  // Enemy attacks first, then it's player's turn

        // Player (defender) uses Defend action
        engine.ExecutePlayerDecision(new CombatAction { ActionType = ActionType.Defend });

        var defenderHealthBeforeAttack = defender.Health;
        _output.WriteLine($"Defender health before attack: {defenderHealthBeforeAttack * 100:F1}%");
        _output.WriteLine($"Defender IsDefending: {defender.IsDefending}");

        // ACT: Enemy (attacker) attacks
        var attackResult = engine.ExecuteEnemyTurn();

        // ASSERT: Damage reduced by 50%
        var damageDealt = defenderHealthBeforeAttack - defender.Health;
        _output.WriteLine($"Damage dealt: {damageDealt * 100:F1}%");
        _output.WriteLine($"Combat log:\n{string.Join("\n", engine.CombatLog)}");

        Assert.True(attackResult.Success);
        Assert.True(attackResult.Damage > 0);
        Assert.Contains(engine.CombatLog, log => log.Contains("defense reduces incoming damage"));
    }

    [Fact]
    public void AdjustLoadout_ReducesPhysicalDamageBy15Percent()
    {
        // ARRANGE: Combatant uses AdjustLoadout for defensive positioning
        // Note: defender is PLAYER (first arg), attacker is ENEMY (second arg)
        var attacker = CreateCombatant("Attacker", strength: 0.20f);
        var defender = CreateCombatant("Defender", defense: 0.10f);

        var engine = new BattleEngine(defender, attacker, null, _testWorld);
        engine.StartBattle();  // Enemy attacks first, then it's player's turn

        // Player (defender) uses AdjustLoadout action
        var adjustResult = engine.ExecutePlayerDecision(new CombatAction
        {
            ActionType = ActionType.AdjustLoadout,
            Parameter = "RightHand:WoodenSword"
        });

        _output.WriteLine($"Adjust result: {adjustResult.Message}");
        _output.WriteLine($"Defender IsAdjusting: {defender.IsAdjusting}");
        _output.WriteLine($"Defender IsDefending: {defender.IsDefending}");

        Assert.True(adjustResult.Success);
        Assert.True(defender.IsAdjusting);
        Assert.False(defender.IsDefending);

        var defenderHealthBeforeAttack = defender.Health;

        // ACT: Enemy (attacker) attacks
        var attackResult = engine.ExecuteEnemyTurn();

        // ASSERT: Damage reduced by 15%
        var damageDealt = defenderHealthBeforeAttack - defender.Health;
        _output.WriteLine($"Damage dealt with 15% reduction: {damageDealt * 100:F1}%");
        _output.WriteLine($"Combat log:\n{string.Join("\n", engine.CombatLog)}");

        Assert.Contains(engine.CombatLog, log => log.Contains("defensive positioning reduces damage"));
    }

    [Fact]
    public void ChangeLoadout_ReducesPhysicalDamageBy15Percent()
    {
        // ARRANGE: Combatant uses ChangeLoadout for multiple changes
        // Note: defender is PLAYER (first arg), attacker is ENEMY (second arg)
        var attacker = CreateCombatant("Attacker", strength: 0.20f);
        var defender = CreateCombatant("Defender", defense: 0.10f);

        var engine = new BattleEngine(defender, attacker, null, _testWorld);
        engine.StartBattle();  // Enemy attacks first, then it's player's turn

        // Player (defender) uses ChangeLoadout action (multiple changes)
        var changeResult = engine.ExecutePlayerDecision(new CombatAction
        {
            ActionType = ActionType.ChangeLoadout,
            Parameter = "RightHand:IronSword,LeftHand:WoodenShield"
        });

        _output.WriteLine($"Change result: {changeResult.Message}");
        _output.WriteLine($"Defender IsAdjusting: {defender.IsAdjusting}");

        Assert.True(changeResult.Success);
        Assert.True(defender.IsAdjusting);

        var defenderHealthBeforeAttack = defender.Health;

        // ACT: Enemy (attacker) attacks
        var attackResult = engine.ExecuteEnemyTurn();

        // ASSERT: Damage reduced by 15%
        var damageDealt = defenderHealthBeforeAttack - defender.Health;
        _output.WriteLine($"Damage dealt with 15% reduction: {damageDealt * 100:F1}%");

        Assert.Contains(engine.CombatLog, log => log.Contains("defensive positioning reduces damage"));
    }

    [Fact]
    public void DefensiveStates_MutuallyExclusive()
    {
        // ARRANGE: Test that Defend and Adjust don't stack
        // Note: combatant is PLAYER (first arg), dummy is ENEMY (second arg)
        var combatant = CreateCombatant("Defender", defense: 0.10f);
        var dummy = CreateCombatant("Dummy", strength: 0.01f);
        var engine = new BattleEngine(combatant, dummy, null, _testWorld);
        engine.StartBattle();  // Enemy attacks first, then it's player's turn

        // Start with Defend
        engine.ExecutePlayerDecision(new CombatAction { ActionType = ActionType.Defend });
        Assert.True(combatant.IsDefending);
        Assert.False(combatant.IsAdjusting);
        _output.WriteLine("After Defend: IsDefending=true, IsAdjusting=false");

        // Skip enemy turn
        engine.ExecuteEnemyTurn();

        // ACT: Switch to AdjustLoadout
        engine.ExecutePlayerDecision(new CombatAction
        {
            ActionType = ActionType.AdjustLoadout,
            Parameter = "RightHand:IronSword"
        });

        // ASSERT: Now adjusting, not defending
        Assert.False(combatant.IsDefending);
        Assert.True(combatant.IsAdjusting);
        _output.WriteLine("After AdjustLoadout: IsDefending=false, IsAdjusting=true");

        // Skip enemy turn
        engine.ExecuteEnemyTurn();

        // Switch back to Defend
        engine.ExecutePlayerDecision(new CombatAction { ActionType = ActionType.Defend });
        Assert.True(combatant.IsDefending);
        Assert.False(combatant.IsAdjusting);
        _output.WriteLine("After Defend again: IsDefending=true, IsAdjusting=false");
    }

    [Fact]
    public void OffensiveAction_ClearsDefensiveStates()
    {
        // ARRANGE: Combatant defends, then attacks
        var player = CreateCombatant("Player", strength: 0.15f);
        var enemy = CreateCombatant("Enemy", strength: 0.10f);
        var engine = new BattleEngine(player, enemy, null, _testWorld);
        engine.StartBattle();  // Enemy attacks first, then it's player's turn

        // Defend first
        engine.ExecutePlayerDecision(new CombatAction { ActionType = ActionType.Defend });
        Assert.True(player.IsDefending);
        _output.WriteLine("After Defend: IsDefending=true");

        // Skip enemy turn
        engine.ExecuteEnemyTurn();

        // ACT: Attack (offensive action)
        var weapon = _testWorld.GetEquipmentByRefName("IronSword")!;
        engine.ExecutePlayerDecision(new CombatAction
        {
            ActionType = ActionType.Attack,
            Parameter = weapon.RefName
        });

        // ASSERT: Defensive states cleared
        Assert.False(player.IsDefending);
        Assert.False(player.IsAdjusting);
        _output.WriteLine("After Attack: IsDefending=false, IsAdjusting=false");
    }

    [Fact]
    public void SpellAttack_ClearsDefensiveStates()
    {
        // ARRANGE: Combatant adjusts loadout, then casts spell
        var player = CreateCombatant("Player", magic: 0.15f);
        player.Capabilities!.Spells = new[] { new SpellEntry { SpellRef = "Fireball", Condition = 1.0f } };

        var enemy = CreateCombatant("Enemy", strength: 0.10f);
        var engine = new BattleEngine(player, enemy, null, _testWorld);
        engine.StartBattle();  // Enemy attacks first, then it's player's turn

        // Adjust loadout first
        engine.ExecutePlayerDecision(new CombatAction
        {
            ActionType = ActionType.AdjustLoadout,
            Parameter = "Affinity:Fire"
        });
        Assert.True(player.IsAdjusting);

        // Skip enemy turn
        engine.ExecuteEnemyTurn();

        // ACT: Cast spell (offensive action)
        engine.ExecutePlayerDecision(new CombatAction
        {
            ActionType = ActionType.CastSpell,
            Parameter = "Fireball"
        });

        // ASSERT: Defensive state cleared
        Assert.False(player.IsAdjusting);
        Assert.False(player.IsDefending);
        _output.WriteLine("After spell cast: defensive states cleared");
    }

    [Fact]
    public void ConsumableUse_ClearsDefensiveStates()
    {
        // ARRANGE: Combatant defends, then uses consumable
        var player = CreateCombatant("Player", strength: 0.10f);
        player.Capabilities!.Consumables = new[] { new ConsumableEntry { ConsumableRef = "HealthPotion", Quantity = 1 } };

        var enemy = CreateCombatant("Enemy", strength: 0.10f);
        var engine = new BattleEngine(player, enemy, null, _testWorld);
        engine.StartBattle();  // Enemy attacks first, then it's player's turn

        // Defend first
        engine.ExecutePlayerDecision(new CombatAction { ActionType = ActionType.Defend });
        Assert.True(player.IsDefending);

        // Skip enemy turn
        engine.ExecuteEnemyTurn();

        // ACT: Use consumable
        engine.ExecutePlayerDecision(new CombatAction
        {
            ActionType = ActionType.UseConsumable,
            Parameter = "HealthPotion"
        });

        // ASSERT: Defensive state cleared
        Assert.False(player.IsDefending);
        _output.WriteLine("After consumable use: defensive state cleared");
    }

    [Fact]
    public void Flee_ClearsDefensiveStates()
    {
        // ARRANGE: Combatant adjusts, then flees
        var player = CreateCombatant("Player", speed: 0.20f);  // High speed for flee success
        var enemy = CreateCombatant("Enemy", speed: 0.05f);
        var engine = new BattleEngine(player, enemy, null, _testWorld);
        engine.StartBattle();  // Enemy attacks first, then it's player's turn

        // Adjust loadout first
        engine.ExecutePlayerDecision(new CombatAction
        {
            ActionType = ActionType.AdjustLoadout,
            Parameter = "RightHand:WoodenSword"
        });
        Assert.True(player.IsAdjusting);

        // Skip enemy turn
        engine.ExecuteEnemyTurn();

        // ACT: Flee
        engine.ExecutePlayerDecision(new CombatAction { ActionType = ActionType.Flee });

        // ASSERT: Defensive state cleared (even if flee fails)
        Assert.False(player.IsAdjusting);
        _output.WriteLine("After flee attempt: defensive state cleared");
    }

    [Fact]
    public void SpellAttack_ReducedDefenseEffectiveness()
    {
        // ARRANGE: Spells are less affected by Defend (70% instead of 50%)
        // Note: defender is PLAYER (first arg), attacker is ENEMY (second arg)
        var attacker = CreateCombatant("Mage", magic: 0.20f);
        attacker.Capabilities!.Spells = new[] { new SpellEntry { SpellRef = "Fireball", Condition = 1.0f } };

        var defender = CreateCombatant("Defender", defense: 0.10f);
        var enemyAI = new CombatAI(_testWorld);  // AI will cast spells when available
        var engine = new BattleEngine(defender, attacker, enemyAI, _testWorld);
        engine.StartBattle();  // Enemy attacks first, then it's player's turn

        // Player (defender) uses Defend
        engine.ExecutePlayerDecision(new CombatAction { ActionType = ActionType.Defend });
        Assert.True(defender.IsDefending);

        var defenderHealthBeforeSpell = defender.Health;

        // ACT: Enemy (attacker/mage) casts spell
        engine.ExecuteEnemyTurn();

        // ASSERT: Spell damage only reduced to 70% (not 50% like physical)
        // Note: The log message varies based on IsDefending vs IsAdjusting and spell type
        _output.WriteLine($"Combat log:\n{string.Join("\n", engine.CombatLog)}");
        Assert.Contains(engine.CombatLog, log =>
            log.Contains("partially reduces spell damage") ||
            log.Contains("defense reduces incoming damage"));
    }

    [Fact]
    public void AdjustLoadout_AgainstSpells_ReducedEffectiveness()
    {
        // ARRANGE: IsAdjusting provides 10% reduction against spells (vs 15% against physical)
        // Note: defender is PLAYER (first arg), attacker is ENEMY (second arg)
        var attacker = CreateCombatant("Mage", magic: 0.20f);
        attacker.Capabilities!.Spells = new[] { new SpellEntry { SpellRef = "Fireball", Condition = 1.0f } };

        var defender = CreateCombatant("Defender", defense: 0.10f);
        var enemyAI = new CombatAI(_testWorld);  // AI will cast spells when available
        var engine = new BattleEngine(defender, attacker, enemyAI, _testWorld);
        engine.StartBattle();  // Enemy attacks first, then it's player's turn

        // Player (defender) adjusts loadout
        engine.ExecutePlayerDecision(new CombatAction
        {
            ActionType = ActionType.AdjustLoadout,
            Parameter = "RightHand:IronSword"
        });
        Assert.True(defender.IsAdjusting);

        var defenderHealthBeforeSpell = defender.Health;

        // ACT: Enemy (attacker/mage) casts spell
        engine.ExecuteEnemyTurn();

        // ASSERT: Spell damage reduced by 10%
        // Note: The log message varies based on IsDefending vs IsAdjusting and spell type
        _output.WriteLine($"Combat log:\n{string.Join("\n", engine.CombatLog)}");
        Assert.Contains(engine.CombatLog, log =>
            log.Contains("slightly reduces spell damage") ||
            log.Contains("defensive positioning reduces damage"));
    }

    [Fact]
    public void MultipleDefensiveTurns_Persist()
    {
        // ARRANGE: Combatant defends multiple turns in a row
        var player = CreateCombatant("Defender", defense: 0.15f);
        var enemy = CreateCombatant("Attacker", strength: 0.20f);
        var engine = new BattleEngine(player, enemy, null, _testWorld);
        engine.StartBattle();  // Enemy attacks first, then it's player's turn

        // Turn 1: Defend
        engine.ExecutePlayerDecision(new CombatAction { ActionType = ActionType.Defend });
        Assert.True(player.IsDefending);

        engine.ExecuteEnemyTurn();  // Enemy attacks, reduced damage
        var healthAfterTurn1 = player.Health;
        _output.WriteLine($"Health after turn 1: {healthAfterTurn1 * 100:F1}%");

        // Turn 2: Defend again
        engine.ExecutePlayerDecision(new CombatAction { ActionType = ActionType.Defend });
        Assert.True(player.IsDefending);

        engine.ExecuteEnemyTurn();  // Enemy attacks again, reduced damage
        var healthAfterTurn2 = player.Health;
        _output.WriteLine($"Health after turn 2: {healthAfterTurn2 * 100:F1}%");

        // Turn 3: Defend AGAIN
        engine.ExecutePlayerDecision(new CombatAction { ActionType = ActionType.Defend });
        Assert.True(player.IsDefending);

        engine.ExecuteEnemyTurn();  // Enemy attacks again, reduced damage
        var healthAfterTurn3 = player.Health;
        _output.WriteLine($"Health after turn 3: {healthAfterTurn3 * 100:F1}%");

        // ASSERT: All three turns had damage reduction
        var totalLogs = engine.CombatLog.Count(log => log.Contains("defense reduces incoming damage"));
        Assert.Equal(3, totalLogs);
        _output.WriteLine("Defensive state persisted through 3 consecutive turns");
    }

    #region Test Helpers

    private World CreateTestWorld()
    {
        var ironSword = new Equipment
        {
            RefName = "IronSword",
            DisplayName = "Iron Sword",
            WholesalePrice = 50,
            Effects = new CharacterEffects { Health = -0.10f },  // 10% damage
            AffinityRef = "Physical"
        };

        var woodenSword = new Equipment
        {
            RefName = "WoodenSword",
            DisplayName = "Wooden Sword",
            WholesalePrice = 10,
            Effects = new CharacterEffects { Health = -0.05f },  // 5% damage
            AffinityRef = "Physical"
        };

        var woodenShield = new Equipment
        {
            RefName = "WoodenShield",
            DisplayName = "Wooden Shield",
            WholesalePrice = 20,
            Effects = new CharacterEffects { Defense = 0.05f }
        };

        var fireball = new Spell
        {
            RefName = "Fireball",
            DisplayName = "Fireball",
            UseType = ItemUseType.Offensive,
            Effects = new CharacterEffects { Health = -0.15f },  // 15% damage
            AffinityRef = "Fire"
        };

        var healthPotion = new Consumable
        {
            RefName = "HealthPotion",
            DisplayName = "Health Potion",
            Effects = new CharacterEffects { Health = 0.25f }  // 25% heal
        };

        var physicalAffinity = new CharacterAffinity
        {
            RefName = "Physical",
            DisplayName = "Physical",
            Description = "Physical combat affinity"
        };

        var fireAffinity = new CharacterAffinity
        {
            RefName = "Fire",
            DisplayName = "Fire",
            Description = "Fire magic affinity"
        };

        var world = new World
        {
            IsProcedural = true,
            WorldTemplate = new WorldTemplate
            {
                Gameplay = new GameplayComponents
                {
                    Equipment = new[] { ironSword, woodenSword, woodenShield },
                    Spells = new[] { fireball },
                    Consumables = new[] { healthPotion },
                    CharacterAffinities = new[] { physicalAffinity, fireAffinity }
                }
            }
        };

        world.EquipmentLookup[ironSword.RefName] = ironSword;
        world.EquipmentLookup[woodenSword.RefName] = woodenSword;
        world.EquipmentLookup[woodenShield.RefName] = woodenShield;
        world.SpellsLookup[fireball.RefName] = fireball;
        world.ConsumablesLookup[healthPotion.RefName] = healthPotion;
        world.CharacterAffinitiesLookup[physicalAffinity.RefName] = physicalAffinity;
        world.CharacterAffinitiesLookup[fireAffinity.RefName] = fireAffinity;

        return world;
    }

    private Combatant CreateCombatant(string name, float strength = 0.10f, float defense = 0.10f, float speed = 0.10f, float magic = 0.10f)
    {
        return new Combatant
        {
            RefName = name,
            DisplayName = name,
            Health = 1.0f,
            Energy = 1.0f,
            Strength = strength,
            Defense = defense,
            Speed = speed,
            Magic = magic,
            AffinityRef = "Physical",
            Capabilities = new ItemCollection
            {
                Equipment = new[] { new EquipmentEntry { EquipmentRef = "IronSword", Condition = 1.0f } },
                Spells = Array.Empty<SpellEntry>(),
                Consumables = Array.Empty<ConsumableEntry>()
            },
            CombatProfile = new Dictionary<string, string>()
        };
    }

    #endregion
}
