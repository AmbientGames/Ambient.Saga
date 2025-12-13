using Ambient.Domain;
using Ambient.Domain.Partials;
using Ambient.Infrastructure.GameLogic.Loading;

namespace Ambient.Saga.Engine.Tests;

/// <summary>
/// Tests for TriggerExpander which expands TriggerPatternRef into concrete trigger sets.
/// </summary>
public class TriggerExpanderTests
{
    private World CreateTestWorld()
    {
        var world = new World
        {
            WorldTemplate = new WorldTemplate
            {
                Gameplay = new GameplayComponents()
            }
        };

        return world;
    }

    [Fact]
    public void ExpandTriggersForSaga_WithNullItems_ReturnsEmptyList()
    {
        // Arrange
        var world = CreateTestWorld();
        var saga = new SagaArc
        {
            RefName = "TestSaga",
            Items = null
        };

        // Act
        var triggers = TriggerExpander.ExpandTriggersForSaga(saga, world);

        // Assert
        Assert.NotNull(triggers);
        Assert.Empty(triggers);
    }

    [Fact]
    public void ExpandTriggersForSaga_WithInlineTrigger_ReturnsTriggerDirectly()
    {
        // Arrange
        var world = CreateTestWorld();
        var inlineTrigger = new SagaTrigger
        {
            RefName = "shrine",
            DisplayName = "Sacred Shrine",
            EnterRadius = 25.0f
        };

        var sagaArc = new SagaArc
        {
            RefName = "TestSaga",
            Items = new object[] { inlineTrigger }
        };

        // Act
        var triggers = TriggerExpander.ExpandTriggersForSaga(sagaArc, world);

        // Assert
        Assert.Single(triggers);
        Assert.Equal("shrine", triggers[0].RefName);
        Assert.Equal("Sacred Shrine", triggers[0].DisplayName);
        Assert.Equal(25.0f, triggers[0].EnterRadius);
    }

    [Fact]
    public void ExpandTriggersForSaga_WithMultipleInlineTriggers_ReturnsAllTriggers()
    {
        // Arrange
        var world = CreateTestWorld();
        var trigger1 = new SagaTrigger { RefName = "outer", EnterRadius = 45.0f };
        var trigger2 = new SagaTrigger { RefName = "middle", EnterRadius = 25.0f };
        var trigger3 = new SagaTrigger { RefName = "inner", EnterRadius = 10.0f };

        var sagaArc = new SagaArc
        {
            RefName = "TestSaga",
            Items = new object[] { trigger1, trigger2, trigger3 }
        };

        // Act
        var triggers = TriggerExpander.ExpandTriggersForSaga(sagaArc, world);

        // Assert
        Assert.Equal(3, triggers.Count);
        Assert.Contains(triggers, t => t.RefName == "outer");
        Assert.Contains(triggers, t => t.RefName == "middle");
        Assert.Contains(triggers, t => t.RefName == "inner");
    }

    [Fact]
    public void ExpandTriggersForSaga_WithTriggerPatternRef_ExpandsPattern()
    {
        // Arrange
        var world = CreateTestWorld();

        var pattern = new SagaTriggerPattern
        {
            RefName = "StandardSaga",
            EnforceProgression = false,
            SagaTrigger = new[]
            {
                new SagaTrigger { RefName = "approach", EnterRadius = 45.0f },
                new SagaTrigger { RefName = "shrine", EnterRadius = 25.0f },
                new SagaTrigger { RefName = "altar", EnterRadius = 17.0f }
            }
        };

        world.Gameplay.SagaTriggerPatterns = new[] { pattern };

        var sagaArc = new SagaArc
        {
            RefName = "KagoshimaCastle",
            Items = new object[] { "StandardSaga" }
        };

        // Act
        var triggers = TriggerExpander.ExpandTriggersForSaga(sagaArc, world);

        // Assert
        Assert.Equal(3, triggers.Count);
        Assert.Contains(triggers, t => t.RefName == "approach" && t.EnterRadius == 45.0f);
        Assert.Contains(triggers, t => t.RefName == "shrine" && t.EnterRadius == 25.0f);
        Assert.Contains(triggers, t => t.RefName == "altar" && t.EnterRadius == 17.0f);
    }

    [Fact]
    public void ExpandTriggersForSaga_WithNonexistentPattern_ThrowsInvalidOperationException()
    {
        // Arrange
        var world = CreateTestWorld();
        world.Gameplay.SagaTriggerPatterns = Array.Empty<SagaTriggerPattern>();

        var saga = new SagaArc
        {
            RefName = "TestSaga",
            Items = new object[] { "NonexistentPattern" }
        };

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(
            () => TriggerExpander.ExpandTriggersForSaga(saga, world));

        Assert.Contains("NonexistentPattern", exception.Message);
        Assert.Contains("not found", exception.Message);
    }

