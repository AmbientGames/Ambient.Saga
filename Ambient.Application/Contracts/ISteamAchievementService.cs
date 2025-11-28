namespace Ambient.Application.Contracts;

/// <summary>
/// Interface for Steam achievement synchronization service.
/// </summary>
public interface ISteamAchievementService
{
    /// <summary>
    /// Unlocks an achievement and syncs to Steam.
    /// Always logs locally, attempts Steam sync if available.
    /// </summary>
    /// <param name="steamAchievementId">Steam API achievement ID (e.g., "ACH_DEFEAT_FIRST_BOSS")</param>
    /// <param name="avatarId">Avatar that earned the achievement</param>
    /// <param name="achievementTemplateRef">Optional reference to game Achievement template</param>
    /// <returns>True if successfully logged (Steam sync may be async)</returns>
    bool UnlockAchievement(string steamAchievementId, string avatarId, string? achievementTemplateRef = null);

    /// <summary>
    /// Replays all pending/failed achievements to Steam.
    /// Call this when a world/save is loaded.
    /// </summary>
    /// <param name="avatarId">Optional: only replay for specific avatar</param>
    void ReplayAchievementsToSteam(string? avatarId = null);

    /// <summary>
    /// Pure Steam test: Set achievement directly to Steam without any database logging.
    /// For testing Steam API only.
    /// </summary>
    /// <returns>Tuple of (success, errorMessage)</returns>
    (bool success, string? errorMessage) SetSteamAchievementDirect(string steamAchievementId);
}