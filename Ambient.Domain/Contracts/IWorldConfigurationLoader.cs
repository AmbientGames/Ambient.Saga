namespace Ambient.Domain.Contracts;

/// <summary>
/// Interface for loading world configurations from data files.
/// Implementations handle XML deserialization and optional validation.
/// </summary>
public interface IWorldConfigurationLoader
{
    /// <summary>
    /// Loads all available world configurations.
    /// </summary>
    /// <param name="dataDirectory">Base data directory containing WorldConfigurations.xml</param>
    /// <param name="definitionDirectory">Definition directory containing XSD schemas. If null or schemas not found, validation is skipped.</param>
    /// <returns>Array of loaded WorldConfiguration objects</returns>
    Task<IWorldConfiguration[]> LoadAvailableWorldConfigurationsAsync(string dataDirectory, string? definitionDirectory);

    /// <summary>
    /// Ensures world content is available locally. For online worlds, downloads and extracts
    /// content if needed and updates the configuration. For offline worlds, this is a no-op.
    /// </summary>
    Task<IWorldConfiguration> ResolveWorldContentAsync(string refName) => Task.FromResult<IWorldConfiguration>(null!);
}
