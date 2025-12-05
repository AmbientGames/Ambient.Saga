using Ambient.Domain;
using Ambient.Saga.Engine.Domain.Rpg.Battle;
using Xunit;

namespace Ambient.Saga.Engine.Tests.Rpg.Battle;

/// <summary>
/// Tests for BattleTriggerEvaluator which evaluates mid-battle dialogue triggers.
/// </summary>
public class BattleTriggerEvaluatorTests
{
    #region HealthBelow Trigger Tests

    [Fact]
    public void Evaluate_HealthBelow_TriggersWhenEnemyHealthIsBelow()
    {
        // Arrange
        var evaluator = new BattleTriggerEvaluator();
        var triggers = new[]
        {
            new CharacterTrigger
            {
                Condition = BattleTriggerCondition.HealthBelow,
                Value = 50f, // Trigger when enemy health is below 50%
                DialogueTreeRef = "BossPhase2",
                StartNodeId = "rage_mode"
            }
        };

        var context = new BattleTriggerContext
        {
            EnemyHealthPercent = 30f, // Enemy at 30% health
            PlayerHealthPercent = 80f,
            TurnNumber = 5
        };

        // Act
        var results = evaluator.Evaluate(triggers, context);

        // Assert
        Assert.Single(results);
        Assert.Equal("BossPhase2", results[0].DialogueTreeRef);
        Assert.Equal("rage_mode", results[0].StartNodeId);
    }

    [Fact]
    public void Evaluate_HealthBelow_DoesNotTriggerWhenEnemyHealthIsAbove()
    {
        // Arrange
        var evaluator = new BattleTriggerEvaluator();
        var triggers = new[]
        {
            new CharacterTrigger
            {
                Condition = BattleTriggerCondition.HealthBelow,
                Value = 50f,
                DialogueTreeRef = "BossPhase2"
            }
        };

        var context = new BattleTriggerContext
        {
            EnemyHealthPercent = 75f, // Enemy at 75% health
            PlayerHealthPercent = 80f,
            TurnNumber = 5
        };

        // Act
        var results = evaluator.Evaluate(triggers, context);

        // Assert
        Assert.Empty(results);
    }

    #endregion

    #region HealthAbove Trigger Tests

    [Fact]
    public void Evaluate_HealthAbove_TriggersWhenEnemyHealthIsAbove()
    {
        // Arrange
        var evaluator = new BattleTriggerEvaluator();
        var triggers = new[]
        {
            new CharacterTrigger
            {
                Condition = BattleTriggerCondition.HealthAbove,
                Value = 80f, // Trigger when enemy health is above 80%
                DialogueTreeRef = "BossIntro",
                StartNodeId = "taunt"
            }
        };

        var context = new BattleTriggerContext
        {
            EnemyHealthPercent = 95f, // Enemy at 95% health
            PlayerHealthPercent = 80f,
            TurnNumber = 1
        };

        // Act
        var results = evaluator.Evaluate(triggers, context);

        // Assert
        Assert.Single(results);
        Assert.Equal("BossIntro", results[0].DialogueTreeRef);
    }

    [Fact]
    public void Evaluate_HealthAbove_DoesNotTriggerWhenEnemyHealthIsBelow()
    {
        // Arrange
        var evaluator = new BattleTriggerEvaluator();
        var triggers = new[]
        {
            new CharacterTrigger
            {
                Condition = BattleTriggerCondition.HealthAbove,
                Value = 80f,
                DialogueTreeRef = "BossIntro"
            }
        };

        var context = new BattleTriggerContext
        {
            EnemyHealthPercent = 60f, // Enemy at 60% health
            PlayerHealthPercent = 80f,
            TurnNumber = 5
        };

        // Act
        var results = evaluator.Evaluate(triggers, context);

        // Assert
        Assert.Empty(results);
    }

    #endregion

    #region PlayerHealthBelow Trigger Tests

    [Fact]
    public void Evaluate_PlayerHealthBelow_TriggersWhenPlayerHealthIsBelow()
    {
        // Arrange
        var evaluator = new BattleTriggerEvaluator();
        var triggers = new[]
        {
            new CharacterTrigger
            {
                Condition = BattleTriggerCondition.PlayerHealthBelow,
                Value = 25f, // Trigger when player health is below 25%
                DialogueTreeRef = "BossMocking",
                StartNodeId = "you_are_weak"
            }
        };

        var context = new BattleTriggerContext
        {
            EnemyHealthPercent = 80f,
            PlayerHealthPercent = 15f, // Player at 15% health
            TurnNumber = 8
        };

        // Act
        var results = evaluator.Evaluate(triggers, context);

        // Assert
        Assert.Single(results);
        Assert.Equal("BossMocking", results[0].DialogueTreeRef);
        Assert.Equal("you_are_weak", results[0].StartNodeId);
    }

