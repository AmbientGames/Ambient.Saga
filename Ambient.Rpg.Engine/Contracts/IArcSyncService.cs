namespace Ambient.Rpg.Engine.Contracts;

/// <summary>
/// Syncs local Arc transaction logs to/from the server.
/// Implementation handles HTTP transport; callers just call PushAsync/PullAsync.
/// </summary>
public interface IArcSyncService
{
    /// <summary>
    /// Push local transactions (and avatar state) to the server.
    /// Called periodically during gameplay.
    /// </summary>
    Task<bool> PushAsync(Guid avatarId);

    /// <summary>
    /// Pull transactions from the server to catch up on progress from other devices.
    /// Called once on world join.
    /// </summary>
    Task<bool> PullAsync(Guid avatarId);
}
