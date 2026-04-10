using Ambient.Application.Contracts;
using Ambient.Domain;
using Ambient.Domain.Contracts;
using Ambient.Domain.Partials;
using Ambient.Domain.Entities;
using Ambient.Saga.Engine.Contracts;
using Ambient.Saga.Engine.Contracts.Cqrs;
using Ambient.Saga.Engine.Domain.Achievements;
using Ambient.Saga.Engine.Domain.Rpg.Sagas.TransactionLog;

namespace Ambient.Saga.Engine.Infrastructure.Persistence;

/// <summary>
/// Repository for managing persisted world state.
/// Uses the CQRS SagaInstanceRepository for all Saga operations.
/// Provides simple CRUD for avatar state.
/// </summary>
internal class WorldStateRepository : IWorldStateRepository
{
    private readonly ISagaInstanceRepository _sagaRepository;
    private readonly IGameAvatarRepository _avatarRepository;
    private readonly IRepository<AchievementInstance> _achievementRepository;
    private readonly IAvatarDiscoveryRepository _discoveryRepository;
    private readonly IWorld _world;

    public WorldStateRepository(
        ISagaInstanceRepository sagaRepository,
        IGameAvatarRepository avatarRepository,
        IRepository<AchievementInstance> achievementRepository,
        IAvatarDiscoveryRepository discoveryRepository,
        IWorld world)
    {
        _sagaRepository = sagaRepository ?? throw new ArgumentNullException(nameof(sagaRepository));
        _avatarRepository = avatarRepository ?? throw new ArgumentNullException(nameof(avatarRepository));
        _achievementRepository = achievementRepository ?? throw new ArgumentNullException(nameof(achievementRepository));
        _discoveryRepository = discoveryRepository ?? throw new ArgumentNullException(nameof(discoveryRepository));
        _world = world ?? throw new ArgumentNullException(nameof(world));
    }

    #region Saga Operations (Delegated to CQRS Repository)

    /// <summary>
    /// Gets a Saga instance by template RefName for a specific avatar.
    /// </summary>
    public async Task<SagaInstance?> GetSagaInstanceAsync(string avatarId, string templateRef)
    {
        var avatarGuid = Guid.Parse(avatarId);
        return await _sagaRepository.GetOrCreateInstanceAsync(avatarGuid, templateRef);
    }

    #endregion

    #region Avatar Persistence (Simple CRUD)

    /// <summary>
    /// Loads avatar from database, or returns null if not found.
    /// </summary>
    public async Task<AvatarEntity?> LoadAvatarAsync()
    {
        return await _avatarRepository.LoadAvatarAsync<AvatarEntity>();
    }

    /// <summary>
    /// Saves avatar to database (creates if new, updates if exists).
    /// </summary>
    public async Task SaveAvatarAsync(AvatarEntity avatarEntity)
    {
        await _avatarRepository.SaveAvatarAsync(avatarEntity);
    }

    /// <summary>
    /// Deletes all avatars from database.
    /// </summary>
    public async Task DeleteAvatarsAsync()
    {
        await _avatarRepository.DeleteAvatarsAsync();
    }

    #endregion

    #region Achievement Instances (Per Avatar)

    /// <summary>
    /// Gets or creates AchievementInstance objects for a specific avatar.
    /// </summary>
    public async Task<List<AchievementInstance>> GetOrCreateAchievementInstancesAsync(string avatarId)
    {
        var existingInstances = (await _achievementRepository.FindAsync(a => a.AvatarId == avatarId)).ToList();
        if (existingInstances.Any())
            return existingInstances;

        // First time: create instances from templates
        var instances = new List<AchievementInstance>();
        foreach (var template in _world.Gameplay.Achievements ?? [])
        {
            var instance = new AchievementInstance
            {
                TemplateRef = template.RefName,
                InstanceId = Guid.NewGuid().ToString(),
                AvatarId = avatarId,
                CurrentProgress = 0,
                IsUnlocked = false
            };
            instances.Add(instance);
        }

        if (instances.Any())
            await _achievementRepository.InsertManyAsync(instances);
        return instances;
    }

    /// <summary>
    /// Saves AchievementInstance state.
    /// </summary>
    public async Task SaveAchievementAsync(AchievementInstance instance)
    {
        await _achievementRepository.UpsertAsync(instance);
    }

    #endregion

    #region Avatar Discovery Tracking

    /// <summary>
    /// Records an avatar discovery (lore, achievement, Saga, etc.).
    /// </summary>
    public async Task<AvatarDiscovery> RecordDiscoveryAsync(string avatarId, string entityType, string entityRef, Dictionary<string, string>? metadata = null)
    {
        var existing = await _discoveryRepository.FindOneAsync<AvatarDiscovery>(avatarId, entityType, entityRef);

        if (existing != null)
            return existing;

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

        await _discoveryRepository.InsertAsync(discovery);
        return discovery;
    }

    /// <summary>
    /// Records a trigger event for an avatar discovery.
    /// </summary>
    public async Task RecordTriggerAsync(string avatarId, string entityType, string entityRef)
    {
        var discovery = await _discoveryRepository.FindOneAsync<AvatarDiscovery>(avatarId, entityType, entityRef);

        if (discovery == null)
        {
            discovery = await RecordDiscoveryAsync(avatarId, entityType, entityRef);
        }

        discovery.RecordTrigger();
        await _discoveryRepository.UpdateAsync(discovery);
    }

    /// <summary>
    /// Gets the last trigger time for a specific avatar/entity combination.
    /// </summary>
    public async Task<DateTime?> GetLastTriggerTimeAsync(string avatarId, string entityType, string entityRef)
    {
        var discovery = await _discoveryRepository.FindOneAsync<AvatarDiscovery>(avatarId, entityType, entityRef);
        return discovery?.LastTriggeredAt;
    }

    /// <summary>
    /// Checks if an avatar has discovered a specific entity.
    /// </summary>
    public async Task<bool> HasDiscoveredAsync(string avatarId, string entityType, string entityRef)
    {
        return await _discoveryRepository.ExistsAsync(avatarId, entityType, entityRef);
    }

    /// <summary>
    /// Gets all discoveries for a specific avatar.
    /// </summary>
    public async Task<List<AvatarDiscovery>> GetAvatarDiscoveriesAsync(string avatarId)
    {
        return await _discoveryRepository.GetByAvatarIdAsync<AvatarDiscovery>(avatarId);
    }

    #endregion
}
