using Ambient.Domain;
using Ambient.SagaEngine.Domain.Rpg.Dialogue.Evaluation;

namespace Ambient.SagaEngine.Tests.Rpg.Dialogue;

public class DialogueConditionEvaluatorTests
{
    private readonly MockDialogueStateProvider _state;
    private readonly DialogueConditionEvaluator _evaluator;

    public DialogueConditionEvaluatorTests()
    {
        _state = new MockDialogueStateProvider();
        _evaluator = new DialogueConditionEvaluator(_state);
    }

    #region Quest Token Conditions

    [Fact]
    public void HasQuestToken_WhenPlayerHasToken_ReturnsTrue()
    {
        _state.AddQuestToken("dragon_quest");

        var condition = new DialogueCondition
        {
            Type = DialogueConditionType.HasQuestToken,
            RefName = "dragon_quest"
        };

        Assert.True(_evaluator.Evaluate(condition));
    }

    [Fact]
    public void LacksQuestToken_WhenPlayerLacksToken_ReturnsTrue()
    {
        var condition = new DialogueCondition
        {
            Type = DialogueConditionType.LacksQuestToken,
            RefName = "dragon_quest"
        };

        Assert.True(_evaluator.Evaluate(condition));
    }

    #endregion

    #region Stackable Item Conditions

    [Fact]
    public void HasConsumable_WithoutOperator_ReturnsTrueIfAnyQuantity()
    {
        _state.AddConsumable("health_potion", 1);

        var condition = new DialogueCondition
        {
            Type = DialogueConditionType.HasConsumable,
            RefName = "health_potion"
        };

        Assert.True(_evaluator.Evaluate(condition));
    }

    [Fact]
    public void HasConsumable_WithQuantityCheck_EvaluatesCorrectly()
    {
        _state.AddConsumable("health_potion", 5);

        var condition = new DialogueCondition
        {
            Type = DialogueConditionType.HasConsumable,
            RefName = "health_potion",
            Operator = ComparisonOperator.GreaterThanOrEqual,
            Value = "3"
        };

        Assert.True(_evaluator.Evaluate(condition));
    }

    [Fact]
    public void LacksConsumable_WhenQuantityIsZero_ReturnsTrue()
    {
        var condition = new DialogueCondition
        {
            Type = DialogueConditionType.LacksConsumable,
            RefName = "health_potion"
        };

        Assert.True(_evaluator.Evaluate(condition));
    }

    [Fact]
    public void HasMaterial_WithQuantityCheck_EvaluatesCorrectly()
    {
        _state.AddMaterial("iron_ore", 10);

        var condition = new DialogueCondition
        {
            Type = DialogueConditionType.HasMaterial,
            RefName = "iron_ore",
            Operator = ComparisonOperator.GreaterThan,
            Value = "5"
        };

        Assert.True(_evaluator.Evaluate(condition));
    }

    #endregion

    #region Degradable Item Conditions

    [Fact]
    public void HasEquipment_WhenPlayerHasItem_ReturnsTrue()
    {
        _state.AddEquipment("iron_sword");

        var condition = new DialogueCondition
        {
            Type = DialogueConditionType.HasEquipment,
            RefName = "iron_sword"
        };

        Assert.True(_evaluator.Evaluate(condition));
    }

    [Fact]
    public void LacksEquipment_WhenPlayerLacksItem_ReturnsTrue()
    {
        var condition = new DialogueCondition
        {
            Type = DialogueConditionType.LacksEquipment,
            RefName = "iron_sword"
        };

        Assert.True(_evaluator.Evaluate(condition));
    }

    [Fact]
    public void HasTool_WhenPlayerHasTool_ReturnsTrue()
    {
        _state.AddTool("pickaxe");

        var condition = new DialogueCondition
        {
            Type = DialogueConditionType.HasTool,
            RefName = "pickaxe"
        };

        Assert.True(_evaluator.Evaluate(condition));
    }

    [Fact]
    public void HasSpell_WhenPlayerHasSpell_ReturnsTrue()
    {
        _state.AddSpell("fireball");

        var condition = new DialogueCondition
        {
            Type = DialogueConditionType.HasSpell,
            RefName = "fireball"
        };

        Assert.True(_evaluator.Evaluate(condition));
    }

    #endregion

    #region Player State Conditions

    [Fact]
    public void HasAchievement_WhenPlayerHasAchievement_ReturnsTrue()
    {
        _state.UnlockAchievement("dragon_slayer");

        var condition = new DialogueCondition
        {
            Type = DialogueConditionType.HasAchievement,
            RefName = "dragon_slayer"
        };

        Assert.True(_evaluator.Evaluate(condition));
    }

    [Fact]
    public void Credits_WithComparison_EvaluatesCorrectly()
    {
        _state.Credits = 150;

        var condition = new DialogueCondition
        {
            Type = DialogueConditionType.Credits,
            Operator = ComparisonOperator.GreaterThan,
            Value = "100"
        };

        Assert.True(_evaluator.Evaluate(condition));
    }

