using Ambient.Application.Utilities;
using Ambient.Domain;
using Ambient.Domain.Contracts;
using Ambient.Infrastructure.Utilities;

namespace Ambient.Infrastructure.GameLogic.Loading;

/// <summary>
/// Loads world configurations from XML files.
/// </summary>
public class WorldConfigurationLoader : IWorldConfigurationLoader
{
    private const string DefaultNamespace = "ambient_games";

    public async Task<IWorldConfiguration[]> LoadAvailableWorldConfigurationsAsync(string dataDirectory, string? definitionDirectory)
    {
        // Try to use schema validation if definition directory exists and contains the schema
        var xsdFilePath = !string.IsNullOrEmpty(definitionDirectory)
            ? Path.Combine(definitionDirectory, "WorldDefinition.xsd")
            : null;
        var useValidation = xsdFilePath != null && File.Exists(xsdFilePath);

        var configs = new List<WorldConfiguration>();

        // Discover world directories from both appdata and install locations
        var worldDirs = DiscoverWorldDirectories();

        foreach (var worldDir in worldDirs)
        {
            var configPath = Path.Combine(worldDir, "assets", DefaultNamespace, "xml", "WorldConfiguration.xml");
            if (File.Exists(configPath))
            {
                try
                {
                    WorldConfiguration config;
                    if (useValidation)
                    {
                        config = await XmlLoader.LoadFromXmlAsync<WorldConfiguration>(configPath, xsdFilePath!);
                    }
                    else
                    {
                        config = await XmlLoader.LoadFromXmlAsync<WorldConfiguration>(configPath);
                    }
                    LoadWorldConfigurationSettings(config);
                    configs.Add(config);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to load WorldConfiguration from {configPath}: {ex.Message}");
                }
            }
        }

        return configs.OrderBy(c => c.DisplayOrder).ThenBy(c => c.DisplayName).ToArray();
    }

    private static IEnumerable<string> DiscoverWorldDirectories()
    {
        var worldDirs = new HashSet<string>();

        // Check %APPDATA% location
        var appDataWorldsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AmbientGames", "Schema", "content", "worlds");
        if (Directory.Exists(appDataWorldsPath))
        {
            foreach (var dir in Directory.GetDirectories(appDataWorldsPath))
            {
                worldDirs.Add(dir);
            }
        }

        // Check install location
        var installWorldsPath = Path.Combine(FileManager.GetExecutingDirectoryName(), "content", "worlds");
        if (Directory.Exists(installWorldsPath))
        {
            foreach (var dir in Directory.GetDirectories(installWorldsPath))
            {
                // Only add if not already found in appdata (appdata takes priority)
                var worldName = Path.GetFileName(dir);
                if (!worldDirs.Any(d => Path.GetFileName(d) == worldName))
                {
                    worldDirs.Add(dir);
                }
            }
        }

        return worldDirs;
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
