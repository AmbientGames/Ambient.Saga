using System.Globalization;
using Ambient.Domain;
using Ambient.Rpg.Engine.Domain.Dialogue;

namespace Ambient.Rpg.Engine.Domain.Dialogue.Evaluation;

/// <summary>
/// Evaluates dialogue conditions against avatar/world state.
/// Fully data-driven - no special cases needed for new condition types.
/// </summary>
public class DialogueConditionEvaluator
{
    private readonly IDialogueStateProvider _stateProvider;

    public DialogueConditionEvaluator(IDialogueStateProvider stateProvider)
    {
        _stateProvider = stateProvider ?? throw new ArgumentNullException(nameof(stateProvider));
    }

    /// <summary>
    /// Evaluates a single condition.
    /// </summary>
    public bool Evaluate(DialogueCondition condition)
    {
        return condition.Type switch
        {
            // Quest tokens
            DialogueConditionType.HasQuestToken => _stateProvider.HasQuestToken(condition.RefName),
            DialogueConditionType.LacksQuestToken => !_stateProvider.HasQuestToken(condition.RefName),

            // Stackable items
            DialogueConditionType.HasConsumable => EvaluateQuantity(_stateProvider.GetConsumableQuantity(condition.RefName), condition),
            DialogueConditionType.LacksConsumable => _stateProvider.GetConsumableQuantity(condition.RefName) == 0,
            DialogueConditionType.HasMaterial => EvaluateQuantity(_stateProvider.GetMaterialQuantity(condition.RefName), condition),
            DialogueConditionType.LacksMaterial => _stateProvider.GetMaterialQuantity(condition.RefName) == 0,
            DialogueConditionType.HasBlock => EvaluateQuantity((int)_stateProvider.GetBlockQuantity(condition.RefName), condition),
            DialogueConditionType.LacksBlock => _stateProvider.GetBlockQuantity(condition.RefName) == 0,

            // Degradable items
            DialogueConditionType.HasEquipment => _stateProvider.HasEquipment(condition.RefName),
            DialogueConditionType.LacksEquipment => !_stateProvider.HasEquipment(condition.RefName),
            DialogueConditionType.HasTool => _stateProvider.HasTool(condition.RefName),
            DialogueConditionType.LacksTool => !_stateProvider.HasTool(condition.RefName),
            DialogueConditionType.HasSpell => _stateProvider.HasSpell(condition.RefName),
            DialogueConditionType.LacksSpell => !_stateProvider.HasSpell(condition.RefName),

            // Avatar state
            DialogueConditionType.HasAchievement => _stateProvider.HasAchievement(condition.RefName),
            DialogueConditionType.Credits => EvaluateNumeric(_stateProvider.GetCredits(), condition),
            DialogueConditionType.Health => EvaluateHealth(condition),

            // Dialogue history
            DialogueConditionType.AvatarVisitCount => EvaluateNumeric(_stateProvider.GetAvatarVisitCount(condition.RefName), condition),
            DialogueConditionType.NodeVisited => _stateProvider.WasNodeVisited(condition.RefName, condition.Value),

            // World state
            DialogueConditionType.BossDefeatedCount => EvaluateNumeric(_stateProvider.GetBossDefeatedCount(condition.RefName), condition),

            // Quest state
            DialogueConditionType.QuestActive => _stateProvider.IsQuestActive(condition.RefName),
            DialogueConditionType.QuestCompleted => _stateProvider.IsQuestCompleted(condition.RefName),
            DialogueConditionType.QuestNotStarted => _stateProvider.IsQuestNotStarted(condition.RefName),

            // Character traits
            DialogueConditionType.TraitComparison => EvaluateTraitComparison(condition),

            // Faction reputation
            DialogueConditionType.ReputationLevel => EvaluateReputationLevel(condition),
            DialogueConditionType.ReputationValue => EvaluateNumeric(_stateProvider.GetFactionReputation(condition.FactionRef ?? ""), condition),

            // Party conditions
            DialogueConditionType.PartySlotAvailable => _stateProvider.HasAvailablePartySlot(),
            DialogueConditionType.IsInParty => _stateProvider.IsInParty(condition.RefName),
            DialogueConditionType.PartySize => EvaluateNumeric(_stateProvider.GetPartySize(), condition),

            _ => throw new NotSupportedException($"Unknown condition type: {condition.Type}")
        };
    }

    /// <summary>
    /// Evaluates multiple conditions with AND/OR logic.
    /// </summary>
    public bool EvaluateAll(DialogueCondition[] conditions, ConditionLogic logic)
    {
        if (conditions == null || conditions.Length == 0)
            return true; // No conditions = always pass

        return logic switch
        {
            ConditionLogic.AND => conditions.All(Evaluate),
            ConditionLogic.OR => conditions.Any(Evaluate),
            _ => throw new NotSupportedException($"Unknown condition logic: {logic}")
        };
    }

