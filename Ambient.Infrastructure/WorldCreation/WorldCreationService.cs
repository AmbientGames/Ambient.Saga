using Ambient.Application.WorldCreation;

namespace Ambient.Infrastructure.WorldCreation;

/// <summary>
/// Service that orchestrates world creation, generating all required files.
/// </summary>
public class WorldCreationService : IWorldCreationService
{
    private readonly WorldConfigurationBuilder _worldConfigBuilder;
    private readonly GenerationConfigurationBuilder _generationConfigBuilder;

    public WorldCreationService()
    {
        _worldConfigBuilder = new WorldConfigurationBuilder();
        _generationConfigBuilder = new GenerationConfigurationBuilder();
    }

    /// <inheritdoc/>
    public async Task<WorldCreationResult> CreateWorldAsync(
        WorldCreationParameters parameters,
        string worldsBasePath,
        Action<string>? progress = null)
    {
        try
        {
            // Calculate output paths
            var worldPath = Path.Combine(worldsBasePath, parameters.WorldRef);
            var xmlPath = Path.Combine(worldPath, "assets", "ambient_games", "xml");

            // Create directories
            progress?.Invoke("Creating directories...");
            Directory.CreateDirectory(worldPath);
            Directory.CreateDirectory(xmlPath);

            // Generate and write GenerationConfiguration.xml (only if it doesn't exist - AI service may have already created it)
            var generationConfigPath = Path.Combine(xmlPath, "GenerationConfiguration.xml");
            if (!File.Exists(generationConfigPath))
            {
                progress?.Invoke("Writing GenerationConfiguration.xml...");
                var generationXml = _generationConfigBuilder.Build(parameters);
                await File.WriteAllTextAsync(generationConfigPath, generationXml);
            }
            else
            {
                progress?.Invoke("Using existing GenerationConfiguration.xml (AI-generated)...");
            }

            // Generate and write WorldConfiguration.xml
            progress?.Invoke("Writing WorldConfiguration.xml...");
            var configXml = _worldConfigBuilder.Build(parameters);
            await File.WriteAllTextAsync(Path.Combine(xmlPath, "WorldConfiguration.xml"), configXml);

            // Copy terrain file if real world (into world folder)
            if (parameters.IsRealWorld && !string.IsNullOrEmpty(parameters.TerrainFilePath))
            {
                progress?.Invoke("Copying terrain file...");
                var geographicDataPath = Path.Combine(
                    worldPath,
                    "assets", "ambient_games", "geographic_data");

                Directory.CreateDirectory(geographicDataPath);

                var terrainFileName = $"{parameters.WorldRef}_terrain.tif";
                var terrainDest = Path.Combine(geographicDataPath, terrainFileName);

                File.Copy(parameters.TerrainFilePath, terrainDest, overwrite: true);
            }

            return WorldCreationResult.Succeeded(worldPath);
        }
        catch (Exception ex)
        {
            return WorldCreationResult.Failed(ex.Message);
        }
    }
}
