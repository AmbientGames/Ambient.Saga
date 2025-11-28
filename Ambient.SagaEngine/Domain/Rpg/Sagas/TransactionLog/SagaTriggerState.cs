namespace Ambient.SagaEngine.Domain.Rpg.Sagas.TransactionLog;

/// <summary>
/// Runtime state of a trigger within a Saga instance.
/// Derived by replaying transactions.
/// </summary>
public class SagaTriggerState
{
    /// <summary>
    /// Reference to the trigger in the Saga template.
    /// </summary>
    public string SagaTriggerRef { get; set; } = string.Empty;

    /// <summary>
    /// Current status of this trigger.
    /// </summary>
    public SagaTriggerStatus Status { get; set; } = SagaTriggerStatus.Inactive;

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
    /// Used for per-player trigger limits and progression tracking.
    /// </summary>
    public HashSet<string> TriggeredByAvatars { get; set; } = new();

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
