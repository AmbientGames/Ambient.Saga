namespace Ambient.Saga.Engine.Domain.Achievements;

/// <summary>
/// Per-avatar achievement state stored in LiteDB.
/// Progress is computed from Saga transaction logs, then cached here for performance.
/// This is NOT event-sourced - it's a computed cache of achievement state.
/// </summary>
public class AchievementInstance
{
    /// <summary>
    /// Unique identifier for this achievement instance.
    /// </summary>
    public string InstanceId { get; set; } = string.Empty;

    /// <summary>
    /// Reference to the achievement template.
    /// </summary>
    public string TemplateRef { get; set; } = string.Empty;

    /// <summary>
    /// Avatar ID that owns this achievement instance.
    /// </summary>
    public string AvatarId { get; set; } = string.Empty;

    /// <summary>
    /// Current progress toward the achievement criteria threshold.
    /// Stored as integer percentage (0-100) for LiteDB efficiency.
    /// </summary>
    public int CurrentProgress { get; set; }

    /// <summary>
    /// Indicates whether this achievement has been unlocked.
    /// </summary>
    public bool IsUnlocked { get; set; }

    /// <summary>
    /// Time when this achievement was unlocked (null if not unlocked).
    /// </summary>
    public DateTime? UnlockedAt { get; set; }
}
