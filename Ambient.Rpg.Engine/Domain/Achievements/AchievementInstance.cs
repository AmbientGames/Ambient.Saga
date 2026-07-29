namespace Ambient.Rpg.Engine.Domain.Achievements;

/// <summary>
/// Per-avatar achievement state, computed on demand.
/// Progress is evaluated from Arc transaction logs; unlock state is projected
/// from (and persisted to) the avatar's Achievements list — the single unlock
/// ledger (audit C2). This type is a transient evaluation/projection DTO; the
/// former dedicated LiteDB collection for it was removed.
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
