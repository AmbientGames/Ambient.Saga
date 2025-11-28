namespace Ambient.Domain.Enums;

/// <summary>
/// Provides maximum limits and constraints for various world elements.
/// </summary>
public class WorldMaximums
{
    /// <summary>
    /// The maximum number of chunks that can be assigned to guest players.
    /// </summary>
    public const int MaximumGuestChunkCount = 64;

    /// <summary>
    /// Bit mask for extracting height values from packed data.
    /// </summary>
    public const byte HeightMask = 240;
    
    /// <summary>
    /// The number of bit positions to right-shift when extracting height values.
    /// </summary>
    public const byte HeightShift = 4;

    /// <summary>
    /// The maximum number of chunks that can be owned by the world owner.
    /// </summary>
    public const int MaximumOwnerChunkCount = 4096;

    /// <summary>
    /// The maximum number of achievements.
    /// </summary>
    public const int MaxAchievements = Constants.ByteCapacity;

    /// <summary>
    /// The maximum number of different tools.
    /// </summary>
    public const int MaxTools = 16;

    /// <summary>
    /// The maximum number of different block types.
    /// </summary>
    public const int MaxBlocks = 512;

    /// <summary>
    /// The maximum number of accessory items.
    /// </summary>
    public const int MaxAccessories = 16;

    /// <summary>
    /// The maximum number of consumable item types.
    /// </summary>
    public const int MaxConsumables = 16;
}