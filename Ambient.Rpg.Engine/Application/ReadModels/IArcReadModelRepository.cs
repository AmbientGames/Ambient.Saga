using Ambient.Rpg.Engine.Domain.Arcs.TransactionLog;

namespace Ambient.Rpg.Engine.Application.ReadModels;

/// <summary>
/// Repository for Arc read models (cached state for fast queries).
/// Implementations can use in-memory cache, Redis, SQL, etc.
/// </summary>
public interface IArcReadModelRepository
{
    /// <summary>
    /// Get cached Arc state for avatar.
    /// Returns null if not cached (caller should rebuild from transactions).
    /// </summary>
    Task<ArcState?> GetCachedStateAsync(Guid avatarId, string arcRef, CancellationToken ct = default);

    /// <summary>
    /// Update cached Arc state after transactions applied.
    /// </summary>
    Task UpdateCachedStateAsync(Guid avatarId, string arcRef, ArcState state, long sequenceNumber, CancellationToken ct = default);

    /// <summary>
    /// Invalidate cached state (forcing rebuild on next query).
    /// </summary>
    Task InvalidateCacheAsync(Guid avatarId, string arcRef, CancellationToken ct = default);

    /// <summary>
    /// Get cached state's sequence number (for checking if cache is stale).
    /// Returns -1 if not cached.
    /// </summary>
    Task<long> GetCachedSequenceNumberAsync(Guid avatarId, string arcRef, CancellationToken ct = default);
}
