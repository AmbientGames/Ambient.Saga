namespace Ambient.SagaEngine.Domain.Rpg.Sagas.TransactionLog;

/// <summary>
/// Tracks a player's visit to a specific dialogue node.
/// Used to ensure idempotent replay - dialogue actions (give items, assign traits) only execute once per visit.
/// </summary>
public class DialogueVisit
{
    /// <summary>
    /// Unique key for this visit.
    /// Format: "{AvatarId}_{CharacterRef}_{NodeId}"
    /// Example: "player123_MerchantNPC_reward_node"
    /// </summary>
    public string VisitKey { get; set; } = string.Empty;

    /// <summary>
    /// Which avatar visited this node.
    /// </summary>
    public string AvatarId { get; set; } = string.Empty;

    /// <summary>
    /// Which character's dialogue tree was being navigated.
    /// </summary>
    public string CharacterRef { get; set; } = string.Empty;

    /// <summary>
    /// Which dialogue tree was being navigated.
    /// </summary>
    public string DialogueTreeRef { get; set; } = string.Empty;

    /// <summary>
    /// Which node in the dialogue tree was visited.
    /// </summary>
    public string DialogueNodeId { get; set; } = string.Empty;

    /// <summary>
    /// When this node was first visited.
    /// </summary>
    public DateTime FirstVisitedAt { get; set; }

    /// <summary>
    /// When this node was most recently visited (null if only visited once).
    /// </summary>
    public DateTime? LastVisitedAt { get; set; }

    /// <summary>
    /// How many times this node has been visited.
    /// </summary>
    public int VisitCount { get; set; } = 1;

    /// <summary>
    /// Items that were awarded on first visit.
    /// Comma-separated list of RefNames: "LegendarySword,HealthPotion,GoldCoin"
    /// Empty if no items awarded.
    /// </summary>
    public string ItemsAwarded { get; set; } = string.Empty;

    /// <summary>
    /// Traits that were assigned on first visit.
    /// Comma-separated list of trait types: "Friendly,TradeDiscount"
    /// Empty if no traits assigned.
    /// </summary>
    public string TraitsAssigned { get; set; } = string.Empty;

    /// <summary>
    /// Quest tokens that were awarded on first visit.
    /// Comma-separated list of RefNames: "DragonSlayerQuest_Completed"
    /// Empty if no quest tokens awarded.
    /// </summary>
    public string QuestTokensAwarded { get; set; } = string.Empty;

    /// <summary>
    /// Currency amount that was transferred on first visit.
    /// Positive = given to player, Negative = taken from player.
    /// </summary>
    public int CurrencyTransferred { get; set; }

    /// <summary>
    /// Checks if this is the first visit to this node.
    /// Used by SagaStateMachine to determine whether to apply actions.
    /// </summary>
    public bool IsFirstVisit => VisitCount == 1;

    /// <summary>
    /// Checks if any rewards were given on first visit.
    /// </summary>
    public bool HasRewards =>
        !string.IsNullOrEmpty(ItemsAwarded) ||
        !string.IsNullOrEmpty(TraitsAssigned) ||
        !string.IsNullOrEmpty(QuestTokensAwarded) ||
        CurrencyTransferred != 0;
}
