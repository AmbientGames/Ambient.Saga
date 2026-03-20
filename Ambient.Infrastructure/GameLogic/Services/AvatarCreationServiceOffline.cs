using Ambient.Domain;
using Ambient.Domain.Contracts;
using Ambient.Domain.Entities;
using Ambient.Domain.GameLogic.Gameplay.Avatar;
using Ambient.Domain.GameLogic.Gameplay.WorldManagers;
using SharpDX;

namespace Ambient.Infrastructure.GameLogic.Services;

/// <summary>
/// Creates avatars locally for offline/Schema games.
/// Generates a local GUID and initializes from archetype + world spawn position.
/// </summary>
public class AvatarCreationServiceOffline : IAvatarCreationService
{
    public Task<AvatarEntity> CreateAvatarAsync(Guid avatarId, AvatarArchetype archetype, IWorld world)
    {
        var avatar = new AvatarEntity
        {
            Id = avatarId,
            AvatarId = avatarId,
            ArchetypeRef = archetype.RefName,
            PlayTimeHours = 0,
            BlocksPlaced = 0,
            BlocksDestroyed = 0,
            DistanceTraveled = 0,
            X = 0,
            Y = 100,
            Z = 0
        };

        AvatarSpawner.SpawnFromModelAvatar(avatar, archetype);
        SetAvatarDefaults(world, avatar);

        return Task.FromResult(avatar);
    }

    private static void SetAvatarDefaults(IWorld world, AvatarEntity avatar)
    {
        if (world.IsProcedural)
        {
            var modelZ = CoordinateConverter.LatitudeToModelZ(world.WorldConfiguration.SpawnLatitude, world);
            avatar.HomeLocation = new Vector3(0, 0, (float)modelZ);
        }
        else
        {
            var modelX = CoordinateConverter.LongitudeToModelX(world.WorldConfiguration.SpawnLongitude, world);
            var modelZ = CoordinateConverter.LatitudeToModelZ(world.WorldConfiguration.SpawnLatitude, world);
            avatar.HomeLocation = new Vector3((float)modelX, 0, (float)modelZ);
        }

        avatar.Position = avatar.HomeLocation;
        avatar.IsInvulnerable = true;
    }
}
