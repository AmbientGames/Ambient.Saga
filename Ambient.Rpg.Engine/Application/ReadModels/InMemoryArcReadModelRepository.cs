using Ambient.Rpg.Engine.Domain.Arcs.TransactionLog;
using System.Collections.Concurrent;

namespace Ambient.Rpg.Engine.Application.ReadModels;

/// <summary>
/// In-memory implementation of Arc read model repository.
/// Simple cache for single-server scenarios. For multiplayer, use Redis/SQL.
/// </summary>
public class InMemoryArcReadModelRepository : IArcReadModelRepository
{
    private readonly ConcurrentDictionary<string, CachedArcState> _cache = new();

    private static string GetCacheKey(Guid avatarId, string arcRef) => $"{avatarId}:{arcRef}";

    public Task<ArcState?> GetCachedStateAsync(Guid avatarId, string arcRef, CancellationToken ct = default)
    {
        var key = GetCacheKey(avatarId, arcRef);
        if (_cache.TryGetValue(key, out var cached))
        {
            return Task.FromResult<ArcState?>(cached.State);
        }
        return Task.FromResult<ArcState?>(null);
    }

    public Task UpdateCachedStateAsync(Guid avatarId, string arcRef, ArcState state, long sequenceNumber, CancellationToken ct = default)
    {
        var key = GetCacheKey(avatarId, arcRef);
        _cache[key] = new CachedArcState
        {
            State = state,
            SequenceNumber = sequenceNumber,
            CachedAt = DateTime.UtcNow
        };
        return Task.CompletedTask;
    }

    public Task InvalidateCacheAsync(Guid avatarId, string arcRef, CancellationToken ct = default)
    {
        var key = GetCacheKey(avatarId, arcRef);
        _cache.TryRemove(key, out _);
        return Task.CompletedTask;
    }

    public Task<long> GetCachedSequenceNumberAsync(Guid avatarId, string arcRef, CancellationToken ct = default)
    {
        var key = GetCacheKey(avatarId, arcRef);
        if (_cache.TryGetValue(key, out var cached))
        {
            return Task.FromResult(cached.SequenceNumber);
        }
        return Task.FromResult(-1L);
    }

    private class CachedArcState
    {
        public required ArcState State { get; init; }
        public required long SequenceNumber { get; init; }
        public DateTime CachedAt { get; init; }
    }
}
