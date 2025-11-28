namespace Ambient.Domain.GameLogic;

/// <summary>
/// Tracks Steam achievement synchronization state.
/// Logs all achievements sent to Steam and enables replay on world load.
/// </summary>
public class SteamAchievementSync
{
    /// <summary>
    /// LiteDB document ID.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Steam API achievement identifier (e.g., "ACH_DEFEAT_FIRST_BOSS").
    /// </summary>
    public string SteamAchievementId { get; set; } = string.Empty;

    /// <summary>
    /// Avatar ID that earned this achievement.
    /// </summary>
    public string AvatarId { get; set; } = string.Empty;

    /// <summary>
    /// Reference to the game's Achievement template (optional, for lookup).
    /// </summary>
    public string? AchievementTemplateRef { get; set; }

    /// <summary>
    /// Timestamp when achievement was earned locally.
    /// </summary>
    public DateTime EarnedAt { get; set; }

    /// <summary>
    /// Timestamp when achievement was last sent to Steam.
    /// </summary>
    public DateTime? LastSyncedAt { get; set; }

    /// <summary>
    /// Synchronization status.
    /// </summary>
    public SteamSyncStatus Status { get; set; }

    /// <summary>
    /// Number of retry attempts (for failed syncs).
    /// </summary>
    public int RetryCount { get; set; }

    /// <summary>
    /// Error message if sync failed.
    /// </summary>
    public string? LastError { get; set; }
}

/// <summary>
/// Steam synchronization status.
/// </summary>
public enum SteamSyncStatus
{
    /// <summary>
    /// Achievement earned locally but not yet sent to Steam.
    /// </summary>
    Pending = 0,

    /// <summary>
    /// Achievement successfully sent to Steam.
    /// </summary>
    Synced = 1,

    /// <summary>
    /// Failed to send to Steam (will retry).
    /// </summary>
    Failed = 2,

    /// <summary>
    /// Steam not available (will retry when Steam is available).
    /// </summary>
    SteamUnavailable = 3
}
