using Ambient.Domain.Enums;

namespace Ambient.Domain.Entities;

// NOTE: it seems like some of these are server specific, so we shouldn't touch them

/// <summary>
/// Represents an avatar entity, combining generated schema properties with base entity behavior.
/// </summary>
public class AvatarEntity : AvatarBase, IBaseEntity
{
    /// <summary>
    /// The unique identifier for the avatar entity.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// The avatar's role within the world.
    /// </summary>
    public AvatarRoles Roles { get; set; } 

    /// <summary>
    /// The sequence number for the avatar's updates.
    /// </summary>
    public int SequenceNumber { get; set; }

    /// <summary>
    /// Statistics for blocks collected by the avatar.
    /// </summary>
    public int[] BlockCollectedStatistics = new int[WorldMaximums.MaxBlocks];

    /// <summary>
    /// The timestamp of the avatar's last activity.
    /// </summary>
    public DateTime LastActivity = DateTime.UtcNow;
}