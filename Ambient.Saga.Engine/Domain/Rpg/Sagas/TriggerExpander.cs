using Ambient.Domain;
using Ambient.Domain.DefinitionExtensions;

namespace Ambient.Saga.Engine.Domain.Rpg.Sagas;

/// <summary>
/// Expands TriggerPattern references into concrete Trigger sets with progression chains.
/// TriggerPattern is a reusable spatial pattern (e.g., concentric rings).
/// A Saga either uses a predefined pattern OR defines triggers inline.
/// </summary>
public static class TriggerExpander
{
    /// <summary>
    /// Expands triggers for a Saga.
    /// If Saga references a TriggerPattern, returns the trigger set with auto-generated progression tokens.
    /// Otherwise, returns the inline triggers.
    /// Additionally generates an implicit central feature trigger if the SagaArc has a Landmark/Structure/QuestSignpost.
    /// </summary>
    public static List<SagaTrigger> ExpandTriggersForSaga(SagaArc sagaArc, IWorld world)
    {
        var result = new List<SagaTrigger>();

        // First, add explicit triggers (inline or from patterns)
        if (sagaArc.Items != null)
        {
            foreach (var item in sagaArc.Items)
            {
                switch (item)
                {
                    case SagaTrigger sagaTrigger:
                        // Inline trigger
                        result.Add(sagaTrigger);
                        break;

                    case string sagaPatternRef:
                        // TriggerPatternRef - expand the entire pattern
                        var pattern = world.Gameplay?.SagaTriggerPatterns?
                            .FirstOrDefault(tp => tp.RefName == sagaPatternRef);

                        if (pattern == null)
                        {
                            throw new InvalidOperationException(
                                $"SagaTriggerPattern '{sagaPatternRef}' not found in world");
                        }

                        var triggers = pattern.SagaTrigger?.ToList() ?? new List<SagaTrigger>();

                        if (pattern.EnforceProgression)
                        {
                            triggers = AddProgressionChain(triggers, sagaArc.RefName);
                        }

                        result.AddRange(triggers);
                        break;

                    default:
                        throw new InvalidOperationException($"Unexpected type {item.GetType()} in Saga Items.");
                }
            }
        }

        // Generate implicit feature trigger if SagaArc has a feature (Landmark/Structure/QuestSignpost)
        var featureTrigger = GenerateFeatureTrigger(sagaArc, world);
        if (featureTrigger != null)
        {
            result.Add(featureTrigger);
        }

        return result;
    }

    /// <summary>
    /// Generates an implicit trigger for the SagaArc's central feature.
    /// This allows features to be "triggered" when the avatar enters their ApproachRadius,
    /// providing a consistent interaction model for triggers, characters, and features.
    /// </summary>
    private static SagaTrigger? GenerateFeatureTrigger(SagaArc sagaArc, IWorld world)
    {
        if (string.IsNullOrEmpty(sagaArc.SagaFeatureRef))
            return null;

        // Retrieve feature using unified lookup
        var feature = world.TryGetSagaFeatureByRefName(sagaArc.SagaFeatureRef);
        var featureType = feature?.Type.ToString() ?? "SagaFeature";

        if (feature?.Interactable == null)
            return null;

        var approachRadius = feature.Interactable.ApproachRadius;
        if (approachRadius <= 0)
            return null; // Feature not approachable (requires manual interaction)

        // Generate trigger at Saga center (0,0 relative coordinates)
        var trigger = new SagaTrigger
        {
            RefName = $"Feature_{sagaArc.RefName}",
            DisplayName = feature.DisplayName ?? $"{featureType} Interaction",
            Description = $"Auto-generated trigger for {featureType} '{sagaArc.SagaFeatureRef}'",
            EnterRadius = approachRadius,
            Tags = $"Feature,{featureType}"
        };

        return trigger;
    }

    /// <summary>
    /// Adds progression chain by generating quest tokens scoped to the Saga.
    /// Triggers must be completed from outermost to innermost (by radius).
    /// Auto-generates tokens: {Saga_RefName}_{Trigger_RefName}_Completed
    /// </summary>
    private static List<SagaTrigger> AddProgressionChain(List<SagaTrigger> triggers, string poiRefName)
    {
        // Sort by radius (descending - outermost first)
        var sorted = triggers.OrderByDescending(t => t.EnterRadius).ToList();

        var result = new List<SagaTrigger>();
        string? previousSagaTriggerToken = null;

        foreach (var trigger in sorted)
        {
            // Generate completion token for this trigger
            var completionToken = $"{poiRefName}_{trigger.RefName}_Completed";

            // Clone trigger and add progression requirements
            // Note: Triggers are stateless spatial zones with no cooldowns.
            // Character respawns are handled via Character.RespawnDurationSeconds.
            var expandedSagaTrigger = new SagaTrigger
            {
                RefName = trigger.RefName,
                DisplayName = trigger.DisplayName,
                Description = trigger.Description,
                EnterRadius = trigger.EnterRadius,
                Tags = trigger.Tags,
                Metadata = trigger.Metadata,
                Spawn = trigger.Spawn
            };

            // If this is not the first (outermost) trigger, require previous completion
            if (previousSagaTriggerToken != null)
            {
                expandedSagaTrigger.RequiresQuestTokenRef = new[] { previousSagaTriggerToken };
            }

            // Give completion token when this trigger is completed
            expandedSagaTrigger.GivesQuestTokenRef = new[] { completionToken };

            result.Add(expandedSagaTrigger);
            previousSagaTriggerToken = completionToken;
        }

        return result;
    }
}