    [Fact]
    public void ExpandTriggersForSaga_WithEnforceProgression_AddsQuestTokenChain()
    {
        // Arrange
        var world = CreateTestWorld();

        var pattern = new SagaTriggerPattern
        {
            RefName = "ProgressiveBoss",
            EnforceProgression = true,
            SagaTrigger = new[]
            {
                new SagaTrigger { RefName = "outer", EnterRadius = 45.0f },
                new SagaTrigger { RefName = "middle", EnterRadius = 25.0f },
                new SagaTrigger { RefName = "inner", EnterRadius = 10.0f }
            }
        };

        world.Gameplay.SagaTriggerPatterns = new[] { pattern };

        var saga = new SagaArc
        {
            RefName = "BossDungeon",
            Items = new object[] { "ProgressiveBoss" }
        };

        // Act
        var triggers = TriggerExpander.ExpandTriggersForSaga(saga, world);

        // Assert
        Assert.Equal(3, triggers.Count);

        // Triggers should be sorted by radius descending (outermost first)
        var sortedTriggers = triggers.OrderByDescending(t => t.EnterRadius).ToList();

        // Outermost trigger (45m) - no requirements, gives completion token
        var outerTrigger = sortedTriggers[0];
        Assert.Equal("outer", outerTrigger.RefName);
        Assert.Null(outerTrigger.RequiresQuestTokenRef);
        Assert.NotNull(outerTrigger.GivesQuestTokenRef);
        Assert.Single(outerTrigger.GivesQuestTokenRef);
        Assert.Equal("BossDungeon_outer_Completed", outerTrigger.GivesQuestTokenRef[0]);

        // Middle trigger (25m) - requires outer completion, gives own completion
        var middleTrigger = sortedTriggers[1];
        Assert.Equal("middle", middleTrigger.RefName);
        Assert.NotNull(middleTrigger.RequiresQuestTokenRef);
        Assert.Single(middleTrigger.RequiresQuestTokenRef);
        Assert.Equal("BossDungeon_outer_Completed", middleTrigger.RequiresQuestTokenRef[0]);
        Assert.NotNull(middleTrigger.GivesQuestTokenRef);
        Assert.Equal("BossDungeon_middle_Completed", middleTrigger.GivesQuestTokenRef[0]);

        // Inner trigger (10m) - requires middle completion, gives own completion
        var innerTrigger = sortedTriggers[2];
        Assert.Equal("inner", innerTrigger.RefName);
        Assert.NotNull(innerTrigger.RequiresQuestTokenRef);
        Assert.Single(innerTrigger.RequiresQuestTokenRef);
        Assert.Equal("BossDungeon_middle_Completed", innerTrigger.RequiresQuestTokenRef[0]);
        Assert.NotNull(innerTrigger.GivesQuestTokenRef);
        Assert.Equal("BossDungeon_inner_Completed", innerTrigger.GivesQuestTokenRef[0]);
    }

    [Fact]
    public void ExpandTriggersForSaga_WithEnforceProgressionFalse_DoesNotAddQuestTokens()
    {
        // Arrange
        var world = CreateTestWorld();

        var pattern = new SagaTriggerPattern
        {
            RefName = "SimpleSaga",
            EnforceProgression = false,
            SagaTrigger = new[]
            {
                new SagaTrigger { RefName = "outer", EnterRadius = 45.0f },
                new SagaTrigger { RefName = "inner", EnterRadius = 10.0f }
            }
        };

        world.Gameplay.SagaTriggerPatterns = new[] { pattern };

        var saga = new SagaArc
        {
            RefName = "SimpleSaga",
            Items = new object[] { "SimpleSaga" }
        };

        // Act
        var triggers = TriggerExpander.ExpandTriggersForSaga(saga, world);

        // Assert
        Assert.Equal(2, triggers.Count);
        Assert.All(triggers, trigger =>
        {
            Assert.Null(trigger.RequiresQuestTokenRef);
            Assert.Null(trigger.GivesQuestTokenRef);
        });
    }

