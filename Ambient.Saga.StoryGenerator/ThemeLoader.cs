using System.Xml;
using System.Xml.Serialization;
using Ambient.Domain;
using Ambient.Saga.StoryGenerator.Models;

namespace Ambient.Saga.StoryGenerator;

/// <summary>
/// Loads theme content from theme directories
/// </summary>
public class ThemeLoader
{
    private readonly string _themesBasePath;

    public ThemeLoader(string themesBasePath)
    {
        _themesBasePath = themesBasePath;
    }

    /// <summary>
    /// Load a theme by RefName
    /// </summary>
    public async Task<ThemeContent> LoadThemeAsync(string themeRefName)
    {
        var themePath = Path.Combine(_themesBasePath, themeRefName);

        if (!Directory.Exists(themePath))
        {
            throw new DirectoryNotFoundException($"Theme directory not found: {themePath}");
        }

        var themeContent = new ThemeContent();

        // Load Theme.xml manifest (metadata)
        var manifestPath = Path.Combine(themePath, "Theme.xml");
        if (File.Exists(manifestPath))
        {
            themeContent.Metadata = await LoadXmlAsync<ThemeDefinition>(manifestPath);
        }
        else
        {
            // Create default metadata if no manifest
            themeContent.Metadata = new ThemeDefinition
            {
                RefName = themeRefName,
                DisplayName = themeRefName
            };
        }

        // Load CharacterArchetypes.xml
        var archetypesPath = Path.Combine(themePath, "CharacterArchetypes.xml");
        if (File.Exists(archetypesPath))
        {
            var archetypes = await LoadXmlAsync<AvatarArchetypes>(archetypesPath);
            if (archetypes?.AvatarArchetype != null)
            {
                themeContent.CharacterArchetypes.AddRange(archetypes.AvatarArchetype);
            }
        }

        // Load Equipment.xml
        var equipmentPath = Path.Combine(themePath, "Equipment.xml");
        if (File.Exists(equipmentPath))
        {
            var equipmentCatalog = await LoadXmlAsync<EquipmentCatalog>(equipmentPath);
            if (equipmentCatalog?.Equipment != null)
            {
                themeContent.Equipment.AddRange(equipmentCatalog.Equipment);
            }
        }

        // Load Spells.xml
        var spellsPath = Path.Combine(themePath, "Spells.xml");
        if (File.Exists(spellsPath))
        {
            var spellCatalog = await LoadXmlAsync<SpellCatalog>(spellsPath);
            if (spellCatalog?.Spell != null)
            {
                themeContent.Spells.AddRange(spellCatalog.Spell);
            }
        }

        // Load Consumables.xml
        var consumablesPath = Path.Combine(themePath, "Consumables.xml");
        if (File.Exists(consumablesPath))
        {
            var consumableCatalog = await LoadXmlAsync<ConsumableCatalog>(consumablesPath);
            if (consumableCatalog?.Consumable != null)
            {
                themeContent.Consumables.AddRange(consumableCatalog.Consumable);
            }
        }

        // Load Tools.xml
        var toolsPath = Path.Combine(themePath, "Tools.xml");
        if (File.Exists(toolsPath))
        {
            var toolCatalog = await LoadXmlAsync<ToolCatalog>(toolsPath);
            if (toolCatalog?.Tool != null)
            {
                themeContent.Tools.AddRange(toolCatalog.Tool);
            }
        }

        // Load CharacterAffinities.xml
        var affinitiesPath = Path.Combine(themePath, "CharacterAffinities.xml");
        if (File.Exists(affinitiesPath))
        {
            var affinitiesCatalog = await LoadXmlAsync<CharacterAffinities>(affinitiesPath);
            if (affinitiesCatalog?.Affinity != null)
            {
                themeContent.Affinities.AddRange(affinitiesCatalog.Affinity);
            }
        }

        // TODO: Load Factions.xml once Faction.xsd is generated
        // var factionsPath = Path.Combine(themePath, "Factions.xml");

        if (!themeContent.HasContent)
        {
            throw new InvalidOperationException($"Theme '{themeRefName}' has no content files");
        }

        return themeContent;
    }

    /// <summary>
    /// Get all available theme RefNames
    /// </summary>
    public List<string> GetAvailableThemes()
    {
        if (!Directory.Exists(_themesBasePath))
        {
            return new List<string>();
        }

        return Directory.GetDirectories(_themesBasePath)
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrEmpty(name))
            .Cast<string>()
            .ToList();
    }

    /// <summary>
    /// Check if a theme exists
    /// </summary>
    public bool ThemeExists(string themeRefName)
    {
        var themePath = Path.Combine(_themesBasePath, themeRefName);
        return Directory.Exists(themePath);
    }

    /// <summary>
    /// Generic XML loader with error handling
    /// </summary>
    private async Task<T> LoadXmlAsync<T>(string path) where T : class
    {
        try
        {
            using var stream = File.OpenRead(path);
            var serializer = new XmlSerializer(typeof(T));
            var result = serializer.Deserialize(stream) as T;

            if (result == null)
            {
                throw new InvalidOperationException($"Failed to deserialize {path} as {typeof(T).Name}");
            }

            return result;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to load {path}: {ex.Message}", ex);
        }
    }
}
