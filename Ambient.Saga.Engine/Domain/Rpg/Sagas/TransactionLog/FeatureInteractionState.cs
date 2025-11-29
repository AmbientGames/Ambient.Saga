namespace Ambient.Saga.Engine.Domain.Rpg.Sagas.TransactionLog;

/// <summary>
/// Runtime state of a feature interaction within a Saga instance.
/// Tracks interaction count, cooldowns, and per-avatar interactions.
/// Derived by replaying EntityInteracted transactions.
/// </summary>
public class FeatureInteractionState
{
    /// <summary>
    /// Reference to the feature in the Saga template.
    /// </summary>
    public string FeatureRef { get; set; } = string.Empty;

    /// <summary>
    /// Total number of times this feature has been interacted with across all avatars.
    /// </summary>
    public int TotalInteractionCount { get; set; }

    /// <summary>
    /// When this feature was first interacted with by any avatar.
    /// </summary>
    public DateTime? FirstInteractedAt { get; set; }

    /// <summary>
    /// When this feature was last interacted with by any avatar.
    /// </summary>
    public DateTime? LastInteractedAt { get; set; }

    /// <summary>
    /// Per-avatar interaction tracking.
    /// Key: AvatarId
    /// Value: Last interaction timestamp (for cooldown checking)
    /// </summary>
    public Dictionary<string, AvatarFeatureInteraction> AvatarInteractions { get; set; } = new();
}

/// <summary>
/// Tracks a single avatar's interactions with a feature.
/// </summary>
public class AvatarFeatureInteraction
{
    /// <summary>
    /// Avatar ID.
    /// </summary>
    public string AvatarId { get; set; } = string.Empty;

    /// <summary>
    /// Number of times this avatar has interacted with the feature.
    /// </summary>
    public int InteractionCount { get; set; }

    /// <summary>
    /// When this avatar first interacted with the feature.
    /// </summary>
    public DateTime? FirstInteractedAt { get; set; }

    /// <summary>
    /// When this avatar last interacted with the feature.
    /// </summary>
    public DateTime LastInteractedAt { get; set; }
}
