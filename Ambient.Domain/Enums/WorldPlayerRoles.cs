namespace Ambient.Domain.Enums;

/// <summary>
/// Defines roles for avatars within a world.
/// </summary>
[Flags]
public enum AvatarRoles
{
    /// <summary>
    /// A guest role.
    /// </summary>
    Guest = 1 << 0,

    /// <summary>
    /// An owner role.
    /// </summary>
    Owner = 1 << 1,

    /// <summary>
    /// A world sharer role.
    /// </summary>
    WorldSharer = 1 << 2,

    /// <summary>
    /// A moderator role.
    /// </summary>
    Moderator = 1 << 3,

    /// <summary>
    /// An admin role.
    /// </summary>
    Admin = 1 << 4
}