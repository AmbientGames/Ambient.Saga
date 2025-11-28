namespace Ambient.Domain.Enums;

/// <summary>
/// Defines the modes for generating living elements on blocks.
/// </summary>
public enum BlockLivingGenerationMode
{
    /// <summary>
    /// No living elements are generated.
    /// </summary>
    None,
    
    /// <summary>
    /// Generate leaves.
    /// </summary>
    Leaves,
    
    /// <summary>
    /// Generate ground cover.
    /// </summary>
    GroundCover,
    
    /// <summary>
    /// Generate shrubs.
    /// </summary>
    Shrub,
    
    /// <summary>
    /// Generate trees.
    /// </summary>
    Tree
}