    #endregion

    #region TurnNumber Trigger Tests

    [Fact]
    public void Evaluate_TurnNumber_TriggersWhenTurnReached()
    {
        // Arrange
        var evaluator = new BattleTriggerEvaluator();
        var triggers = new[]
        {
            new CharacterTrigger
            {
                Condition = BattleTriggerCondition.TurnNumber,
                Value = 10f, // Trigger at turn 10
                DialogueTreeRef = "BossEnraged",
                StartNodeId = "enough_games"
            }
        };

        var context = new BattleTriggerContext
        {
            EnemyHealthPercent = 50f,
            PlayerHealthPercent = 50f,
            TurnNumber = 10 // Exactly at turn 10
        };

        // Act
        var results = evaluator.Evaluate(triggers, context);

        // Assert
        Assert.Single(results);
        Assert.Equal("BossEnraged", results[0].DialogueTreeRef);
    }

    [Fact]
    public void Evaluate_TurnNumber_TriggersAfterTurnReached()
    {
        // Arrange
        var evaluator = new BattleTriggerEvaluator();
        var triggers = new[]
        {
            new CharacterTrigger
            {
                Condition = BattleTriggerCondition.TurnNumber,
                Value = 10f, // Trigger at turn 10+
                DialogueTreeRef = "BossEnraged"
            }
        };

        var context = new BattleTriggerContext
        {
            TurnNumber = 15 // Past turn 10
        };

        // Act
        var results = evaluator.Evaluate(triggers, context);

        // Assert
        Assert.Single(results);
    }

    [Fact]
    public void Evaluate_TurnNumber_DoesNotTriggerBeforeTurn()
    {
        // Arrange
        var evaluator = new BattleTriggerEvaluator();
        var triggers = new[]
        {
            new CharacterTrigger
            {
                Condition = BattleTriggerCondition.TurnNumber,
                Value = 10f,
                DialogueTreeRef = "BossEnraged"
            }
        };

        var context = new BattleTriggerContext
        {
            TurnNumber = 5 // Before turn 10
        };

        // Act
        var results = evaluator.Evaluate(triggers, context);

        // Assert
        Assert.Empty(results);
    }

    #endregion

    #region StanceChanged Trigger Tests

    [Fact]
    public void Evaluate_StanceChanged_TriggersWhenStanceChanges()
    {
        // Arrange
        var evaluator = new BattleTriggerEvaluator();
        var triggers = new[]
        {
            new CharacterTrigger
            {
                Condition = BattleTriggerCondition.StanceChanged,
                DialogueTreeRef = "BossStanceChange",
                StartNodeId = "new_tactics"
            }
        };

        var context = new BattleTriggerContext
        {
            StanceJustChanged = true
        };

        // Act
        var results = evaluator.Evaluate(triggers, context);

        // Assert
        Assert.Single(results);
        Assert.Equal("BossStanceChange", results[0].DialogueTreeRef);
    }

    [Fact]
    public void Evaluate_StanceChanged_DoesNotTriggerWithoutChange()
    {
        // Arrange
        var evaluator = new BattleTriggerEvaluator();
        var triggers = new[]
        {
            new CharacterTrigger
            {
                Condition = BattleTriggerCondition.StanceChanged,
                DialogueTreeRef = "BossStanceChange"
            }
        };

        var context = new BattleTriggerContext
        {
            StanceJustChanged = false
        };

        // Act
        var results = evaluator.Evaluate(triggers, context);

        // Assert
        Assert.Empty(results);
    }

    #endregion

    #region AffinityChanged Trigger Tests

    [Fact]
    public void Evaluate_AffinityChanged_TriggersWhenAffinityChanges()
    {
        // Arrange
        var evaluator = new BattleTriggerEvaluator();
        var triggers = new[]
        {
            new CharacterTrigger
            {
                Condition = BattleTriggerCondition.AffinityChanged,
                DialogueTreeRef = "BossAffinityChange",
                StartNodeId = "elemental_shift"
            }
        };

        var context = new BattleTriggerContext
        {
            AffinityJustChanged = true
        };

        // Act
        var results = evaluator.Evaluate(triggers, context);

        // Assert
        Assert.Single(results);
        Assert.Equal("BossAffinityChange", results[0].DialogueTreeRef);
    }

