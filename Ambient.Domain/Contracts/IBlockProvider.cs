namespace Ambient.Domain.Contracts;

/// <summary>
/// Provides block lookup functionality for systems that need to resolve block references.
/// Implemented by game-specific domain projects that define block types.
/// </summary>
public interface IBlockProvider
{
    /// <summary>
    /// Looks up a block by its reference name.
    /// </summary>
    /// <param name="blockRef">The reference name of the block to find.</param>
    /// <returns>The block as ITradeable if found, null otherwise.</returns>
    IBlock? GetBlockByRef(string blockRef);
}
