namespace Ambient.Rpg.Engine.Domain.Arcs.TransactionLog;

/// <summary>
/// Runtime state of a trigger within an arc instance.
/// Derived by replaying transactions.
/// </summary>
public class ArcTriggerState
{
    /// <summary>
    /// Reference to the trigger in the arc template.
    /// </summary>
    public string ArcTriggerRef { get; set; } = string.Empty;

    /// <summary>
    /// Current status of this trigger.
    /// </summary>
    public ArcTriggerStatus Status { get; set; } = ArcTriggerStatus.Inactive;

    /// <summary>
    /// When this trigger was first activated.
    /// </summary>
    public DateTime? FirstActivatedAt { get; set; }

    /// <summary>
    /// When this trigger was last activated.
    /// </summary>
    public DateTime? LastActivatedAt { get; set; }

    /// <summary>
    /// Number of times this trigger has been activated.
    /// </summary>
    public int ActivationCount { get; set; }

    /// <summary>
    /// Set of avatar IDs that have triggered this.
    /// Used for per-avatar trigger limits and progression tracking.
    /// </summary>
    public HashSet<string> TriggeredByAvatars { get; set; } = new();

    /// <summary>
    /// Avatars currently inside this trigger's ring: added by AvatarEntered,
    /// removed by AvatarExited. This is what makes exit emission transition-based —
    /// ArcInteractionService only records an AvatarExited when the avatar is in
    /// this set, so leaving the ring produces exactly one exit transaction instead
    /// of one per position tick (audit B9).
    /// </summary>
    public HashSet<string> OccupyingAvatars { get; set; } = new();

    /// <summary>
    /// When this trigger was completed (null if not completed).
    /// </summary>
    public DateTime? CompletedAt { get; set; }

    /// <summary>
    /// Characters spawned by this trigger.
    /// Key: CharacterInstanceId as string (for LiteDB compatibility)
    /// </summary>
    public Dictionary<string, CharacterState> SpawnedCharacters { get; set; } = new();
}