    [Fact]
    public void Health_WithComparison_EvaluatesCorrectly()
    {
        _state.Health = 50;

        var condition = new DialogueCondition
        {
            Type = DialogueConditionType.Health,
            Operator = ComparisonOperator.LessThan,
            Value = "75"
        };

        Assert.True(_evaluator.Evaluate(condition));
    }

    #endregion

    #region Dialogue History Conditions

    [Fact]
    public void PlayerVisitCount_EvaluatesCorrectly()
    {
        _state.RecordNodeVisit("merchant_dialogue", "greeting");
        _state.RecordNodeVisit("merchant_dialogue", "quest_offer");

        var condition = new DialogueCondition
        {
            Type = DialogueConditionType.PlayerVisitCount,
            RefName = "merchant_dialogue",
            Operator = ComparisonOperator.Equals,
            Value = "2"
        };

        Assert.True(_evaluator.Evaluate(condition));
    }

    [Fact]
    public void NodeVisited_WhenNodeWasVisited_ReturnsTrue()
    {
        _state.RecordNodeVisit("merchant_dialogue", "quest_accepted");

        var condition = new DialogueCondition
        {
            Type = DialogueConditionType.NodeVisited,
            RefName = "merchant_dialogue",
            Value = "quest_accepted"
        };

        Assert.True(_evaluator.Evaluate(condition));
    }

    #endregion

    #region World State Conditions

    [Fact]
    public void BossDefeatedCount_EvaluatesCorrectly()
    {
        _state.SetBossDefeatedCount("dragon", 3);

        var condition = new DialogueCondition
        {
            Type = DialogueConditionType.BossDefeatedCount,
            RefName = "dragon",
            Operator = ComparisonOperator.GreaterThanOrEqual,
            Value = "3"
        };

        Assert.True(_evaluator.Evaluate(condition));
    }

    #endregion

    #region Multiple Conditions with AND/OR Logic

    [Fact]
    public void EvaluateAll_WithAND_AllMustPass()
    {
        _state.Credits = 100;
        _state.AddQuestToken("main_quest");

        var conditions = new[]
        {
            new DialogueCondition
            {
                Type = DialogueConditionType.Credits,
                Operator = ComparisonOperator.GreaterThanOrEqual,
                Value = "50"
            },
            new DialogueCondition
            {
                Type = DialogueConditionType.HasQuestToken,
                RefName = "main_quest"
            }
        };

        Assert.True(_evaluator.EvaluateAll(conditions, ConditionLogic.AND));
    }

    [Fact]
    public void EvaluateAll_WithAND_FailsIfOneFails()
    {
        _state.Credits = 100;
        // No quest token

        var conditions = new[]
        {
            new DialogueCondition
            {
                Type = DialogueConditionType.Credits,
                Operator = ComparisonOperator.GreaterThanOrEqual,
                Value = "50"
            },
            new DialogueCondition
            {
                Type = DialogueConditionType.HasQuestToken,
                RefName = "main_quest"
            }
        };

        Assert.False(_evaluator.EvaluateAll(conditions, ConditionLogic.AND));
    }

    [Fact]
    public void EvaluateAll_WithOR_PassesIfOneSucceeds()
    {
        _state.Credits = 10; // Not enough
        _state.AddQuestToken("main_quest"); // Has token

        var conditions = new[]
        {
            new DialogueCondition
            {
                Type = DialogueConditionType.Credits,
                Operator = ComparisonOperator.GreaterThanOrEqual,
                Value = "100"
            },
            new DialogueCondition
            {
                Type = DialogueConditionType.HasQuestToken,
                RefName = "main_quest"
            }
        };

        Assert.True(_evaluator.EvaluateAll(conditions, ConditionLogic.OR));
    }

    [Fact]
    public void EvaluateAll_WithEmptyConditions_ReturnsTrue()
    {
        Assert.True(_evaluator.EvaluateAll(Array.Empty<DialogueCondition>(), ConditionLogic.AND));
    }

    #endregion

    #region Comparison Operators

    [Theory]
    [InlineData(ComparisonOperator.Equals, 100, "100", true)]
    [InlineData(ComparisonOperator.Equals, 100, "50", false)]
    [InlineData(ComparisonOperator.NotEquals, 100, "50", true)]
    [InlineData(ComparisonOperator.GreaterThan, 100, "50", true)]
    [InlineData(ComparisonOperator.GreaterThan, 50, "100", false)]
    [InlineData(ComparisonOperator.GreaterThanOrEqual, 100, "100", true)]
    [InlineData(ComparisonOperator.LessThan, 50, "100", true)]
    [InlineData(ComparisonOperator.LessThanOrEqual, 100, "100", true)]
    public void ComparisonOperators_WorkCorrectly(ComparisonOperator op, int actualValue, string expectedValue, bool expectedResult)
    {
        _state.Credits = actualValue;

        var condition = new DialogueCondition
        {
            Type = DialogueConditionType.Credits,
            Operator = op,
            Value = expectedValue
        };

        Assert.Equal(expectedResult, _evaluator.Evaluate(condition));
    }

    #endregion
}
