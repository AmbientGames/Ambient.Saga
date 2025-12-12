using Ambient.Application.Contracts;
using Ambient.Domain;
using Ambient.Domain.Contracts;
using Ambient.Domain.DefinitionExtensions;
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
    private readonly IPlayerDiscoveryRepository _discoveryRepository;
    private readonly IWorld _world;

    public WorldStateRepository(
        ISagaInstanceRepository sagaRepository,
        IGameAvatarRepository avatarRepository,
        IRepository<AchievementInstance> achievementRepository,
        IPlayerDiscoveryRepository discoveryRepository,
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
    /// Gets or creates Saga instances for single-player.
    /// Each avatar gets their own instance of each Saga.
    /// </summary>
    public async Task<List<SagaInstance>> GetOrCreateSagaInstancesAsync(string avatarId)
    {
        var instances = new List<SagaInstance>();
        var avatarGuid = Guid.Parse(avatarId);

        foreach (var sagaTemplate in _world.Gameplay.SagaArcs ?? [])
        {
            var instance = await _sagaRepository.GetOrCreateInstanceAsync(avatarGuid, sagaTemplate.RefName);
            instances.Add(instance);
        }
        return instances;
    }

    /// <summary>
    /// Gets a Saga instance by template RefName for a specific avatar.
    /// </summary>
    public async Task<SagaInstance?> GetSagaInstanceAsync(string avatarId, string templateRef)
    {
        var avatarGuid = Guid.Parse(avatarId);
        return await _sagaRepository.GetOrCreateInstanceAsync(avatarGuid, templateRef);
    }

    /// <summary>
    /// Adds a transaction to a Saga instance and saves it.
    /// </summary>
    public async Task AddSagaTransactionAsync(Guid instanceId, SagaTransaction transaction)
    {
        await _sagaRepository.AddTransactionsAsync(instanceId, new List<SagaTransaction> { transaction });
    }

    /// <summary>
    /// Gets the current derived state of a Saga by replaying its transactions.
    /// </summary>
    public async Task<SagaState> GetSagaStateAsync(Guid instanceId)
    {
        var instance = await _sagaRepository.GetInstanceByIdAsync(instanceId);
        if (instance == null)
            throw new InvalidOperationException($"Saga instance {instanceId} not found");

        var template = _world.Gameplay.SagaArcs?.FirstOrDefault(p => p.RefName == instance.SagaRef);
        if (template == null)
            throw new InvalidOperationException($"Saga template '{instance.SagaRef}' not found");

        // Replay transactions to derive current state
        var stateMachine = new SagaStateMachine(template, new List<SagaTrigger>(), _world);
        return stateMachine.Replay(instance.Transactions);
    }

    #endregion

    #region Character Queries (REMOVED - Use CQRS Queries Instead)

    // NOTE: Character query methods removed from WorldStateRepository
    // Use official CQRS queries instead:
    //   - GetAvailableInteractionsQuery for nearby characters
    //   - GetCharacterByIdQuery for specific character
    //   - GetSpawnedCharactersQuery for all spawned characters

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

    #region Player Discovery Tracking

    /// <summary>
    /// Records a player discovery (lore, achievement, Saga, etc.).
    /// </summary>
    public async Task<PlayerDiscovery> RecordDiscoveryAsync(string avatarId, string entityType, string entityRef, Dictionary<string, string>? metadata = null)
    {
        var existing = await _discoveryRepository.FindOneAsync<PlayerDiscovery>(avatarId, entityType, entityRef);

        if (existing != null)
            return existing;

        var discovery = new PlayerDiscovery
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
    /// Records a trigger event for a player discovery.
    /// </summary>
    public async Task RecordTriggerAsync(string avatarId, string entityType, string entityRef)
    {
        var discovery = await _discoveryRepository.FindOneAsync<PlayerDiscovery>(avatarId, entityType, entityRef);

        if (discovery == null)
        {
            discovery = await RecordDiscoveryAsync(avatarId, entityType, entityRef);
        }

        discovery.RecordTrigger();
        await _discoveryRepository.UpdateAsync(discovery);
    }

    /// <summary>
    /// Gets the last trigger time for a specific player/entity combination.
    /// </summary>
    public async Task<DateTime?> GetLastTriggerTimeAsync(string avatarId, string entityType, string entityRef)
    {
        var discovery = await _discoveryRepository.FindOneAsync<PlayerDiscovery>(avatarId, entityType, entityRef);
        return discovery?.LastTriggeredAt;
    }

    /// <summary>
    /// Checks if a player has discovered a specific entity.
    /// </summary>
    public async Task<bool> HasDiscoveredAsync(string avatarId, string entityType, string entityRef)
    {
        return await _discoveryRepository.ExistsAsync(avatarId, entityType, entityRef);
    }

    /// <summary>
    /// Gets all discoveries for a specific player.
    /// </summary>
    public async Task<List<PlayerDiscovery>> GetPlayerDiscoveriesAsync(string avatarId)
    {
        return await _discoveryRepository.GetByAvatarIdAsync<PlayerDiscovery>(avatarId);
    }

    #endregion
}