    #endregion

    #region OnVictory Trigger Tests

    [Fact]
    public void Evaluate_OnVictory_TriggersWhenPlayerWins()
    {
        // Arrange
        var evaluator = new BattleTriggerEvaluator();
        var triggers = new[]
        {
            new CharacterTrigger
            {
                Condition = BattleTriggerCondition.OnVictory,
                DialogueTreeRef = "BossDefeat",
                StartNodeId = "final_words"
            }
        };

        var context = new BattleTriggerContext
        {
            BattleEnded = true,
            PlayerVictory = true
        };

        // Act
        var results = evaluator.Evaluate(triggers, context);

        // Assert
        Assert.Single(results);
        Assert.Equal("BossDefeat", results[0].DialogueTreeRef);
        Assert.Equal("final_words", results[0].StartNodeId);
    }

    [Fact]
    public void Evaluate_OnVictory_DoesNotTriggerWhenPlayerLoses()
    {
        // Arrange
        var evaluator = new BattleTriggerEvaluator();
        var triggers = new[]
        {
            new CharacterTrigger
            {
                Condition = BattleTriggerCondition.OnVictory,
                DialogueTreeRef = "BossDefeat"
            }
        };

        var context = new BattleTriggerContext
        {
            BattleEnded = true,
            PlayerVictory = false // Player lost
        };

        // Act
        var results = evaluator.Evaluate(triggers, context);

        // Assert
        Assert.Empty(results);
    }

    [Fact]
    public void Evaluate_OnVictory_DoesNotTriggerMidBattle()
    {
        // Arrange
        var evaluator = new BattleTriggerEvaluator();
        var triggers = new[]
        {
            new CharacterTrigger
            {
                Condition = BattleTriggerCondition.OnVictory,
                DialogueTreeRef = "BossDefeat"
            }
        };

        var context = new BattleTriggerContext
        {
            BattleEnded = false, // Battle still ongoing
            PlayerVictory = true
        };

        // Act
        var results = evaluator.Evaluate(triggers, context);

        // Assert
        Assert.Empty(results);
    }

    #endregion

    #region OnDefeat Trigger Tests

    [Fact]
    public void Evaluate_OnDefeat_TriggersWhenPlayerLoses()
    {
        // Arrange
        var evaluator = new BattleTriggerEvaluator();
        var triggers = new[]
        {
            new CharacterTrigger
            {
                Condition = BattleTriggerCondition.OnDefeat,
                DialogueTreeRef = "BossVictory",
                StartNodeId = "you_fool"
            }
        };

        var context = new BattleTriggerContext
        {
            BattleEnded = true,
            PlayerVictory = false
        };

        // Act
        var results = evaluator.Evaluate(triggers, context);

        // Assert
        Assert.Single(results);
        Assert.Equal("BossVictory", results[0].DialogueTreeRef);
    }

    #endregion

    #region OnceOnly Trigger Tests

    [Fact]
    public void Evaluate_OnceOnlyTrigger_FiresOnlyOnce()
    {
        // Arrange
        var evaluator = new BattleTriggerEvaluator();
        var triggers = new[]
        {
            new CharacterTrigger
            {
                Condition = BattleTriggerCondition.HealthBelow,
                Value = 50f,
                DialogueTreeRef = "BossPhase2",
                OnceOnly = true
            }
        };

        var context = new BattleTriggerContext
        {
            EnemyHealthPercent = 30f // Below 50%
        };

        // Act - First evaluation
        var results1 = evaluator.Evaluate(triggers, context);

        // Assert - Should fire
        Assert.Single(results1);
        Assert.True(results1[0].ConsumeAfterFiring);

        // Act - Second evaluation with same context
        var results2 = evaluator.Evaluate(triggers, context);

        // Assert - Should not fire again
        Assert.Empty(results2);
    }

