namespace Ambient.Domain.Enums;

/// <summary>
/// Contains constant values that define the fundamental structure and dimensions of world chunks.
/// These constants determine how the voxel world is organized spatially and how chunks interact.
/// </summary>
/// <remarks>
/// Chunks are the basic organizational unit of the voxel world. They represent cubic regions of space
/// that can be loaded, unloaded, and processed independently. The chunk system enables efficient
/// world streaming and supports large world sizes by dividing the world into manageable pieces.
/// 
/// All measurements are in voxel units unless otherwise specified. The chunk dimensions are powers
/// of 2 to enable efficient bit manipulation and memory alignment optimizations.
/// </remarks>
public class ChunkConstants
{
    /// <summary>
    /// Maximum render distance in chunks from the player. Defines the furthest chunks that can be visible.
    /// </summary>
    public const int MaximumSceneRadiusInChunks = 8192;
    
    /// <summary>
    /// The level of detail (LOD) at which blocks begin to be simplified for distant rendering.
    /// </summary>
    public const int FirstBlockLod = 2;
    
    /// <summary>
    /// Maximum valid coordinate within a chunk (ChunkWidth - 1). Used for bounds checking.
    /// </summary>
    public const int ChunkWidthMax = ChunkWidth - 1;

    /// <summary>
    /// Maximum number of chunk sections that can contain interactive elements.
    /// </summary>
    public const int MaxInteractableSections = 8;
    
    /// <summary>
    /// Minimum number of chunk sections required for basic chunk functionality.
    /// </summary>
    public const int BaseRequiredSections = 1;
    /// <summary>
    /// Width of chunk planes including boundary padding. Adds 2 extra units for neighbor overlap.
    /// </summary>
    public const int ChunkPlaneWidth = ChunkWidth + 2;
    
    /// <summary>
    /// Total area of a chunk plane including boundary padding (ChunkPlaneWidth²).
    /// </summary>
    public const int ChunkPlaneInclusive = ChunkPlaneWidth * ChunkPlaneWidth;

    /// <summary>
    /// Width and depth of a chunk in voxels. Standard chunk size is 16x16 voxels.
    /// This is a power of 2 to enable efficient bit shifting operations.
    /// </summary>
    public const int ChunkWidth = 16;

    public const int ChunkWidthMask = 15;

    public const int ChunkDiagonal = 23;

    public const int HubWidthDepth = ChunkConstants.ChunkWidth * 3;

    /// <summary>
    /// Number of bits to shift for chunk width calculations (log₂(ChunkWidth)).
    /// Used for fast multiplication/division operations.
    /// </summary>
    public const int ChunkWidthShift = 4;

    /// <summary>
    /// Quarter of chunk width, useful for sub-chunk calculations and optimization.
    /// </summary>
    public const int ChunkWidthFourth = ChunkWidth / 4;

    /// <summary>
    /// Legacy chunk height value maintained for backward compatibility.
    /// TODO: This should be phased out in favor of dynamic height systems.
    /// </summary>
    public const int ChunkHeightOldSchool = 256;
    //public const int WorldChunkHeight = 512; // *** testing

    public const int PriorityRegionChunkCount = 4;
    public const int CoreRegionChunkCount = 128;

    // todo: figure out why everything is +1 (space right after in each direction it seems)
    public const int ChunkSectionHeight = ChunkWidth;
    public const int ChunkSectionStorageVolume = (ChunkWidth + 2) * (ChunkWidth + 2) * (ChunkSectionHeight + 1);
    public const int SectionVoxelIndexX00 = (ChunkWidth + 2) * (ChunkSectionHeight + 1);
    public const int SectionVoxelIndex0Z0 = ChunkSectionHeight + 1;
    public const int SectionShift = 4;  // Number of bits to shift (log2(16))
    public const int SectionMask = 0xF; // Mask for extracting the lower 4 bits (binary: 00001111)


    //private const int RenderSetIndex = 1048576;

    // private const long VoxelIndexX00Database = 68719476736; //((long)2 << (int)36);
    // private const long VoxelIndex0Z0Database = ByteCapacity; //((long)2 << (int)8);
    //private const ulong VoxelIndexMultiplier22Bits = 4194304ul;

    //private const ulong VoxelIndexMask22Bits = VoxelIndexMultiplier22Bits - 1;

    //private const ulong VoxelIndexMultiplier20Bits = 1048576ul;

    //private const ulong VoxelIndexMask20Bits = VoxelIndexMultiplier20Bits - 1;

    //private const ulong VoxelIndexX00Database = VoxelIndexMultiplier22Bits * VoxelIndexMultiplier20Bits;

    //private const ulong VoxelIndex0Z0Database = VoxelIndexMultiplier20Bits;

    // private const ulong VoxelIndex00Z0Database = 16ul; //((long)2 << (int)8);

    public const int StackChunkWidthMax = StackChunkWidth - 1;

    //private const int StackChunkHeightMax = StackChunkHeight - 1;

    public const int StackChunkWidth = StackWidth * ChunkWidth;
    public const int StackChunkWidthShift = 8;

    public const int RegionWidthThisIsWrong = ChunkWidth * 8; // !!!!

    // todo: temporary - is this even right - what the heck is this!!!


    public const int StackWidth = 16;
    public const int StackWidthMax = StackWidth - 1;
    public const int StackWidthShift = 2;
    public const int StackLodShift= ChunkWidthShift + StackWidthShift;

    public const int StackVolumeNew = StackWidth * StackWidth;

    public const int ChunkLodWidth = 4;
    public const int ChunkLodWidthMax = ChunkLodWidth - 1;
    public const int ChunkLodVolume = ChunkLodWidth * ChunkLodWidth;
    public const int ChunkLodVolumeMax = ChunkLodVolume - 1;

    public const int Lod1ChunkWidth = ChunkLodWidth * ChunkWidth;
    public const int Lod1ChunkWidthMax = Lod1ChunkWidth - 1;

    private const int VoxelShapePointOffsetBase = 2097152 * ChunkWidth; // weird that I have this and the real deal below - this one is actually used

    // (2 ^ 21); // 67108864; // (2 ^ 26) //1073741824; //(2 ^ 30)
    public const int VoxelShapePointOffset = VoxelShapePointOffsetBase + 1024; // was + StackChunkWidth, but that isn't divisable by 1024, which is required for spawn stuff - NO idea if this will work or not

    public const string UnknownOwnerId = "b279df14-2abd-4be7-9a70-ad41a558f111";

    public const int MaxChunkHeight = 4096;
    public const int MaxChunkHeightMask = 4095;
}