using Ambient.Domain.Entities;

namespace Ambient.Domain.Contracts;

/// <summary>
/// Creates a new avatar from an archetype for a given world.
/// Offline: creates locally with a new GUID.
/// Online: calls the server, receives avatar with server-assigned GUID.
/// </summary>
public interface IAvatarCreationService
{
    Task<AvatarEntity> CreateAvatarAsync(Guid avatarId, AvatarArchetype archetype, IWorld world);

    /// <summary>
    /// Checks if this player already has an avatar for the given world (e.g. played on another device).
    /// Returns the seeded avatar if found, null otherwise. Offline always returns null.
    /// </summary>
    Task<AvatarEntity?> FindExistingAvatarAsync(IWorld world) => Task.FromResult<AvatarEntity?>(null);
}
