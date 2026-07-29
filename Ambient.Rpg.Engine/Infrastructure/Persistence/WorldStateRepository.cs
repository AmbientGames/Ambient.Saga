using Ambient.Application.Contracts;
using Ambient.Domain;
using Ambient.Domain.Contracts;
using Ambient.Domain.Partials;
using Ambient.Domain.Entities;
using Ambient.Rpg.Engine.Contracts;
using Ambient.Rpg.Engine.Contracts.Cqrs;
using Ambient.Rpg.Engine.Domain.Arcs.TransactionLog;

namespace Ambient.Rpg.Engine.Infrastructure.Persistence;

/// <summary>
/// Repository for managing persisted world state.
/// Uses the CQRS ArcInstanceRepository for all arc operations.
/// Provides simple CRUD for avatar state.
/// Achievement unlocks are NOT stored here: the avatar's persisted Achievements
/// list (saved via SaveAvatarAsync) is the single unlock ledger (audit C2).
/// </summary>
internal class WorldStateRepository : IWorldStateRepository
{
    private readonly IArcInstanceRepository _arcRepository;
    private readonly IGameAvatarRepository _avatarRepository;
    private readonly IAvatarDiscoveryRepository _discoveryRepository;

    public WorldStateRepository(
        IArcInstanceRepository arcRepository,
        IGameAvatarRepository avatarRepository,
        IAvatarDiscoveryRepository discoveryRepository)
    {
        _arcRepository = arcRepository ?? throw new ArgumentNullException(nameof(arcRepository));
        _avatarRepository = avatarRepository ?? throw new ArgumentNullException(nameof(avatarRepository));
        _discoveryRepository = discoveryRepository ?? throw new ArgumentNullException(nameof(discoveryRepository));
    }

    #region Arc Operations (Delegated to CQRS Repository)

    /// <summary>
    /// Gets an arc instance by template RefName for a specific avatar.
    /// </summary>
    public async Task<ArcInstance?> GetArcInstanceAsync(string avatarId, string templateRef)
    {
        var avatarGuid = Guid.Parse(avatarId);
        return await _arcRepository.GetOrCreateInstanceAsync(avatarGuid, templateRef);
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

    #region Avatar Discovery Tracking

    /// <summary>
    /// Records an avatar discovery (lore, achievement, Arc, etc.).
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
