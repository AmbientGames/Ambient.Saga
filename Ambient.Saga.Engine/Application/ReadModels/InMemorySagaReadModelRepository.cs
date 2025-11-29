using Ambient.Saga.Engine.Domain.Rpg.Sagas.TransactionLog;
using System.Collections.Concurrent;

namespace Ambient.Saga.Engine.Application.ReadModels;

/// <summary>
/// In-memory implementation of Saga read model repository.
/// Simple cache for single-server scenarios. For multiplayer, use Redis/SQL.
/// </summary>
public class InMemorySagaReadModelRepository : ISagaReadModelRepository
{
    private readonly ConcurrentDictionary<string, CachedSagaState> _cache = new();

    private static string GetCacheKey(Guid avatarId, string sagaRef) => $"{avatarId}:{sagaRef}";

    public Task<SagaState?> GetCachedStateAsync(Guid avatarId, string sagaRef, CancellationToken ct = default)
    {
        var key = GetCacheKey(avatarId, sagaRef);
        if (_cache.TryGetValue(key, out var cached))
        {
            return Task.FromResult<SagaState?>(cached.State);
        }
        return Task.FromResult<SagaState?>(null);
    }

    public Task UpdateCachedStateAsync(Guid avatarId, string sagaRef, SagaState state, long sequenceNumber, CancellationToken ct = default)
    {
        var key = GetCacheKey(avatarId, sagaRef);
        _cache[key] = new CachedSagaState
        {
            State = state,
            SequenceNumber = sequenceNumber,
            CachedAt = DateTime.UtcNow
        };
        return Task.CompletedTask;
    }

    public Task InvalidateCacheAsync(Guid avatarId, string sagaRef, CancellationToken ct = default)
    {
        var key = GetCacheKey(avatarId, sagaRef);
        _cache.TryRemove(key, out _);
        return Task.CompletedTask;
    }

    public Task<long> GetCachedSequenceNumberAsync(Guid avatarId, string sagaRef, CancellationToken ct = default)
    {
        var key = GetCacheKey(avatarId, sagaRef);
        if (_cache.TryGetValue(key, out var cached))
        {
            return Task.FromResult(cached.SequenceNumber);
        }
        return Task.FromResult(-1L);
    }

    private class CachedSagaState
    {
        public required SagaState State { get; init; }
        public required long SequenceNumber { get; init; }
        public DateTime CachedAt { get; init; }
    }
}
