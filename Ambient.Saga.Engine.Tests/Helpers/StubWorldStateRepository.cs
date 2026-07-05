using Ambient.Domain.Partials;
using Ambient.Domain.Entities;
using Ambient.Saga.Engine.Contracts;
using Ambient.Saga.Engine.Domain.Rpg.Sagas.TransactionLog;

namespace Ambient.Saga.Engine.Tests.Helpers;

/// <summary>
/// Stub implementation of IWorldStateRepository for testing.
/// Provides minimal no-op implementations for tests that need the repository registered
/// but don't exercise its functionality.
/// </summary>
public class StubWorldStateRepository : IWorldStateRepository
{
    private readonly Dictionary<string, SagaInstance> _sagaInstances = new();
    private readonly Dictionary<string, AvatarEntity> _avatars = new();
    private readonly List<AvatarDiscovery> _discoveries = new();

    public void AddSagaInstance(string avatarId, string templateRef, SagaInstance instance)
    {
        var key = $"{avatarId}:{templateRef}";
        _sagaInstances[key] = instance;
    }

    public Task<SagaInstance?> GetSagaInstanceAsync(string avatarId, string templateRef)
    {
        var key = $"{avatarId}:{templateRef}";
        _sagaInstances.TryGetValue(key, out var instance);
        return Task.FromResult(instance);
    }

    public Task<AvatarEntity?> LoadAvatarAsync()
    {
        var avatar = _avatars.Values.FirstOrDefault();
        return Task.FromResult(avatar);
    }

    public Task SaveAvatarAsync(AvatarEntity avatarEntity)
    {
        _avatars[avatarEntity.AvatarId.ToString()] = avatarEntity;
        return Task.CompletedTask;
    }

    public Task DeleteAvatarsAsync()
    {
        _avatars.Clear();
        return Task.CompletedTask;
    }

    public Task<AvatarDiscovery> RecordDiscoveryAsync(string avatarId, string entityType, string entityRef, Dictionary<string, string>? metadata = null)
    {
        var existing = _discoveries.FirstOrDefault(d =>
            d.AvatarId == avatarId && d.EntityType == entityType && d.EntityRef == entityRef);

        if (existing != null)
            return Task.FromResult(existing);

        var discovery = new AvatarDiscovery
        {
            AvatarId = avatarId,
            EntityType = entityType,
            EntityRef = entityRef,
            DiscoveredAt = DateTime.UtcNow,
            LastTriggeredAt = null,
            TriggerCount = 0,
            Metadata = metadata ?? new Dictionary<string, string>()
        };
        _discoveries.Add(discovery);
        return Task.FromResult(discovery);
    }

    public Task RecordTriggerAsync(string avatarId, string entityType, string entityRef)
    {
        var discovery = _discoveries.FirstOrDefault(d =>
            d.AvatarId == avatarId && d.EntityType == entityType && d.EntityRef == entityRef);

        discovery?.RecordTrigger();
        return Task.CompletedTask;
    }

    public Task<DateTime?> GetLastTriggerTimeAsync(string avatarId, string entityType, string entityRef)
    {
        var discovery = _discoveries.FirstOrDefault(d =>
            d.AvatarId == avatarId && d.EntityType == entityType && d.EntityRef == entityRef);
        return Task.FromResult(discovery?.LastTriggeredAt);
    }

    public Task<bool> HasDiscoveredAsync(string avatarId, string entityType, string entityRef)
    {
        var exists = _discoveries.Any(d =>
            d.AvatarId == avatarId && d.EntityType == entityType && d.EntityRef == entityRef);
        return Task.FromResult(exists);
    }

    public Task<List<AvatarDiscovery>> GetAvatarDiscoveriesAsync(string avatarId)
    {
        var discoveries = _discoveries.Where(d => d.AvatarId == avatarId).ToList();
        return Task.FromResult(discoveries);
    }
}
