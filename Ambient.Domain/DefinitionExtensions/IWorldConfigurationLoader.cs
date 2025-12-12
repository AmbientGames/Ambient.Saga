namespace Ambient.Domain.DefinitionExtensions;

/// <summary>
/// Interface for loading world configurations from data files.
/// Implementations handle XML deserialization and validation.
/// </summary>
public interface IWorldConfigurationLoader
{
    /// <summary>
    /// Loads all available world configurations.
    /// </summary>
    /// <param name="dataDirectory">Base data directory containing WorldConfigurations.xml</param>
    /// <param name="definitionDirectory">Definition directory containing XSD schemas</param>
    /// <returns>Array of loaded WorldConfiguration objects</returns>
    Task<WorldConfiguration[]> LoadAvailableWorldConfigurationsAsync(string dataDirectory, string definitionDirectory);
}
