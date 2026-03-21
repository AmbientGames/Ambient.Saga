namespace Ambient.Saga.Engine.Contracts;

/// <summary>
/// Syncs local Saga transaction logs to/from the server.
/// Implementation handles HTTP transport; callers just call PushAsync/PullAsync.
/// </summary>
public interface ISagaSyncService
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
