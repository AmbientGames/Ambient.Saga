using Ambient.Application.Contracts;
using Ambient.Domain.Partials;
using Ambient.Domain.Entities;
using Ambient.Saga.Engine.Domain.Rpg.Sagas.TransactionLog;

namespace Ambient.Saga.Engine.Contracts;

/// <summary>
/// Interface for world state repository operations.
/// This interface belongs in the Application layer as part of the repository pattern.
/// NOTE: achievement unlock state deliberately has NO members here — the avatar's
/// persisted Achievements list is the single unlock ledger (audit C2). The former
/// AchievementInstance LiteDB collection was a divergent parallel store whose
/// instance set froze at first creation.
/// </summary>
public interface IWorldStateRepository
{
    /// <summary>
    /// Gets a Saga instance by template RefName for a specific avatar.
    /// </summary>
    Task<SagaInstance?> GetSagaInstanceAsync(string avatarId, string templateRef);

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
    /// Records an avatar discovery (lore, achievement, Saga, etc.).
    /// </summary>
    Task<AvatarDiscovery> RecordDiscoveryAsync(string avatarId, string entityType, string entityRef, Dictionary<string, string>? metadata = null);

    /// <summary>
    /// Records a trigger event for an avatar discovery.
    /// </summary>
    Task RecordTriggerAsync(string avatarId, string entityType, string entityRef);

    /// <summary>
    /// Gets the last trigger time for a specific avatar/entity combination.
    /// </summary>
    Task<DateTime?> GetLastTriggerTimeAsync(string avatarId, string entityType, string entityRef);

    /// <summary>
    /// Checks if an avatar has discovered a specific entity.
    /// </summary>
    Task<bool> HasDiscoveredAsync(string avatarId, string entityType, string entityRef);

    /// <summary>
    /// Gets all discoveries for a specific avatar.
    /// </summary>
    Task<List<AvatarDiscovery>> GetAvatarDiscoveriesAsync(string avatarId);
}