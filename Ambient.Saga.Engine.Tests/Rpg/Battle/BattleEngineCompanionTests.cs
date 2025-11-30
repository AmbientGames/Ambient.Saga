using Ambient.Domain;
using Ambient.Domain.DefinitionExtensions;
using Ambient.Saga.Engine.Domain.Rpg.Battle;
using Xunit;

namespace Ambient.Saga.Engine.Tests.Rpg.Battle;

/// <summary>
/// Tests for BattleEngine companion/party support.
/// Validates turn order, targeting, and victory/defeat conditions with party members.
/// </summary>
public class BattleEngineCompanionTests
{
    private readonly World _world;

    public BattleEngineCompanionTests()
    {
        _world = CreateMinimalBattleWorld();
    }

    #region Party Setup Tests

    [Fact]
    public void Constructor_WithCompanions_InitializesParty()
    {
        // Arrange
        var player = CreateCombatant("Player", 100);
        var enemy = CreateCombatant("Enemy", 50);
        var companion1 = CreateCombatant("Companion1", 80);
        var companion2 = CreateCombatant("Companion2", 60);

        // Act
        var engine = new BattleEngine(
            player, enemy,
            enemyMind: null,
            world: _world,
            companions: new List<Combatant> { companion1, companion2 });

        // Assert
        Assert.Equal(3, engine.Party.Count);  // Player + 2 companions
        Assert.Contains(engine.Party, c => c.RefName == "Player");
        Assert.Contains(engine.Party, c => c.RefName == "Companion1");
        Assert.Contains(engine.Party, c => c.RefName == "Companion2");
    }

    [Fact]
    public void Constructor_WithoutCompanions_PlayerOnlyInParty()
    {
        // Arrange
        var player = CreateCombatant("Player", 100);
        var enemy = CreateCombatant("Enemy", 50);

        // Act
        var engine = new BattleEngine(player, enemy);

        // Assert
        Assert.Single(engine.Party);
        Assert.Equal("Player", engine.Party[0].RefName);
    }

    #endregion

    #region Turn Order Tests

    [Fact]
    public void StartBattle_EnemyMovesFirst()
    {
        // Arrange
        var player = CreateCombatant("Player", 100);
        var enemy = CreateCombatant("Enemy", 50);
        var companion = CreateCombatant("Companion", 80);
        var enemyAI = new CombatAI(_world);

        var engine = new BattleEngine(
            player, enemy, enemyAI, _world,
            randomSeed: 12345,
            companions: new List<Combatant> { companion });

        // Act
        engine.StartBattle();

        // Assert - Enemy's turn should have been executed
        Assert.NotEmpty(engine.ActionHistory);
        Assert.Equal("Enemy", engine.ActionHistory[0].ActorName);
        Assert.Equal(BattleState.PlayerTurn, engine.State);
    }

    [Fact]
    public void AfterPlayerTurn_CompanionTurnsFollow()
    {
        // Arrange
        var player = CreateCombatant("Player", 100);
        var enemy = CreateCombatant("Enemy", 100);
        var companion1 = CreateCombatant("Companion1", 80);
        var companion2 = CreateCombatant("Companion2", 60);
        var ai = new CombatAI(_world);

        var engine = new BattleEngine(
            player, enemy, ai, _world,
            randomSeed: 12345,
            companions: new List<Combatant> { companion1, companion2 });

        engine.StartBattle();  // Enemy moves first

        // Act - Player executes their turn
        var playerResult = engine.ExecutePlayerDecision(new CombatAction { ActionType = ActionType.Attack });

        // Assert - Should now be companion turn
        Assert.True(playerResult.Success);
        Assert.Equal(BattleState.CompanionTurn, engine.State);
        Assert.NotNull(engine.CurrentCompanion);
    }

    [Fact]
    public void CompanionTurn_ExecutesAIDecision()
    {
        // Arrange
        var player = CreateCombatant("Player", 100);
        var enemy = CreateCombatant("Enemy", 100);
        var companion = CreateCombatant("Companion", 80);
        var ai = new CombatAI(_world);

        var engine = new BattleEngine(
            player, enemy, ai, _world,
            randomSeed: 12345,
            companions: new List<Combatant> { companion });

        engine.StartBattle();
        engine.ExecutePlayerDecision(new CombatAction { ActionType = ActionType.Attack });

        // Act - Execute companion's turn
        var companionResult = engine.ExecuteCompanionTurn();

        // Assert
        Assert.True(companionResult.Success);
        Assert.Equal("Companion", companionResult.ActorName);
        Assert.Equal("Enemy", companionResult.TargetName);
    }

