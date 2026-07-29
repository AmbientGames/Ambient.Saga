namespace Ambient.Rpg.Engine.Domain.Arcs.TransactionLog;

/// <summary>
/// Current state of an arc instance.
/// Derived by replaying all committed transactions from the template.
/// This is a computed snapshot - the source of truth is the transaction log.
/// </summary>
public class ArcState
{
    /// <summary>
    /// Reference to the arc template in the world definition.
    /// </summary>
    public string ArcRef { get; set; } = string.Empty;

    /// <summary>
    /// Current overall status of this arc.
    /// </summary>
    public ArcStatus Status { get; set; } = ArcStatus.Undiscovered;

    /// <summary>
    /// When this arc was first discovered by any avatar.
    /// </summary>
    public DateTime? FirstDiscoveredAt { get; set; }

    /// <summary>
    /// When this arc was completed (all objectives done).
    /// </summary>
    public DateTime? CompletedAt { get; set; }

    /// <summary>
    /// State of each trigger in this arc.
    /// Key: TriggerRef
    /// </summary>
    public Dictionary<string, ArcTriggerState> Triggers { get; set; } = new();

    /// <summary>
    /// All characters ever spawned in this arc instance.
    /// Key: CharacterInstanceId as string (for LiteDB compatibility)
    /// Includes both currently spawned and despawned/defeated characters.
    /// </summary>
    public Dictionary<string, CharacterState> Characters { get; set; } = new();

    /// <summary>
    /// Which avatars have discovered this arc.
    /// </summary>
    public HashSet<string> DiscoveredByAvatars { get; set; } = new();

    /// <summary>
    /// Which avatars have completed this arc.
    /// </summary>
    public HashSet<string> CompletedByAvatars { get; set; } = new();

    /// <summary>
    /// Tracks all dialogue node visits by all avatars in this arc.
    /// Key: "{AvatarId}_{CharacterRef}_{NodeId}"
    /// Used to ensure idempotent replay - dialogue rewards only given once.
    /// </summary>
    public Dictionary<string, DialogueVisit> DialogueNodeVisits { get; set; } = new();

    /// <summary>
    /// Per-item listing prices the shop owner has set on this arc (ShopPriceSet
    /// transactions; last write wins on replay, price 0 clears the listing).
    /// Key: ItemRef. A listed item sells to visitors at EXACTLY this price instead of
    /// catalog x markup — bread dear in the mountains, ore cheap. Market arcs only.
    /// </summary>
    public Dictionary<string, int> ShopPrices { get; set; } = new();

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
    /// Faction reputation values for this avatar in this arc.
    /// Key: FactionRef, Value: Reputation points (-42000 to +infinity)
    /// Tracks avatar standing with all factions, computed from ReputationChanged transactions.
    /// </summary>
    public Dictionary<string, int> FactionReputation { get; set; } = new();

    /// <summary>
    /// Quest tokens awarded within THIS arc's transaction log.
    /// Populated during replay — shows which tokens originated in this arc.
    /// Gating decisions read from the avatar progress table (cross-arc), not this field.
    /// </summary>
    public HashSet<string> AwardedQuestTokens { get; set; } = new();

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