    [Fact]
    public void Evaluate_NonOnceOnlyTrigger_CanFireMultipleTimes()
    {
        // Arrange
        var evaluator = new BattleTriggerEvaluator();
        var triggers = new[]
        {
            new CharacterTrigger
            {
                Condition = BattleTriggerCondition.HealthBelow,
                Value = 50f,
                DialogueTreeRef = "BossPhase2",
                OnceOnly = false // Can fire multiple times
            }
        };

        var context = new BattleTriggerContext
        {
            EnemyHealthPercent = 30f
        };

        // Act - First evaluation
        var results1 = evaluator.Evaluate(triggers, context);
        Assert.Single(results1);

        // Act - Second evaluation
        var results2 = evaluator.Evaluate(triggers, context);

        // Assert - Should fire again
        Assert.Single(results2);
        Assert.False(results2[0].ConsumeAfterFiring);
    }

    [Fact]
    public void Reset_ClearsOnceOnlyTriggers()
    {
        // Arrange
        var evaluator = new BattleTriggerEvaluator();
        var triggers = new[]
        {
            new CharacterTrigger
            {
                Condition = BattleTriggerCondition.HealthBelow,
                Value = 50f,
                DialogueTreeRef = "BossPhase2",
                OnceOnly = true
            }
        };

        var context = new BattleTriggerContext { EnemyHealthPercent = 30f };

        // Fire trigger once
        evaluator.Evaluate(triggers, context);

        // Act - Reset
        evaluator.Reset();

        // Second evaluation after reset
        var results = evaluator.Evaluate(triggers, context);

        // Assert - Should fire again after reset
        Assert.Single(results);
    }

    #endregion

    #region Multiple Triggers Tests

    [Fact]
    public void Evaluate_MultipleTriggers_CanFireMultipleAtOnce()
    {
        // Arrange
        var evaluator = new BattleTriggerEvaluator();
        var triggers = new[]
        {
            new CharacterTrigger
            {
                Condition = BattleTriggerCondition.HealthBelow,
                Value = 50f,
                DialogueTreeRef = "LowHealth"
            },
            new CharacterTrigger
            {
                Condition = BattleTriggerCondition.TurnNumber,
                Value = 5f,
                DialogueTreeRef = "Turn5"
            }
        };

        var context = new BattleTriggerContext
        {
            EnemyHealthPercent = 30f, // Below 50%
            TurnNumber = 7 // Past turn 5
        };

        // Act
        var results = evaluator.Evaluate(triggers, context);

        // Assert - Both triggers should fire
        Assert.Equal(2, results.Count);
        Assert.Contains(results, r => r.DialogueTreeRef == "LowHealth");
        Assert.Contains(results, r => r.DialogueTreeRef == "Turn5");
    }

    #endregion

    #region Null/Empty Triggers Tests

    [Fact]
    public void Evaluate_NullTriggers_ReturnsEmpty()
    {
        // Arrange
        var evaluator = new BattleTriggerEvaluator();
        var context = new BattleTriggerContext { EnemyHealthPercent = 50f };

        // Act
        var results = evaluator.Evaluate(null, context);

        // Assert
        Assert.Empty(results);
    }

    [Fact]
    public void Evaluate_EmptyTriggers_ReturnsEmpty()
    {
        // Arrange
        var evaluator = new BattleTriggerEvaluator();
        var context = new BattleTriggerContext { EnemyHealthPercent = 50f };

        // Act
        var results = evaluator.Evaluate(Array.Empty<CharacterTrigger>(), context);

        // Assert
        Assert.Empty(results);
    }

    #endregion

    #region ConsumeTrigger Tests

    [Fact]
    public void ConsumeTrigger_ManuallyConsumesTrigger()
    {
        // Arrange
        var evaluator = new BattleTriggerEvaluator();
        var triggers = new[]
        {
            new CharacterTrigger
            {
                Condition = BattleTriggerCondition.HealthBelow,
                Value = 50f,
                DialogueTreeRef = "BossPhase2",
                OnceOnly = true
            }
        };

        var context = new BattleTriggerContext { EnemyHealthPercent = 30f };

        // Manually consume before first evaluation
        evaluator.ConsumeTrigger(0);

        // Act
        var results = evaluator.Evaluate(triggers, context);

        // Assert - Should not fire because already consumed
        Assert.Empty(results);
    }

    [Fact]
    public void IsTriggerConsumed_ReturnsCorrectStatus()
    {
        // Arrange
        var evaluator = new BattleTriggerEvaluator();

        // Act & Assert
        Assert.False(evaluator.IsTriggerConsumed(0));

        evaluator.ConsumeTrigger(0);
        Assert.True(evaluator.IsTriggerConsumed(0));
        Assert.False(evaluator.IsTriggerConsumed(1));
    }

    #endregion
}