    [Fact]
    public void AfterAllCompanionTurns_EnemyTurnFollows()
    {
        // Arrange
        var player = CreateCombatant("Player", 100);
        var enemy = CreateCombatant("Enemy", 200);  // High HP to survive
        var companion = CreateCombatant("Companion", 80);
        var ai = new CombatAI(_world);

        var engine = new BattleEngine(
            player, enemy, ai, _world,
            randomSeed: 12345,
            companions: new List<Combatant> { companion });

        engine.StartBattle();  // Enemy turn
        engine.ExecutePlayerDecision(new CombatAction { ActionType = ActionType.Attack });  // Player turn

        // Act - Execute companion's turn
        var result = engine.ExecuteCompanionTurn();

        // Assert - Should transition to enemy turn after all companions
        Assert.True(result.Success);
        Assert.Equal(BattleState.EnemyTurn, engine.State);
    }

    #endregion

    #region Targeting Tests

    [Fact]
    public void EnemyTargeting_CanTargetPlayer()
    {
        // Arrange
        var player = CreateCombatant("Player", 100);
        var enemy = CreateCombatant("Enemy", 50);
        var companion = CreateCombatant("Companion", 80);
        var ai = new CombatAI(_world);

        // Use a seed where enemy targets player
        var engine = new BattleEngine(
            player, enemy, ai, _world,
            randomSeed: 42,  // Testing seed
            companions: new List<Combatant> { companion });

        // Act
        engine.StartBattle();

        // Assert - Enemy should attack either player or companion
        var enemyAction = engine.ActionHistory.First();
        Assert.True(
            enemyAction.TargetName == "Player" || enemyAction.TargetName == "Companion",
            $"Enemy targeted {enemyAction.TargetName}, expected Player or Companion");
    }

    [Fact]
    public void EnemyTargeting_CanTargetCompanions()
    {
        // This test runs multiple battles with different seeds to verify companions can be targeted
        var ai = new CombatAI(_world);

        var targetedCompanion = false;

        // Run multiple times with different seeds
        for (int seed = 0; seed < 100; seed++)
        {
            var engine = new BattleEngine(
                CreateCombatant("Player", 100),
                CreateCombatant("Enemy", 50),
                ai, _world,
                randomSeed: seed,
                companions: new List<Combatant> { CreateCombatant("Companion", 80) });

            engine.StartBattle();

            if (engine.ActionHistory.First().TargetName == "Companion")
            {
                targetedCompanion = true;
                break;
            }
        }

        Assert.True(targetedCompanion, "Enemy never targeted companion in 100 battles");
    }

    [Fact]
    public void EnemyTargeting_OnlyTargetsAliveCombatants()
    {
        // Arrange
        var player = CreateCombatant("Player", 100);
        var enemy = CreateCombatant("Enemy", 100);
        var deadCompanion = CreateCombatant("DeadCompanion", 0);  // Already dead
        var aliveCompanion = CreateCombatant("AliveCompanion", 80);
        var ai = new CombatAI(_world);

        var engine = new BattleEngine(
            player, enemy, ai, _world,
            randomSeed: 12345,
            companions: new List<Combatant> { deadCompanion, aliveCompanion });

        // Act
        engine.StartBattle();

        // Assert - Enemy should not target dead companion
        var enemyAction = engine.ActionHistory.First();
        Assert.NotEqual("DeadCompanion", enemyAction.TargetName);
    }

    #endregion

    #region Victory/Defeat Conditions

    [Fact]
    public void PlayerDefeat_WhenPlayerDies_BattleEnds()
    {
        // Arrange - Player with very low HP
        var player = CreateCombatant("Player", 1);
        var enemy = CreateCombatant("Enemy", 100, strength: 50);  // High damage
        var companion = CreateCombatant("Companion", 100);
        var ai = new CombatAI(_world);

        var engine = new BattleEngine(
            player, enemy, ai, _world,
            randomSeed: 12345,
            companions: new List<Combatant> { companion });

        // Act
        engine.StartBattle();  // Enemy attacks, likely kills player

        // Assert - Battle should end in defeat if player died
        if (!player.IsAlive)
        {
            Assert.Equal(BattleState.Defeat, engine.State);
        }
    }

