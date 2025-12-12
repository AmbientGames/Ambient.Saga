using Ambient.Domain;
using Ambient.Domain.DefinitionExtensions;
using Ambient.Infrastructure.Utilities;

namespace Ambient.Saga.Engine.Infrastructure.Loading;

/// <summary>
/// Loads world configurations from XML files.
/// </summary>
public class WorldConfigurationLoader : IWorldConfigurationLoader
{
    public async Task<WorldConfiguration[]> LoadAvailableWorldConfigurationsAsync(string dataDirectory, string definitionDirectory)
    {
        var xsdFilePath = Path.Combine(definitionDirectory, "WorldConfiguration.xsd");
        var configs = (await XmlLoader.LoadFromXmlAsync<WorldConfigurations>(Path.Combine(dataDirectory, "WorldConfigurations.xml"), xsdFilePath)).WorldConfiguration;

        foreach (var config in configs)
        {
            LoadWorldConfigurationSettings(config);
        }

        return configs;
    }

    /// <summary>
    /// Loads HeightMapSettings or ProceduralSettings from the Item union type.
    /// </summary>
    private static void LoadWorldConfigurationSettings(WorldConfiguration config)
    {
        if (config.HeightMapSettings != null || config.ProceduralSettings != null)
            return;

        switch (config.Item)
        {
            case HeightMapSettings mapSettings:
                config.HeightMapSettings = mapSettings;
                break;
            case ProceduralSettings proceduralSettings:
                config.ProceduralSettings = proceduralSettings;
                break;
        }
    }
}