    [Fact]
    public void ExpandTriggersForSaga_WithProgression_PreservesOriginalTriggerProperties()
    {
        // Arrange
        var world = CreateTestWorld();

        var pattern = new SagaTriggerPattern
        {
            RefName = "DetailedPattern",
            EnforceProgression = true,
            SagaTrigger = new[]
            {
                new SagaTrigger
                {
                    RefName = "boss",
                    DisplayName = "Dragon Boss",
                    Description = "A fearsome dragon",
                    EnterRadius = 50.0f,
                    Tags = "dragon,boss,fire",
                    Spawn = new[]
                    {
                        new CharacterSpawn
                        {
                            Item = "DragonBoss",
                            ItemElementName = ItemChoiceType.CharacterRef
                        }
                    }
                }
            }
        };

        world.Gameplay.SagaTriggerPatterns = new[] { pattern };

        var saga = new SagaArc
        {
            RefName = "DragonLair",
            Items = new object[] { "DetailedPattern" }
        };

        // Act
        var triggers = TriggerExpander.ExpandTriggersForSaga(saga, world);

        // Assert
        Assert.Single(triggers);
        var trigger = triggers[0];

        Assert.Equal("boss", trigger.RefName);
        Assert.Equal("Dragon Boss", trigger.DisplayName);
        Assert.Equal("A fearsome dragon", trigger.Description);
        Assert.Equal(50.0f, trigger.EnterRadius);
        Assert.Equal("dragon,boss,fire", trigger.Tags);
        Assert.NotNull(trigger.Spawn);
        Assert.Single(trigger.Spawn);

        // Should also have progression tokens
        Assert.NotNull(trigger.GivesQuestTokenRef);
        Assert.Equal("DragonLair_boss_Completed", trigger.GivesQuestTokenRef[0]);
    }

    [Fact]
    public void ExpandTriggersForSaga_WithEmptyPattern_ReturnsEmptyList()
    {
        // Arrange
        var world = CreateTestWorld();

        var pattern = new SagaTriggerPattern
        {
            RefName = "EmptyPattern",
            EnforceProgression = false,
            SagaTrigger = null
        };

        world.Gameplay.SagaTriggerPatterns = new[] { pattern };

        var saga = new SagaArc
        {
            RefName = "TestSaga",
            Items = new object[] { "EmptyPattern" }
        };

        // Act
        var triggers = TriggerExpander.ExpandTriggersForSaga(saga, world);

        // Assert
        Assert.NotNull(triggers);
        Assert.Empty(triggers);
    }

    [Fact]
    public void ExpandTriggersForSaga_MixedInlineAndPattern_ReturnsCombined()
    {
        // Arrange
        var world = CreateTestWorld();

        var pattern = new SagaTriggerPattern
        {
            RefName = "TwoRing",
            EnforceProgression = false,
            SagaTrigger = new[]
            {
                new SagaTrigger { RefName = "pattern_outer", EnterRadius = 30.0f },
                new SagaTrigger { RefName = "pattern_inner", EnterRadius = 15.0f }
            }
        };

        world.Gameplay.SagaTriggerPatterns = new[] { pattern };

        var inlineTrigger = new SagaTrigger { RefName = "inline_trigger", EnterRadius = 50.0f };

        var saga = new SagaArc
        {
            RefName = "MixedSaga",
            Items = new object[] { inlineTrigger, "TwoRing" }
        };

        // Act
        var triggers = TriggerExpander.ExpandTriggersForSaga(saga, world);

        // Assert
        Assert.Equal(3, triggers.Count);
        Assert.Contains(triggers, t => t.RefName == "inline_trigger");
        Assert.Contains(triggers, t => t.RefName == "pattern_outer");
        Assert.Contains(triggers, t => t.RefName == "pattern_inner");
    }

    [Fact]
    public void ExpandTriggersForSaga_Progression_TokensAreScopedToSaga()
    {
        // Arrange
        var world = CreateTestWorld();

        var pattern = new SagaTriggerPattern
        {
            RefName = "SharedPattern",
            EnforceProgression = true,
            SagaTrigger = new[]
            {
                new SagaTrigger { RefName = "outer", EnterRadius = 30.0f },
                new SagaTrigger { RefName = "inner", EnterRadius = 10.0f }
            }
        };

        world.Gameplay.SagaTriggerPatterns = new[] { pattern };

        var poi1 = new SagaArc
        {
            RefName = "FirstLocation",
            Items = new object[] { "SharedPattern" }
        };

        var poi2 = new SagaArc
        {
            RefName = "SecondLocation",
            Items = new object[] { "SharedPattern" }
        };

        // Act
        var triggers1 = TriggerExpander.ExpandTriggersForSaga(poi1, world);
        var triggers2 = TriggerExpander.ExpandTriggersForSaga(poi2, world);

        // Assert - Tokens should be scoped to each Saga
        var token1 = triggers1[0].GivesQuestTokenRef![0];
        var token2 = triggers2[0].GivesQuestTokenRef![0];

        Assert.StartsWith("FirstLocation_", token1);
        Assert.StartsWith("SecondLocation_", token2);
        Assert.NotEqual(token1, token2);
    }
}