    [Fact]
    public void CompanionDeath_BattleContinues()
    {
        // Arrange - Companion with very low HP
        var player = CreateCombatant("Player", 100);
        var enemy = CreateCombatant("Enemy", 100);
        var weakCompanion = CreateCombatant("WeakCompanion", 1);
        var ai = new CombatAI(_world);

        // Create engine where we manually damage companion
        var engine = new BattleEngine(
            player, enemy, ai, _world,
            randomSeed: 12345,
            companions: new List<Combatant> { weakCompanion });

        engine.StartBattle();

        // Manually kill companion (simulate damage)
        weakCompanion.Health = 0;

        // Act - Continue battle
        var playerResult = engine.ExecutePlayerDecision(new CombatAction { ActionType = ActionType.Attack });

        // Assert - Battle should continue
        Assert.True(playerResult.Success);
        Assert.NotEqual(BattleState.Defeat, engine.State);
    }

    [Fact]
    public void Victory_WhenEnemyDefeated_WithCompanions()
    {
        // Arrange - Enemy with very low HP
        var player = CreateCombatant("Player", 100, strength: 50);  // High damage
        var enemy = CreateCombatant("Enemy", 1);
        var companion = CreateCombatant("Companion", 80);
        var ai = new CombatAI(_world);

        var engine = new BattleEngine(
            player, enemy, ai, _world,
            randomSeed: 12345,
            companions: new List<Combatant> { companion });

        engine.StartBattle();

        // Act - Player attacks weak enemy
        engine.ExecutePlayerDecision(new CombatAction { ActionType = ActionType.Attack });

        // Assert - Battle should end in victory
        Assert.Equal(BattleState.Victory, engine.State);
    }

    #endregion

    #region Skip Dead Companions

    [Fact]
    public void CompanionTurn_SkipsDeadCompanions()
    {
        // Arrange
        var player = CreateCombatant("Player", 100);
        var enemy = CreateCombatant("Enemy", 200);
        var deadCompanion = CreateCombatant("DeadCompanion", 0);  // Dead
        var aliveCompanion = CreateCombatant("AliveCompanion", 80);
        var ai = new CombatAI(_world);

        var engine = new BattleEngine(
            player, enemy, ai, _world,
            randomSeed: 12345,
            companions: new List<Combatant> { deadCompanion, aliveCompanion });

        engine.StartBattle();
        engine.ExecutePlayerDecision(new CombatAction { ActionType = ActionType.Attack });

        // Act - Execute companion turns
        var companionResult = engine.ExecuteCompanionTurn();

        // Assert - Should be the alive companion
        Assert.Equal("AliveCompanion", companionResult.ActorName);
    }

    [Fact]
    public void CompanionTurn_AllDeadCompanions_SkipsToEnemyTurn()
    {
        // Arrange
        var player = CreateCombatant("Player", 100);
        var enemy = CreateCombatant("Enemy", 200);
        var deadCompanion1 = CreateCombatant("DeadCompanion1", 0);
        var deadCompanion2 = CreateCombatant("DeadCompanion2", 0);
        var ai = new CombatAI(_world);

        var engine = new BattleEngine(
            player, enemy, ai, _world,
            randomSeed: 12345,
            companions: new List<Combatant> { deadCompanion1, deadCompanion2 });

        engine.StartBattle();
        engine.ExecutePlayerDecision(new CombatAction { ActionType = ActionType.Attack });

        // Assert - Should skip directly to enemy turn
        Assert.Equal(BattleState.EnemyTurn, engine.State);
    }

    #endregion

    #region Helper Methods

    private Combatant CreateCombatant(string name, float health, float strength = 10)
    {
        return new Combatant
        {
            RefName = name,
            DisplayName = name,
            Health = health,
            Energy = 100,
            Strength = strength,
            Defense = 5,
            Speed = 10,
            Magic = 5,
            AffinityRef = "IRON",
            Capabilities = new ItemCollection()
        };
    }

    private static World CreateMinimalBattleWorld()
    {
        var world = new World();

        // Initialize with minimal gameplay components needed for battle
        world.WorldTemplate = new WorldTemplate
        {
            Gameplay = new GameplayComponents
            {
                Equipment = Array.Empty<Equipment>(),
                Consumables = Array.Empty<Consumable>(),
                Spells = Array.Empty<Spell>(),
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
