namespace Ambient.Saga.Engine.Contracts;

/// <summary>
/// Syncs local Saga transaction logs to the server.
/// Implementation handles HTTP transport; callers just call SyncAsync.
/// </summary>
public interface ISagaSyncService
{
    Task<bool> SyncAsync(Guid avatarId);
}
