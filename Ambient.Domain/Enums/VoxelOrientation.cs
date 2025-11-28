namespace Ambient.Domain.Enums;

/// <summary>
/// Defines the spatial orientations and directions for voxels in the 3D world coordinate system.
/// This enum provides directional constants for voxel placement, face identification, and spatial
/// calculations using a right-handed coordinate system where Y is up, X is east-west, and Z is north-south.
/// </summary>
/// <remarks>
/// VoxelOrientation is fundamental to the 3D voxel engine for determining block faces, adjacency,
/// and directional operations. The coordinate system follows standard conventions:
/// - Y axis: Up (+Y) / Down (-Y) 
/// - X axis: East (+X) / West (-X)
/// - Z axis: North (+Z) / South (-Z)
/// 
/// The enum includes both cardinal directions (N, S, E, W, Up, Down) and diagonal combinations
/// for more precise spatial calculations. Composite values like All21 and All22 represent
/// bitmasks for efficient bulk operations on multiple orientations simultaneously.
/// </remarks>
public enum VoxelOrientation
{
    /// <summary>
    /// Represents the downward direction along the negative Y axis.
    /// Used for bottom faces of voxels and downward-oriented operations.
    /// </summary>
    DownNegativeY = 3,

    /// <summary>
    /// Represents the upward direction along the positive Y axis.
    /// Used for top faces of voxels and upward-oriented operations.
    /// </summary>
    UpPositiveY = 2,

    /// <summary>
    /// Represents the eastward direction along the positive X axis.
    /// Used for eastern faces of voxels and eastward-oriented operations.
    /// </summary>
    EastPositiveX = 4,

    /// <summary>
    /// Represents the westward direction along the negative X axis.
    /// Used for western faces of voxels and westward-oriented operations.
    /// </summary>
    WestNegativeX = 5,

    /// <summary>
    /// Represents the northward direction along the positive Z axis.
    /// Used for northern faces of voxels and northward-oriented operations.
    /// </summary>
    NorthPositiveZ = 1,

    /// <summary>
    /// Represents the southward direction along the negative Z axis.
    /// Used for southern faces of voxels and southward-oriented operations.
    /// </summary>
    SouthNegativeZ = 0,

    /// <summary>
    /// Represents the northeast diagonal direction (positive Z and positive X).
    /// Used for diagonal movement and corner-oriented spatial calculations.
    /// </summary>
    NorthEastPositiveZPositiveX = 6,

    /// <summary>
    /// Represents the northwest diagonal direction (positive Z and negative X).
    /// Used for diagonal movement and corner-oriented spatial calculations.
    /// </summary>
    NorthWestPositiveZNegativeX = 7,

    /// <summary>
    /// Represents the southeast diagonal direction (negative Z and positive X).
    /// Used for diagonal movement and corner-oriented spatial calculations.
    /// </summary>
    SouthEastNegativeZPositiveX = 8,

    /// <summary>
    /// Represents the southwest diagonal direction (negative Z and negative X).
    /// Used for diagonal movement and corner-oriented spatial calculations.
    /// </summary>
    SouthWestNegativeZNegativeX = 9,

    /// <summary>
    /// Represents the maximum single orientation value (9).
    /// Used for bounds checking and validation of orientation values.
    /// </summary>
    Max = 9,

    /// <summary>
    /// Bitmask representing a specific combination of orientations (value: 63).
    /// Used for efficient bulk operations on multiple orientations simultaneously.
    /// </summary>
    All21 = 63,

    /// <summary>
    /// Bitmask representing a larger combination of orientations (value: 1023).
    /// Used for comprehensive spatial operations involving multiple directional checks.
    /// </summary>
    All22 = 1023,

    /// <summary>
    /// Bitmask representing all diagonal orientations (value: 960).
    /// Used specifically for operations that need to process only diagonal directions
    /// (northeast, northwest, southeast, southwest) while excluding cardinal directions.
    /// </summary>
    AllDiaganols = 960
}