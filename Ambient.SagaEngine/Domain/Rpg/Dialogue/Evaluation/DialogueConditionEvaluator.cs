using Ambient.Domain;
using Ambient.SagaEngine.Domain.Rpg.Dialogue;

namespace Ambient.SagaEngine.Domain.Rpg.Dialogue.Evaluation;

/// <summary>
/// Evaluates dialogue conditions against player/world state.
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

            // Degradable items
            DialogueConditionType.HasEquipment => _stateProvider.HasEquipment(condition.RefName),
            DialogueConditionType.LacksEquipment => !_stateProvider.HasEquipment(condition.RefName),
            DialogueConditionType.HasTool => _stateProvider.HasTool(condition.RefName),
            DialogueConditionType.LacksTool => !_stateProvider.HasTool(condition.RefName),
            DialogueConditionType.HasSpell => _stateProvider.HasSpell(condition.RefName),
            DialogueConditionType.LacksSpell => !_stateProvider.HasSpell(condition.RefName),

            // Player state
            DialogueConditionType.HasAchievement => _stateProvider.HasAchievement(condition.RefName),
            DialogueConditionType.Credits => EvaluateNumeric(_stateProvider.GetCredits(), condition),
            DialogueConditionType.Health => EvaluateNumeric(_stateProvider.GetHealth(), condition),

            // Dialogue history
            DialogueConditionType.PlayerVisitCount => EvaluateNumeric(_stateProvider.GetPlayerVisitCount(condition.RefName), condition),
            DialogueConditionType.NodeVisited => _stateProvider.WasNodeVisited(condition.RefName, condition.Value),

            // World state
            DialogueConditionType.BossDefeatedCount => EvaluateNumeric(_stateProvider.GetBossDefeatedCount(condition.RefName), condition),

            // Quest state
            DialogueConditionType.QuestActive => _stateProvider.IsQuestActive(condition.RefName),
            DialogueConditionType.QuestCompleted => _stateProvider.IsQuestCompleted(condition.RefName),
            DialogueConditionType.QuestNotStarted => _stateProvider.IsQuestNotStarted(condition.RefName),

            // Character traits
            DialogueConditionType.TraitComparison => EvaluateNumeric(_stateProvider.GetTraitValue(condition.Trait.ToString()) ?? 0, condition),

            // Faction reputation
            DialogueConditionType.ReputationLevel => EvaluateReputationLevel(condition),
            DialogueConditionType.ReputationValue => EvaluateNumeric(_stateProvider.GetFactionReputation(condition.FactionRef ?? ""), condition),

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
        if (!int.TryParse(condition.Value, out var expectedValue))
            throw new InvalidOperationException($"Cannot parse numeric value: {condition.Value}");

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
