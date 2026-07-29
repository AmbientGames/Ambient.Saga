using Ambient.Domain;

namespace Ambient.Rpg.Engine.Domain.Battle;

/// <summary>
/// Result of evaluating a battle trigger.
/// Contains the dialogue to show and whether the trigger should be consumed.
/// </summary>
public record BattleTriggerResult
{
    /// <summary>
    /// Dialogue tree reference to show
    /// </summary>
    public required string DialogueTreeRef { get; init; }

    /// <summary>
    /// Starting node ID within the dialogue tree (null = start from beginning)
    /// </summary>
    public string? StartNodeId { get; init; }

    /// <summary>
    /// Whether this trigger should be consumed (OnceOnly = true) after firing
    /// </summary>
    public bool ConsumeAfterFiring { get; init; }

    /// <summary>
    /// Index of the trigger that fired (for tracking consumed triggers)
    /// </summary>
    public int TriggerIndex { get; init; }
}

/// <summary>
/// Evaluates battle dialogue triggers during combat.
/// Triggers can fire based on health thresholds, turn numbers, stance/affinity changes, and battle outcomes.
/// </summary>
public class BattleTriggerEvaluator
{
    private readonly HashSet<int> _firedOnceOnlyTriggers = new();

    /// <summary>
    /// Clears all fired once-only triggers.
    /// Call this when starting a new battle.
    /// </summary>
    public void Reset()
    {
        _firedOnceOnlyTriggers.Clear();
    }

    /// <summary>
    /// Mark a trigger as consumed (for OnceOnly triggers).
    /// </summary>
    public void ConsumeTrigger(int triggerIndex)
    {
        _firedOnceOnlyTriggers.Add(triggerIndex);
    }

    /// <summary>
    /// Check if a trigger has already been consumed.
    /// </summary>
    public bool IsTriggerConsumed(int triggerIndex)
    {
        return _firedOnceOnlyTriggers.Contains(triggerIndex);
    }

    /// <summary>
    /// Evaluates all battle triggers for a character and returns any that should fire.
    /// </summary>
    /// <param name="triggers">Array of battle triggers to evaluate</param>
    /// <param name="context">Current battle context for evaluation</param>
    /// <returns>List of triggered dialogue results (may be empty)</returns>
    public List<BattleTriggerResult> Evaluate(CharacterTrigger[]? triggers, BattleTriggerContext context)
    {
        var results = new List<BattleTriggerResult>();

        if (triggers == null || triggers.Length == 0)
            return results;

        for (int i = 0; i < triggers.Length; i++)
        {
            var trigger = triggers[i];

            // Skip if already fired and is once-only
            if (trigger.OnceOnly && _firedOnceOnlyTriggers.Contains(i))
                continue;

            if (EvaluateTrigger(trigger, context))
            {
                results.Add(new BattleTriggerResult
                {
                    DialogueTreeRef = trigger.DialogueTreeRef,
                    StartNodeId = trigger.StartNodeId,
                    ConsumeAfterFiring = trigger.OnceOnly,
                    TriggerIndex = i
                });

                // Auto-consume once-only triggers when they fire
                if (trigger.OnceOnly)
                {
                    _firedOnceOnlyTriggers.Add(i);
                }
            }
        }

        return results;
    }

    /// <summary>
    /// Evaluates a single trigger against the current context.
    /// </summary>
    private bool EvaluateTrigger(CharacterTrigger trigger, BattleTriggerContext context)
    {
        // Shipped content authors health thresholds as normalized fractions
        // (Value="0.75" = 75%) while the context carries percentages — values in
        // (0,1] are read as fractions, larger values as percents
        var healthThreshold = trigger.Value > 0 && trigger.Value <= 1.0f
            ? trigger.Value * 100f
            : trigger.Value;

        return trigger.Condition switch
        {
            // Enemy (character) health threshold checks
            BattleTriggerCondition.HealthBelow =>
                context.EnemyHealthPercent < healthThreshold,

            BattleTriggerCondition.HealthAbove =>
                context.EnemyHealthPercent > healthThreshold,

            // Avatar health threshold check
            BattleTriggerCondition.AvatarHealthBelow =>
                context.AvatarHealthPercent < healthThreshold,

            // Turn-based trigger (fires when turn number is reached)
            BattleTriggerCondition.TurnNumber =>
                context.TurnNumber >= (int)trigger.Value,

            // Combat style changes - these require tracking previous values
            BattleTriggerCondition.StanceChanged =>
                context.StanceJustChanged,

            BattleTriggerCondition.AffinityChanged =>
                context.AffinityJustChanged,

            // Battle outcome triggers - only fire at end of battle.
            // Fleeing is neither: a fled battle fires no outcome dialogue.
            BattleTriggerCondition.OnVictory =>
                context.BattleEnded && context.AvatarVictory,

            BattleTriggerCondition.OnDefeat =>
                context.BattleEnded && !context.AvatarVictory && !context.AvatarFled,

            _ => false
        };
    }
}

/// <summary>
/// Context for evaluating battle triggers.
/// Contains all the state needed to check trigger conditions.
/// </summary>
public class BattleTriggerContext
{
    /// <summary>
    /// Avatar's current health as a percentage (0-100)
    /// </summary>
    public float AvatarHealthPercent { get; init; }

    /// <summary>
    /// Enemy's current health as a percentage (0-100)
    /// </summary>
    public float EnemyHealthPercent { get; init; }

    /// <summary>
    /// Current turn number (1-based)
    /// </summary>
    public int TurnNumber { get; init; }

    /// <summary>
    /// Whether the avatar or enemy just changed stance this turn
    /// </summary>
    public bool StanceJustChanged { get; init; }

    /// <summary>
    /// Whether the avatar or enemy just changed affinity this turn
    /// </summary>
    public bool AffinityJustChanged { get; init; }

    /// <summary>
    /// Whether the battle has ended
    /// </summary>
    public bool BattleEnded { get; init; }

    /// <summary>
    /// Whether the avatar won (only valid if BattleEnded is true)
    /// </summary>
    public bool AvatarVictory { get; init; }

    /// <summary>
    /// Whether the avatar fled (only valid if BattleEnded is true).
    /// A fled battle is neither a victory nor a defeat.
    /// </summary>
    public bool AvatarFled { get; init; }

    /// <summary>
    /// Create context from current battle engine state.
    /// </summary>
    public static BattleTriggerContext FromBattleEngine(
        BattleEngine engine,
        bool stanceJustChanged = false,
        bool affinityJustChanged = false)
    {
        var avatar = engine.GetAvatar();
        var enemy = engine.GetEnemy();

        return new BattleTriggerContext
        {
            AvatarHealthPercent = avatar.HealthPercent,
            EnemyHealthPercent = enemy.HealthPercent,
            TurnNumber = engine.GetTurnNumber(),
            StanceJustChanged = stanceJustChanged,
            AffinityJustChanged = affinityJustChanged,
            BattleEnded = engine.State == BattleState.Victory ||
                         engine.State == BattleState.Defeat ||
                         engine.State == BattleState.Fled,
            AvatarVictory = engine.State == BattleState.Victory,
            AvatarFled = engine.State == BattleState.Fled
        };
    }
}
