using Ambient.SagaEngine.Domain.Rpg.Sagas.TransactionLog;

namespace Ambient.SagaEngine.Application.ReadModels;

/// <summary>
/// Repository for Saga read models (cached state for fast queries).
/// Implementations can use in-memory cache, Redis, SQL, etc.
/// </summary>
public interface ISagaReadModelRepository
{
    /// <summary>
    /// Get cached Saga state for avatar.
    /// Returns null if not cached (caller should rebuild from transactions).
    /// </summary>
    Task<SagaState?> GetCachedStateAsync(Guid avatarId, string sagaRef, CancellationToken ct = default);

    /// <summary>
    /// Update cached Saga state after transactions applied.
    /// </summary>
    Task UpdateCachedStateAsync(Guid avatarId, string sagaRef, SagaState state, long sequenceNumber, CancellationToken ct = default);

    /// <summary>
    /// Invalidate cached state (forcing rebuild on next query).
    /// </summary>
    Task InvalidateCacheAsync(Guid avatarId, string sagaRef, CancellationToken ct = default);

    /// <summary>
    /// Get cached state's sequence number (for checking if cache is stale).
    /// Returns -1 if not cached.
    /// </summary>
    Task<long> GetCachedSequenceNumberAsync(Guid avatarId, string sagaRef, CancellationToken ct = default);
}
