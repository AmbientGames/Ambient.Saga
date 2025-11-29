namespace Ambient.Saga.Engine.Domain.Rpg.Sagas.TransactionLog;

/// <summary>
/// Current state of a Saga instance.
/// Derived by replaying all committed transactions from the template.
/// This is a computed snapshot - the source of truth is the transaction log.
/// </summary>
public class SagaState
{
    /// <summary>
    /// Reference to the Saga template in the world definition.
    /// </summary>
    public string SagaRef { get; set; } = string.Empty;

    /// <summary>
    /// Current overall status of this Saga.
    /// </summary>
    public SagaStatus Status { get; set; } = SagaStatus.Undiscovered;

    /// <summary>
    /// When this Saga was first discovered by any player.
    /// </summary>
    public DateTime? FirstDiscoveredAt { get; set; }

    /// <summary>
    /// When this Saga was completed (all objectives done).
    /// </summary>
    public DateTime? CompletedAt { get; set; }

    /// <summary>
    /// State of each trigger in this Saga.
    /// Key: TriggerRef
    /// </summary>
    public Dictionary<string, SagaTriggerState> Triggers { get; set; } = new();

    /// <summary>
    /// All characters ever spawned in this Saga instance.
    /// Key: CharacterInstanceId as string (for LiteDB compatibility)
    /// Includes both currently spawned and despawned/defeated characters.
    /// </summary>
    public Dictionary<string, CharacterState> Characters { get; set; } = new();

    /// <summary>
    /// Which avatars have discovered this Saga.
    /// </summary>
    public HashSet<string> DiscoveredByAvatars { get; set; } = new();

    /// <summary>
    /// Which avatars have completed this Saga.
    /// </summary>
    public HashSet<string> CompletedByAvatars { get; set; } = new();

    /// <summary>
    /// Tracks all dialogue node visits by all avatars in this Saga.
    /// Key: "{AvatarId}_{CharacterRef}_{NodeId}"
    /// Used to ensure idempotent replay - dialogue rewards only given once.
    /// </summary>
    public Dictionary<string, DialogueVisit> DialogueNodeVisits { get; set; } = new();

    /// <summary>
    /// Tracks character traits assigned via dialogue.
    /// Key: CharacterRef, Value: List of traits currently active
    /// Used for relationship tracking and achievement progress.
    /// </summary>
    public Dictionary<string, List<string>> CharacterTraits { get; set; } = new();

    /// <summary>
    /// Tracks feature interactions (loot chests, landmarks, quest markers).
    /// Key: FeatureRef
    /// Used for cooldown checking, MaxInteractions limits, and per-avatar tracking.
    /// </summary>
    public Dictionary<string, FeatureInteractionState> FeatureInteractions { get; set; } = new();

    /// <summary>
    /// Active quests that have been accepted but not yet completed.
    /// Key: QuestRef
    /// Tracks current progress toward quest objectives.
    /// </summary>
    public Dictionary<string, QuestState> ActiveQuests { get; set; } = new();

    /// <summary>
    /// Quests that have been completed.
    /// Used to prevent re-acceptance and for achievement tracking.
    /// </summary>
    public HashSet<string> CompletedQuests { get; set; } = new();

    /// <summary>
    /// Faction reputation values for this avatar in this Saga.
    /// Key: FactionRef, Value: Reputation points (-42000 to +infinity)
    /// Tracks player standing with all factions, computed from ReputationChanged transactions.
    /// </summary>
    public Dictionary<string, int> FactionReputation { get; set; } = new();

    /// <summary>
    /// Number of transactions replayed to create this state.
    /// Used for snapshot optimization - if many transactions exist,
    /// create a snapshot to avoid replaying all of them every time.
    /// </summary>
    public int TransactionCount { get; set; }

    /// <summary>
    /// Timestamp when this state snapshot was created.
    /// </summary>
    public DateTime SnapshotAt { get; set; } = DateTime.UtcNow;
}
