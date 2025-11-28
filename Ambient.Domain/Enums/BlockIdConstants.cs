namespace Ambient.Domain.Enums;

/// <summary>
/// Defines constant values for special block IDs.
/// </summary>
public class BlockIdConstants
{
    /// <summary>
    /// Block ID for air/empty space.
    /// </summary>
    public const ushort AirBlockId = 0;

    /// <summary>
    /// Block ID for snow.
    /// </summary>
    public const ushort SnowBlockId = 1;

    /// <summary>
    /// Reserved block ID for system use.
    /// </summary>
    public const ushort ReservedBlockId = 15;

    /// <summary>
    /// Block ID for water.
    /// </summary>
    public const ushort WaterBlockId = 16;

    /// <summary>
    /// Block ID for ice.
    /// </summary>
    public const ushort IceBlockId = 17;

    /// <summary>
    /// Block ID for glacial ice.
    /// </summary>
    public const ushort GlacialIceBlockId = 18;

    /// <summary>
    /// Block ID for lava.
    /// </summary>
    public const ushort LavaBlockId = 19;

    /// <summary>
    /// Block ID for the base layer of terrain.
    /// </summary>
    public const ushort BaseLayerBlockId = 20;

    /// <summary>
    /// Block ID for the crust layer of terrain.
    /// </summary>
    public const ushort CrustLayerBlockId = 21;

    /// <summary>
    /// Block ID for sand.
    /// </summary>
    public const ushort SandBlockId = 22;

    /// <summary>
    /// Block ID for aggregate material.
    /// </summary>
    public const ushort AggregateBlockId = 23;

    /// <summary>
    /// Block ID for coarse aggregate terraform crust alternate material.
    /// </summary>
    public const ushort CoarseAggregateTerraformCrustAlternateBlockId = 24;

    /// <summary>
    /// For filling empty caverns, primarily under seabeds, to prevent excessive water movement and geometry for areas not frequented
    /// </summary>
    public const ushort ShaleBlockId = 25;

    /// <summary>
    /// Block ID used for selection highlighting.
    /// </summary>
    public const ushort SelectBlockId = 29;

    /// <summary>
    /// Block ID used for general highlighting.
    /// </summary>
    public const ushort HighlightBlockId = 30;

    /// <summary>
    /// The first valid block ID for regular blocks.
    /// </summary>
    public const ushort FirstBlockId = 16;
}