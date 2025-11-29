using Ambient.Saga.WorldForge;
using System.Xml.Serialization;

namespace Ambient.Saga.WorldForge;

/// <summary>
/// Loads GenerationConfiguration files for story generation.
/// These configs are separate from WorldConfiguration and live in the StoryGenerator project.
///
/// NOTE: GenerationConfiguration and SourceLocation classes are generated from
/// DefinitionXsd/GenerationConfiguration.xsd via BuildDefinitions.ps1
/// </summary>
public class GenerationConfigurationLoader
{
    private readonly string _configsDirectory;

    public GenerationConfigurationLoader(string configsDirectory)
    {
        _configsDirectory = configsDirectory ?? throw new ArgumentNullException(nameof(configsDirectory));

        if (!Directory.Exists(_configsDirectory))
        {
            throw new DirectoryNotFoundException($"Generation configs directory not found: {_configsDirectory}");
        }
    }

    /// <summary>
    /// Check if a generation config exists for the given world RefName
    /// </summary>
    public bool HasGenerationConfig(string worldRef)
    {
        var configPath = GetConfigPath(worldRef);
        return File.Exists(configPath);
    }

    /// <summary>
    /// Load generation config for the given world RefName
    /// </summary>
    public GenerationConfiguration LoadConfig(string worldRef)
    {
        var configPath = GetConfigPath(worldRef);

        if (!File.Exists(configPath))
        {
            throw new FileNotFoundException($"Generation config not found for world '{worldRef}' at: {configPath}");
        }

        try
        {
            var serializer = new XmlSerializer(typeof(GenerationConfiguration), "Ambient.Saga.WorldForge");
            using var stream = File.OpenRead(configPath);
            var config = (GenerationConfiguration?)serializer.Deserialize(stream);

            if (config == null)
            {
                throw new InvalidOperationException($"Failed to deserialize generation config: {configPath}");
            }

            return config;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Error loading generation config from {configPath}: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Get all available generation configs
    /// </summary>
    public List<GenerationConfiguration> GetAllConfigs()
    {
        var configs = new List<GenerationConfiguration>();
        var files = Directory.GetFiles(_configsDirectory, "*Generation.xml");

        foreach (var file in files)
        {
            try
            {
                var serializer = new XmlSerializer(typeof(GenerationConfiguration), "Ambient.Saga.WorldForge");
                using var stream = File.OpenRead(file);
                var config = (GenerationConfiguration?)serializer.Deserialize(stream);
                if (config != null)
                {
                    configs.Add(config);
                }
            }
            catch
            {
                // Skip invalid configs
                continue;
            }
        }

        return configs;
    }

    private string GetConfigPath(string worldRef)
    {
        return Path.Combine(_configsDirectory, $"{worldRef}Generation.xml");
    }
}
