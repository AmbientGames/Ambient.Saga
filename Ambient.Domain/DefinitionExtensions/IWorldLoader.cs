using Ambient.Domain.Contracts;

namespace Ambient.Domain.DefinitionExtensions;

/// <summary>
/// Interface for loading world assets including configuration, metadata, and gameplay data.
/// Implementations handle the full world loading pipeline.
/// </summary>
public interface IWorldLoader
{
    /// <summary>
    /// Loads a world by finding the WorldConfiguration with the specified RefName.
    /// </summary>
    /// <param name="dataDirectory">Base data directory</param>
    /// <param name="definitionDirectory">Definition directory containing XSD schemas</param>
    /// <param name="configurationRefName">The RefName of the WorldConfiguration to load</param>
    /// <returns>A fully loaded World instance</returns>
    Task<IWorld> LoadWorldByConfigurationAsync(string dataDirectory, string definitionDirectory, string configurationRefName);

    /// <summary>
    /// Loads a world using a specific WorldConfiguration.
    /// </summary>
    /// <param name="dataDirectory">Base data directory</param>
    /// <param name="definitionDirectory">Definition directory containing XSD schemas</param>
    /// <param name="worldConfiguration">The WorldConfiguration to use</param>
    /// <returns>A fully loaded World instance</returns>
    Task<IWorld> LoadWorldAsync(string dataDirectory, string definitionDirectory, IWorldConfiguration worldConfiguration);
}
