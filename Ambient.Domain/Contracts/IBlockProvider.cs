namespace Ambient.Domain.Contracts;

/// <summary>
/// Provides block catalog and lookup functionality for systems that need to work with blocks.
/// Implemented by game-specific domain projects that define block types.
/// </summary>
public interface IBlockProvider
{
    /// <summary>
    /// Gets all available blocks in the catalog.
    /// Used by UI to display block lists for trading, inventory, etc.
    /// </summary>
    /// <returns>All blocks available in the game.</returns>
    IEnumerable<IBlock> GetAllBlocks();

    /// <summary>
    /// Looks up a block by its reference name.
    /// </summary>
    /// <param name="blockRef">The reference name of the block to find.</param>
    /// <returns>The block if found, null otherwise.</returns>
    IBlock? GetBlockByRef(string blockRef);

    /// <summary>
    /// Gets blocks filtered by substance type.
    /// </summary>
    /// <param name="substanceRef">The substance type to filter by (e.g., "Stone", "Wood").</param>
    /// <returns>Blocks matching the specified substance.</returns>
    IEnumerable<IBlock> GetBlocksBySubstance(string substanceRef);
}
