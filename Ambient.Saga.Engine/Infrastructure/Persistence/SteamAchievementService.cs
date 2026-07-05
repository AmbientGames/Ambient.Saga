using System.Globalization;
using Ambient.Application.Contracts;
using Ambient.Domain.Entities;
using Ambient.Domain.GameLogic;
using LiteDB;
using Steamworks;

namespace Ambient.Saga.Engine.Infrastructure.Persistence;

/// <summary>
/// Wraps Steam achievement calls with local persistence.
/// Logs all achievements sent to Steam and replays them on world load.
/// Replay is driven by the avatar's persisted Achievements list — the single
/// unlock ledger (audit C2): ledger unlocks missing from the local sync journal
/// are seeded as pending records first, so achievements earned through dialogue,
/// quest rewards, or the evaluation pipeline reach Steam even though those paths
/// never call UnlockAchievement directly.
/// Takes the raw LiteDatabase so it works with both an owned WorldStateDatabase
/// (Sandbox path) and a shared/injected database (game host path).
/// </summary>
internal class SteamAchievementService : ISteamAchievementService
{
    private readonly LiteDatabase _database;
    private readonly bool _isSteamAvailable;

    public SteamAchievementService(LiteDatabase database, bool isSteamAvailable)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _isSteamAvailable = isSteamAvailable;
    }

    /// <summary>
    /// Unlocks an achievement and syncs to Steam.
    /// Always logs locally, attempts Steam sync if available.
    /// </summary>
    /// <param name="steamAchievementId">Steam API achievement ID (e.g., "ACH_DEFEAT_FIRST_BOSS")</param>
    /// <param name="avatarId">Avatar that earned the achievement</param>
    /// <param name="achievementTemplateRef">Optional reference to game Achievement template</param>
    /// <returns>True if successfully logged (Steam sync may be async)</returns>
    public bool UnlockAchievement(string steamAchievementId, string avatarId, string? achievementTemplateRef = null)
    {
        var collection = _database.GetCollection<SteamAchievementSync>("SteamAchievements");

        // Check if already unlocked for this avatar
        var existing = collection.FindOne(s =>
            s.SteamAchievementId == steamAchievementId &&
            s.AvatarId == avatarId);

        if (existing != null)
        {
            // Already unlocked, but retry sync if previous attempt failed
            if (existing.Status != SteamSyncStatus.Synced)
            {
                TrySyncToSteam(existing, collection);
            }
            return true;
        }

        // Create new sync record
        var sync = new SteamAchievementSync
        {
            SteamAchievementId = steamAchievementId,
            AvatarId = avatarId,
            AchievementTemplateRef = achievementTemplateRef,
            EarnedAt = DateTime.UtcNow,
            Status = SteamSyncStatus.Pending,
            RetryCount = 0
        };

        collection.Insert(sync);

        // Attempt Steam sync
        TrySyncToSteam(sync, collection);

        return true;
    }

    /// <summary>
    /// Replays all pending/failed achievements to Steam.
    /// Call this when a world/save is loaded.
    /// Reads the avatar's Achievements ledger first and seeds sync records for any
    /// unlock the journal doesn't know about (see class remarks), then pushes all
    /// non-synced records to Steam.
    /// </summary>
    /// <param name="avatarId">Optional: only replay for specific avatar</param>
    public void ReplayAchievementsToSteam(string? avatarId = null)
    {
        var collection = _database.GetCollection<SteamAchievementSync>("SteamAchievements");

        // Seed even when Steam is down — the records stay Pending and sync on a
        // later replay once Steam is available.
        SeedJournalFromAvatarLedger(collection, avatarId);

        if (!_isSteamAvailable)
            return;

        // Get all non-synced achievements
        var query = collection.Query();

        if (!string.IsNullOrEmpty(avatarId))
        {
            query = query.Where(s => s.AvatarId == avatarId);
        }

        var pendingAchievements = query
            .Where(s => s.Status != SteamSyncStatus.Synced)
            .ToList();

        foreach (var sync in pendingAchievements)
        {
            TrySyncToSteam(sync, collection);
        }

        // Store stats to Steam
        if (pendingAchievements.Any())
        {
            SteamUserStats.StoreStats();
        }
    }

    /// <summary>
    /// Seeds sync records for ledger unlocks missing from the journal.
    /// The avatar's persisted Achievements list is the single unlock ledger; the
    /// journal only tracks what has been handed to Steam. An achievement RefName
    /// IS the Steam API achievement id (by definition, see AchievementViewModel).
    /// </summary>
    private void SeedJournalFromAvatarLedger(ILiteCollection<SteamAchievementSync> collection, string? avatarId)
    {
        var avatar = new GameAvatarRepository(_database)
            .LoadAvatarAsync<AvatarEntity>()
            .GetAwaiter().GetResult();

        if (avatar?.Achievements == null)
            return;

        var ledgerAvatarId = avatar.AvatarId.ToString();
        if (!string.IsNullOrEmpty(avatarId) && !string.Equals(avatarId, ledgerAvatarId, StringComparison.OrdinalIgnoreCase))
            return; // Ledger belongs to a different avatar than the one being replayed

        foreach (var entry in avatar.Achievements)
        {
            var achievementRef = entry.AchievementRef;
            if (string.IsNullOrEmpty(achievementRef))
                continue;

            var existing = collection.FindOne(s =>
                s.SteamAchievementId == achievementRef &&
                s.AvatarId == ledgerAvatarId);

            if (existing != null)
                continue;

            collection.Insert(new SteamAchievementSync
            {
                SteamAchievementId = achievementRef,
                AvatarId = ledgerAvatarId,
                AchievementTemplateRef = achievementRef,
                EarnedAt = DateTime.TryParse(entry.UnlockedDate, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var earnedAt)
                    ? earnedAt
                    : DateTime.UtcNow,
                Status = SteamSyncStatus.Pending,
                RetryCount = 0
            });
        }
    }

    /// <summary>
    /// Gets all achievement sync records for an avatar.
    /// </summary>
    public List<SteamAchievementSync> GetAchievementSyncs(string avatarId)
    {
        var collection = _database.GetCollection<SteamAchievementSync>("SteamAchievements");
        return collection.Find(s => s.AvatarId == avatarId).ToList();
    }

    /// <summary>
    /// Gets sync status for a specific achievement.
    /// </summary>
    public SteamAchievementSync? GetAchievementSync(string steamAchievementId, string avatarId)
    {
        var collection = _database.GetCollection<SteamAchievementSync>("SteamAchievements");
        return collection.FindOne(s =>
            s.SteamAchievementId == steamAchievementId &&
            s.AvatarId == avatarId);
    }

    /// <summary>
    /// Clears an achievement (for testing).
    /// WARNING: This clears from Steam AND local database.
    /// </summary>
    public bool ClearAchievement(string steamAchievementId, string avatarId)
    {
        var collection = _database.GetCollection<SteamAchievementSync>("SteamAchievements");

        var sync = collection.FindOne(s =>
            s.SteamAchievementId == steamAchievementId &&
            s.AvatarId == avatarId);

        if (sync == null)
            return false;

        // Clear from Steam if available
        if (_isSteamAvailable)
        {
            SteamUserStats.ClearAchievement(steamAchievementId);
            SteamUserStats.StoreStats();
        }

        // Delete from local database
        collection.Delete(sync.Id);
        return true;
    }

    /// <summary>
    /// Gets basic Steam connection info for diagnostics.
    /// </summary>
    public string GetSteamConnectionInfo()
    {
        if (!_isSteamAvailable)
            return "Steam: NOT AVAILABLE";

        try
        {
            var appId = SteamUtils.GetAppID();
            var userName = SteamFriends.GetPersonaName();
            var steamId = SteamUser.GetSteamID();
            var numAchievements = SteamUserStats.GetNumAchievements();

            return $"Steam: CONNECTED\n" +
                   $"App ID: {appId}\n" +
                   $"User: {userName}\n" +
                   $"Steam ID: {steamId}\n" +
                   $"Achievements Available: {numAchievements}";
        }
        catch (Exception ex)
        {
            return $"Steam: ERROR - {ex.Message}";
        }
    }

    /// <summary>
    /// Lists all available achievements from Steam for the current app.
    /// </summary>
    public List<(string name, string displayName, string description, bool achieved)> GetAllAchievements()
    {
        var achievements = new List<(string name, string displayName, string description, bool achieved)>();

        if (!_isSteamAvailable)
            return achievements;

        try
        {
            var numAchievements = SteamUserStats.GetNumAchievements();

            for (uint i = 0; i < numAchievements; i++)
            {
                var name = SteamUserStats.GetAchievementName(i);
                var displayName = SteamUserStats.GetAchievementDisplayAttribute(name, "name");
                var description = SteamUserStats.GetAchievementDisplayAttribute(name, "desc");

                bool achieved;
                SteamUserStats.GetAchievement(name, out achieved);

                achievements.Add((name, displayName, description, achieved));
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Steam] Error getting achievements: {ex.Message}");
        }

        return achievements;
    }

    /// <summary>
    /// Directly queries Steam to check if an achievement is unlocked.
    /// Does NOT check local database - pure Steam query.
    /// </summary>
    /// <returns>Tuple of (isUnlocked, unlockTime, errorMessage). UnlockTime is 0 if not unlocked.</returns>
    public (bool isUnlocked, uint unlockTime, string? errorMessage) QuerySteamAchievementStatus(string steamAchievementId)
    {
        if (!_isSteamAvailable)
            return (false, 0, "Steam not available");

        try
        {
            bool achieved;
            uint unlockTime;
            var success = SteamUserStats.GetAchievementAndUnlockTime(steamAchievementId, out achieved, out unlockTime);

            if (!success)
                return (false, 0, $"GetAchievementAndUnlockTime returned false for '{steamAchievementId}' - may not exist");

            return (achieved, unlockTime, null);
        }
        catch (Exception ex)
        {
            return (false, 0, $"Exception: {ex.Message}");
        }
    }

    /// <summary>
    /// Pure Steam test: Set achievement directly to Steam without any database logging.
    /// For testing Steam API only.
    /// </summary>
    public (bool success, string? errorMessage) SetSteamAchievementDirect(string steamAchievementId)
    {
        if (!_isSteamAvailable)
            return (false, "Steam not available");

        try
        {
            var success = SteamUserStats.SetAchievement(steamAchievementId);
            if (!success)
            {
                return (false, $"SetAchievement returned false for '{steamAchievementId}' - achievement may not exist or already unlocked");
            }

            // Store the stats to commit to Steam
            var storeSuccess = SteamUserStats.StoreStats();
            if (!storeSuccess)
            {
                return (false, "SetAchievement succeeded but StoreStats failed");
            }

            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, $"Exception: {ex.Message}");
        }
    }

    /// <summary>
    /// Attempts to sync an achievement to Steam.
    /// Updates the sync record with results.
    /// </summary>
    private void TrySyncToSteam(SteamAchievementSync sync, LiteDB.ILiteCollection<SteamAchievementSync> collection)
    {
        if (!_isSteamAvailable)
        {
            sync.Status = SteamSyncStatus.SteamUnavailable;
            sync.LastError = "Steam not initialized";
            collection.Update(sync);
            return;
        }

        try
        {
            // Set achievement in Steam
            var success = SteamUserStats.SetAchievement(sync.SteamAchievementId);

            if (success)
            {
                sync.Status = SteamSyncStatus.Synced;
                sync.LastSyncedAt = DateTime.UtcNow;
                sync.LastError = null;
            }
            else
            {
                sync.Status = SteamSyncStatus.Failed;
                sync.RetryCount++;
                sync.LastError = "SteamUserStats.SetAchievement returned false";
            }

            collection.Update(sync);

            // Request stats store (this actually commits to Steam)
            // Note: This is async, so we mark as "Synced" optimistically
            SteamUserStats.StoreStats();
        }
        catch (Exception ex)
        {
            sync.Status = SteamSyncStatus.Failed;
            sync.RetryCount++;
            sync.LastError = ex.Message;
            collection.Update(sync);
        }
    }
}
