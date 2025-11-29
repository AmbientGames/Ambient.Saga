using Ambient.Application.Contracts;
using Ambient.Domain.DefinitionExtensions;
using Ambient.Domain.Entities;
using Ambient.Saga.Engine.Domain.Achievements;
using Ambient.Saga.Engine.Domain.Rpg.Sagas.TransactionLog;

namespace Ambient.Saga.Engine.Contracts;

/// <summary>
/// Interface for world state repository operations.
/// This interface belongs in the Application layer as part of the repository pattern.
/// </summary>
public interface IWorldStateRepository
{
    /// <summary>
    /// Gets a Saga instance by template RefName for a specific avatar.
    /// </summary>
    Task<SagaInstance?> GetSagaInstanceAsync(string avatarId, string templateRef);

    /// <summary>
    /// Gets or creates AchievementInstance objects for a specific avatar.
    /// </summary>
    Task<List<AchievementInstance>> GetOrCreateAchievementInstancesAsync(string avatarId);

    /// <summary>
    /// Saves AchievementInstance state.
    /// </summary>
    Task SaveAchievementAsync(AchievementInstance instance);

    /// <summary>
    /// Loads avatar from database, or returns null if not found.
    /// </summary>
    Task<AvatarEntity?> LoadAvatarAsync();

    /// <summary>
    /// Saves avatar to database (creates if new, updates if exists).
    /// </summary>
    Task SaveAvatarAsync(AvatarEntity avatarEntity);

    /// <summary>
    /// Deletes all avatars from database.
    /// </summary>
    Task DeleteAvatarsAsync();

    /// <summary>
    /// Records a player discovery (lore, achievement, Saga, etc.).
    /// </summary>
    Task<PlayerDiscovery> RecordDiscoveryAsync(string avatarId, string entityType, string entityRef, Dictionary<string, string>? metadata = null);

    /// <summary>
    /// Records a trigger event for a player discovery.
    /// </summary>
    Task RecordTriggerAsync(string avatarId, string entityType, string entityRef);

    /// <summary>
    /// Gets the last trigger time for a specific player/entity combination.
    /// </summary>
    Task<DateTime?> GetLastTriggerTimeAsync(string avatarId, string entityType, string entityRef);

    /// <summary>
    /// Checks if a player has discovered a specific entity.
    /// </summary>
    Task<bool> HasDiscoveredAsync(string avatarId, string entityType, string entityRef);

    /// <summary>
    /// Gets all discoveries for a specific player.
    /// </summary>
    Task<List<PlayerDiscovery>> GetPlayerDiscoveriesAsync(string avatarId);
}