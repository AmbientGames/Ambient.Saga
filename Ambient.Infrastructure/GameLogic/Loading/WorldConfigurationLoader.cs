using Ambient.Application.Contracts;
using Ambient.Application.Utilities;
using Ambient.Domain;
using Ambient.Domain.Contracts;
using Ambient.Infrastructure.Utilities;
using Microsoft.Extensions.Logging;

namespace Ambient.Infrastructure.GameLogic.Loading;

/// <summary>
/// Loads world configurations from XML files.
/// </summary>
public class WorldConfigurationLoader : IWorldConfigurationLoader
{
    private readonly IGameSettings _gameSettings;
    private readonly ILogger<WorldConfigurationLoader>? _logger;
    private const string DefaultNamespace = "ambient_games";

    public WorldConfigurationLoader(IGameSettings gameSettings, ILogger<WorldConfigurationLoader>? logger = null)
    {
        _gameSettings = gameSettings ?? throw new ArgumentNullException(nameof(gameSettings));
        _logger = logger;
    }

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

                    // Set the source directory so content generator knows where to find/output files
                    config.SourceDirectory = Path.GetDirectoryName(configPath);

                    configs.Add(config);
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Failed to load WorldConfiguration from {ConfigPath}", configPath);
                }
            }
        }

        return configs.OrderBy(c => c.DisplayOrder).ThenBy(c => c.DisplayName).ToArray();
    }

    private IEnumerable<string> DiscoverWorldDirectories()
    {
        var worldDirs = new HashSet<string>();

        // BREAKPOINT: Check %APPDATA% location
        var appDataWorldsPath = Path.Combine(_gameSettings.GetAppDataContentPath(), "worlds");
        System.Diagnostics.Debug.WriteLine($"[WorldConfigurationLoader] AppData worlds path: {appDataWorldsPath}");
        System.Diagnostics.Debug.WriteLine($"[WorldConfigurationLoader] AppData exists: {Directory.Exists(appDataWorldsPath)}");
        _logger?.LogInformation("Checking AppData worlds path: {Path}", appDataWorldsPath);
        if (Directory.Exists(appDataWorldsPath))
        {
            var appDataDirs = Directory.GetDirectories(appDataWorldsPath);
            System.Diagnostics.Debug.WriteLine($"[WorldConfigurationLoader] Found {appDataDirs.Length} dirs in AppData");
            foreach (var dir in appDataDirs)
            {
                var configPath = Path.Combine(dir, "assets", "ambient_games", "xml", "WorldConfiguration.xml");
                System.Diagnostics.Debug.WriteLine($"[WorldConfigurationLoader]   {Path.GetFileName(dir)} - WorldConfig exists: {File.Exists(configPath)}");
                worldDirs.Add(dir);
            }
        }

        // Check install location
        var installWorldsPath = Path.Combine(FileManager.GetExecutingDirectoryName(), "content", "worlds");
        System.Diagnostics.Debug.WriteLine($"[WorldConfigurationLoader] Install worlds path: {installWorldsPath}");
        System.Diagnostics.Debug.WriteLine($"[WorldConfigurationLoader] Install exists: {Directory.Exists(installWorldsPath)}");
        if (Directory.Exists(installWorldsPath))
        {
            var installDirs = Directory.GetDirectories(installWorldsPath);
            System.Diagnostics.Debug.WriteLine($"[WorldConfigurationLoader] Found {installDirs.Length} dirs in Install");
            foreach (var dir in installDirs)
            {
                // Only add if not already found in appdata (appdata takes priority)
                var worldName = Path.GetFileName(dir);
                if (!worldDirs.Any(d => Path.GetFileName(d) == worldName))
                {
                    worldDirs.Add(dir);
                    System.Diagnostics.Debug.WriteLine($"[WorldConfigurationLoader]   Added from install: {worldName}");
                }
            }
        }

        System.Diagnostics.Debug.WriteLine($"[WorldConfigurationLoader] Total world dirs: {worldDirs.Count}");
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