    private bool EvaluateQuantity(int actualQuantity, DialogueCondition condition)
    {
        // For Has* conditions, if no operator/value specified, just check quantity > 0
        if (string.IsNullOrEmpty(condition.Value))
            return actualQuantity > 0;

        return EvaluateNumeric(actualQuantity, condition);
    }

    private bool EvaluateNumeric(float actualValue, DialogueCondition condition)
    {
        // Unparseable authored values FAIL the condition instead of throwing —
        // a bad Value used to soft-lock the whole conversation (every query and
        // command re-hit the throw). Invariant culture: "0.5" must parse the
        // same on a de-DE machine as on the author's (M15).
        if (!float.TryParse(condition.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var expectedValue))
            return false;

        return Compare(actualValue, expectedValue, condition.Operator);
    }

    /// <summary>
    /// Health conditions: avatar health is stored on a 0..1 scale (archetypes may spawn
    /// slightly over-full, e.g. 1.05), but content authors percentages ("50" meaning half
    /// health) — which previously always passed &gt;-style checks. Expected values above 1
    /// are ALWAYS interpreted as percentages so both "0.5" and "50" mean half health (M15).
    /// The normalisation must not depend on the ACTUAL value: gating it on actual &lt;= 1
    /// made an over-full avatar compare 1.05 against a raw 75, so a maximally healthy
    /// avatar failed "Health &gt;= 75" (fixed 2026-07-15). Unparseable values fail the
    /// condition.
    /// </summary>
    private bool EvaluateHealth(DialogueCondition condition)
    {
        var actualValue = _stateProvider.GetHealth();

        if (!float.TryParse(condition.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var expectedValue))
            return false;

        if (expectedValue > 1f)
            expectedValue /= 100f;

        return Compare(actualValue, expectedValue, condition.Operator);
    }

    private static bool Compare(float actualValue, float expectedValue, ComparisonOperator op)
    {
        return op switch
        {
            ComparisonOperator.Equals => actualValue == expectedValue,
            ComparisonOperator.NotEquals => actualValue != expectedValue,
            ComparisonOperator.GreaterThan => actualValue > expectedValue,
            ComparisonOperator.GreaterThanOrEqual => actualValue >= expectedValue,
            ComparisonOperator.LessThan => actualValue < expectedValue,
            ComparisonOperator.LessThanOrEqual => actualValue <= expectedValue,
            _ => throw new NotSupportedException($"Unknown operator: {op}")
        };
    }

    private bool EvaluateBoolean(bool actualValue, DialogueCondition condition)
    {
        if (!bool.TryParse(condition.Value, out var expectedValue))
            throw new InvalidOperationException($"Cannot parse boolean value: {condition.Value}");

        return condition.Operator switch
        {
            ComparisonOperator.Equals => actualValue == expectedValue,
            ComparisonOperator.NotEquals => actualValue != expectedValue,
            _ => throw new NotSupportedException($"Boolean conditions only support Equals/NotEquals operators")
        };
    }

    private bool EvaluateTraitComparison(DialogueCondition condition)
    {
        if (!condition.TraitSpecified)
            throw new InvalidOperationException("TraitComparison condition requires Trait attribute to be specified");

        var traitValue = _stateProvider.GetTraitValue(condition.Trait.ToString()) ?? 0;
        return EvaluateNumeric(traitValue, condition);
    }

    private bool EvaluateReputationLevel(DialogueCondition condition)
    {
        // Get actual reputation level for the faction
        var actualLevelString = _stateProvider.GetFactionReputationLevel(condition.FactionRef ?? "");

        if (!Enum.TryParse<ReputationLevel>(actualLevelString, out var actualLevel))
            return false;

        // Parse expected level from Value
        if (string.IsNullOrEmpty(condition.Value) || !Enum.TryParse<ReputationLevel>(condition.Value, out var expectedLevel))
            throw new InvalidOperationException($"Cannot parse ReputationLevel value: {condition.Value}");

        // Compare using enum numeric values (Hated=0, Hostile=1, ..., Exalted=7)
        var actualValue = (int)actualLevel;
        var expectedValue = (int)expectedLevel;

        return condition.Operator switch
        {
            ComparisonOperator.Equals => actualValue == expectedValue,
            ComparisonOperator.NotEquals => actualValue != expectedValue,
            ComparisonOperator.GreaterThan => actualValue > expectedValue,
            ComparisonOperator.GreaterThanOrEqual => actualValue >= expectedValue,
            ComparisonOperator.LessThan => actualValue < expectedValue,
            ComparisonOperator.LessThanOrEqual => actualValue <= expectedValue,
            _ => throw new NotSupportedException($"Unknown operator: {condition.Operator}")
        };
    }
}
