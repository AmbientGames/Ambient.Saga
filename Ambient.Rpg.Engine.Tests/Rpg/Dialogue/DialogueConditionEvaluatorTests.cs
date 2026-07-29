using Ambient.Domain;
using Ambient.Rpg.Engine.Domain.Dialogue.Evaluation;

namespace Ambient.Rpg.Engine.Tests.Rpg.Dialogue;

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
    public void HasQuestToken_WhenAvatarHasToken_ReturnsTrue()
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
    public void LacksQuestToken_WhenAvatarLacksToken_ReturnsTrue()
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
    public void HasEquipment_WhenAvatarHasItem_ReturnsTrue()
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
    public void LacksEquipment_WhenAvatarLacksItem_ReturnsTrue()
    {
        var condition = new DialogueCondition
        {
            Type = DialogueConditionType.LacksEquipment,
            RefName = "iron_sword"
        };

        Assert.True(_evaluator.Evaluate(condition));
    }

    [Fact]
    public void HasTool_WhenAvatarHasTool_ReturnsTrue()
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
    public void HasSpell_WhenAvatarHasSpell_ReturnsTrue()
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

    #region Avatar State Conditions

    [Fact]
    public void HasAchievement_WhenAvatarHasAchievement_ReturnsTrue()
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
        // Health lives on the avatar's 0..1 scale; "75" is the authored percentage form.
        _state.Health = 0.5f;

        var condition = new DialogueCondition
        {
            Type = DialogueConditionType.Health,
            Operator = ComparisonOperator.LessThan,
            Value = "75"
        };

        Assert.True(_evaluator.Evaluate(condition));
    }

    // --- M15: fractional health scale + no-throw on bad values ---
    // Real avatars store Health on a 0..1 scale, but shipped content authors percentages
    // ("50" = half). Before the fix, an authored "50" int-parsed and compared against a
    // 0..1 value (so "Health > 50" was ALWAYS false), and an authored "0.5" failed to
    // int-parse and THREW, soft-locking the whole conversation on every re-evaluation.

    [Fact]
    public void Health_PercentageAuthored_InterpretedAgainstFractionalScale()
    {
        // Full health on the 0..1 scale is greater than 50% ...
        _state.Health = 1;
        var greaterThanHalf = new DialogueCondition
        {
            Type = DialogueConditionType.Health,
            Operator = ComparisonOperator.GreaterThan,
            Value = "50" // percentage, not a 0..1 literal
        };
        Assert.True(_evaluator.Evaluate(greaterThanHalf)); // was always false pre-fix

        // ... and an empty-health avatar is not.
        _state.Health = 0;
        Assert.False(_evaluator.Evaluate(greaterThanHalf));
    }

    [Fact]
    public void Health_FractionalValueAuthored_ParsesInsteadOfSoftLocking()
    {
        _state.Health = 1; // full

        var condition = new DialogueCondition
        {
            Type = DialogueConditionType.Health,
            Operator = ComparisonOperator.GreaterThanOrEqual,
            Value = "0.5" // pre-fix: int.TryParse fails -> throws -> conversation soft-locks
        };

        // No exception, and full health satisfies ">= 0.5"
        Assert.True(_evaluator.Evaluate(condition));
    }

    [Fact]
    public void Health_OverFullAvatar_StillSatisfiesPercentageGates()
    {
        // Archetypes may spawn OVER-full (e.g. Warrior Health=1.05). The percentage
        // normalisation used to be gated on actual <= 1, so an over-full avatar compared
        // 1.05 against a raw 75 and a maximally healthy avatar FAILED "Health >= 75"
        // (and passed "Health < 75"). Fixed 2026-07-15: expected > 1 always normalises.
        _state.Health = 1.05f;

        var hale = new DialogueCondition
        {
            Type = DialogueConditionType.Health,
            Operator = ComparisonOperator.GreaterThanOrEqual,
            Value = "75"
        };
        Assert.True(_evaluator.Evaluate(hale)); // 1.05 >= 0.75 — was false pre-fix

        var wounded = new DialogueCondition
        {
            Type = DialogueConditionType.Health,
            Operator = ComparisonOperator.LessThan,
            Value = "75"
        };
        Assert.False(_evaluator.Evaluate(wounded)); // 1.05 < 0.75 is false — was true pre-fix
    }

    [Fact]
    public void Numeric_UnparseableValue_ReturnsFalseInsteadOfThrowing()
    {
        _state.Credits = 100;

        var condition = new DialogueCondition
        {
            Type = DialogueConditionType.Credits,
            Operator = ComparisonOperator.GreaterThan,
            Value = "not-a-number" // malformed authored value
        };

        // A bad Value used to throw and re-throw on every query/command (soft-lock).
        Assert.False(_evaluator.Evaluate(condition));
    }

    #endregion

    #region Dialogue History Conditions

    [Fact]
    public void AvatarVisitCount_EvaluatesCorrectly()
    {
        _state.RecordNodeVisit("merchant_dialogue", "greeting");
        _state.RecordNodeVisit("merchant_dialogue", "quest_offer");

        var condition = new DialogueCondition
        {
            Type = DialogueConditionType.AvatarVisitCount,
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

    #region Party Conditions

    [Fact]
    public void PartySlotAvailable_WhenSlotExists_ReturnsTrue()
    {
        _state.MaxPartySlots = 2;
        _state.AddPartyMember("COMPANION_A");

        var condition = new DialogueCondition
        {
            Type = DialogueConditionType.PartySlotAvailable
        };

        Assert.True(_evaluator.Evaluate(condition));
    }

    [Fact]
    public void PartySlotAvailable_WhenPartyFull_ReturnsFalse()
    {
        _state.MaxPartySlots = 1;
        _state.AddPartyMember("COMPANION_A");

        var condition = new DialogueCondition
        {
            Type = DialogueConditionType.PartySlotAvailable
        };

        Assert.False(_evaluator.Evaluate(condition));
    }

    [Fact]
    public void IsInParty_WhenCharacterInParty_ReturnsTrue()
    {
        _state.MaxPartySlots = 2;
        _state.AddPartyMember("LYRA_THE_HEALER");

        var condition = new DialogueCondition
        {
            Type = DialogueConditionType.IsInParty,
            RefName = "LYRA_THE_HEALER"
        };

        Assert.True(_evaluator.Evaluate(condition));
    }

    [Fact]
    public void IsInParty_WhenCharacterNotInParty_ReturnsFalse()
    {
        _state.MaxPartySlots = 2;
        _state.AddPartyMember("COMPANION_A");

        var condition = new DialogueCondition
        {
            Type = DialogueConditionType.IsInParty,
            RefName = "LYRA_THE_HEALER"
        };

        Assert.False(_evaluator.Evaluate(condition));
    }

    [Fact]
    public void PartySize_WithComparison_EvaluatesCorrectly()
    {
        _state.MaxPartySlots = 3;
        _state.AddPartyMember("COMPANION_A");
        _state.AddPartyMember("COMPANION_B");

        var condition = new DialogueCondition
        {
            Type = DialogueConditionType.PartySize,
            Operator = ComparisonOperator.GreaterThanOrEqual,
            Value = "2"
        };

        Assert.True(_evaluator.Evaluate(condition));
    }

    [Fact]
    public void PartySize_EmptyParty_ReturnsZero()
    {
        var condition = new DialogueCondition
        {
            Type = DialogueConditionType.PartySize,
            Operator = ComparisonOperator.Equals,
            Value = "0"
        };

        Assert.True(_evaluator.Evaluate(condition));
    }

    [Fact]
    public void PartyConditions_CanCombineWithAND()
    {
        // Setup: Has 1 companion, room for 1 more
        _state.MaxPartySlots = 2;
        _state.AddPartyMember("COMPANION_A");

        // Condition: Has slot available AND at least 1 party member
        var conditions = new[]
        {
            new DialogueCondition
            {
                Type = DialogueConditionType.PartySlotAvailable
            },
            new DialogueCondition
            {
                Type = DialogueConditionType.PartySize,
                Operator = ComparisonOperator.GreaterThanOrEqual,
                Value = "1"
            }
        };

        Assert.True(_evaluator.EvaluateAll(conditions, ConditionLogic.AND));
    }

    [Fact]
    public void PartyConditions_RecruitmentScenario_WorksCorrectly()
    {
        // Scenario: Character offers to join if:
        // 1. Party slot is available
        // 2. They're not already in the party
        // 3. Avatar has completed a quest (has token)
        _state.MaxPartySlots = 2;
        _state.AddQuestToken("saved_the_village");

        var conditions = new[]
        {
            new DialogueCondition
            {
                Type = DialogueConditionType.PartySlotAvailable
            },
            new DialogueCondition
            {
                Type = DialogueConditionType.IsInParty,
                RefName = "GRATEFUL_WARRIOR"
            },
            new DialogueCondition
            {
                Type = DialogueConditionType.HasQuestToken,
                RefName = "saved_the_village"
            }
        };

        // Should fail because IsInParty returns false (they're not in party yet)
        // Wait - we want them to NOT be in party, so we need to negate this...
        // The IsInParty condition returns true if IN party, false if not.
        // So when character is NOT in party, IsInParty returns false, making AND fail.
        // This test shows the limitation - we don't have "NotInParty" condition.
        // For now, let's test what we have:

        Assert.False(_evaluator.EvaluateAll(conditions, ConditionLogic.AND));

        // Add them to party - now IsInParty should return true
        _state.AddPartyMember("GRATEFUL_WARRIOR");
        Assert.True(_evaluator.EvaluateAll(conditions, ConditionLogic.AND));
    }

    #endregion

    #region Trait Comparison Conditions

    [Fact]
    public void TraitComparison_WithTraitSpecified_EvaluatesCorrectly()
    {
        _state.SetTraitValue(CharacterTraitType.Aggression.ToString(), 75);

        var condition = new DialogueCondition
        {
            Type = DialogueConditionType.TraitComparison,
            Trait = CharacterTraitType.Aggression,
            TraitSpecified = true,
            Operator = ComparisonOperator.GreaterThanOrEqual,
            Value = "50"
        };

        Assert.True(_evaluator.Evaluate(condition));
    }

    [Fact]
    public void TraitComparison_WithTraitSpecified_WhenBelowThreshold_ReturnsFalse()
    {
        _state.SetTraitValue(CharacterTraitType.Aggression.ToString(), 25);

        var condition = new DialogueCondition
        {
            Type = DialogueConditionType.TraitComparison,
            Trait = CharacterTraitType.Aggression,
            TraitSpecified = true,
            Operator = ComparisonOperator.GreaterThanOrEqual,
            Value = "50"
        };

        Assert.False(_evaluator.Evaluate(condition));
    }

    [Fact]
    public void TraitComparison_WithoutTraitSpecified_ThrowsException()
    {
        var condition = new DialogueCondition
        {
            Type = DialogueConditionType.TraitComparison,
            // TraitSpecified defaults to false
            Operator = ComparisonOperator.GreaterThanOrEqual,
            Value = "50"
        };

        var ex = Assert.Throws<InvalidOperationException>(() => _evaluator.Evaluate(condition));
        Assert.Contains("TraitComparison condition requires Trait attribute", ex.Message);
    }

    [Fact]
    public void TraitComparison_WithMissingTraitValue_DefaultsToZero()
    {
        // Trait not set in state - should default to 0

        var condition = new DialogueCondition
        {
            Type = DialogueConditionType.TraitComparison,
            Trait = CharacterTraitType.Morale,
            TraitSpecified = true,
            Operator = ComparisonOperator.Equals,
            Value = "0"
        };

        Assert.True(_evaluator.Evaluate(condition));
    }

    #endregion
}
