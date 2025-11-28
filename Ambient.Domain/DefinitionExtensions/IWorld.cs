using Ambient.Domain.Contracts;

namespace Ambient.Domain.DefinitionExtensions;

/// <summary>
/// Defines the contract for world properties.
/// </summary>
public interface IWorld
{
    WorldConfiguration WorldConfiguration { get; set; }

    /// <summary>
    /// Optional block provider for games that include block/voxel systems.
    /// Returns null by default - implemented by game-specific domain projects.
    /// </summary>
    IBlockProvider? BlockProvider => null;
}