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
}
