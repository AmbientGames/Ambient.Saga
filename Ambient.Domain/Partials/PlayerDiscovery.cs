namespace Ambient.Domain.Partials;

/// <summary>
/// Tracks player discovery/interaction relationships with world entities.
/// Used for multiplayer state management - prevents entity bloat while supporting queries.
/// </summary>
public class PlayerDiscovery
{
    /// <summary>
    /// Unique identifier for this discovery record.
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// The avatar that made this discovery.
    /// </summary>
    public string AvatarId { get; set; } = string.Empty;

    /// <summary>
    /// Type of entity discovered (Landmark, Achievement, Saga, etc.).
    /// </summary>
    public string EntityType { get; set; } = string.Empty;

    /// <summary>
    /// Reference name of the entity (matches RefName in catalogs).
    /// </summary>
    public string EntityRef { get; set; } = string.Empty;

    /// <summary>
    /// When this entity was first discovered by this player.
    /// </summary>
    public DateTime DiscoveredAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When this entity was last triggered by this player (for cooldown tracking).
    /// Null if never triggered, or same as DiscoveredAt on first trigger.
    /// </summary>
    public DateTime? LastTriggeredAt { get; set; }

    /// <summary>
    /// Number of times this player has triggered this entity.
    /// </summary>
    public int TriggerCount { get; set; } = 0;

    /// <summary>
    /// Optional metadata (conditions, context, rewards granted, etc.).
    /// </summary>
    public Dictionary<string, string> Metadata { get; set; } = new();

    /// <summary>
    /// Records a trigger event for this discovery.
    /// </summary>
    public void RecordTrigger()
    {
        LastTriggeredAt = DateTime.UtcNow;
        TriggerCount++;
    }
}
