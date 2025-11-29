using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ambient.Saga.WorldForge.Models;

/// <summary>
/// Metadata stored in ExtensionData for items (equipment, spells, consumables, tools)
///
/// CORE MATCHING: suitableFor - list of character types that can use this item
/// OPTIONAL AI HINTS: category, rarity, powerLevel, tags - for AI generation and organization
///
/// Users define their own strings - no enforcement (e.g., "Warrior", "Netrunner", "Jock", "CustomClass123")
/// </summary>
public class ItemMetadata
{
    /// <summary>
    /// [REQUIRED] Which character types can use this item
    /// Examples: ["Warrior", "Mage"], ["Netrunner", "Fixer"], ["Jock", "Athlete"]
    /// Matching: items.Where(i => i.Metadata.SuitableFor.Contains(characterType))
    /// </summary>
    [JsonPropertyName("suitableFor")]
    public List<string> SuitableFor { get; set; } = new();

    /// <summary>
    /// [OPTIONAL - AI Hint] Item category for organization
    /// Examples: "Offensive", "Defensive", "Healing", "Utility"
    /// </summary>
    [JsonPropertyName("category")]
    public string? Category { get; set; }

    /// <summary>
    /// [OPTIONAL - AI Hint] Rarity level for value/power indication
    /// Examples: "Common", "Uncommon", "Rare", "Epic", "Legendary"
    /// </summary>
    [JsonPropertyName("rarity")]
    public string? Rarity { get; set; }

    /// <summary>
    /// [OPTIONAL - AI Hint] Power level for progression indication
    /// Examples: "Starter", "Basic", "Advanced", "Epic"
    /// </summary>
    [JsonPropertyName("powerLevel")]
    public string? PowerLevel { get; set; }

    /// <summary>
    /// [OPTIONAL - AI Hint] Descriptive tags for flexible categorization
    /// Examples: ["fire", "magic"], ["stealth", "ranged"], ["heavy", "armor"]
    /// NOTE: Affinity is in XSD schema as AffinityRef - no duplication needed!
    /// </summary>
    [JsonPropertyName("tags")]
    public List<string> Tags { get; set; } = new();

    /// <summary>
    /// AI generation hints (not used at runtime, for content generation only)
    /// </summary>
    [JsonPropertyName("aiGeneration")]
    public AIGenerationHints? AIGeneration { get; set; }

    /// <summary>
    /// Serialize to JSON string for ExtensionData attribute
    /// </summary>
    public string ToJson()
    {
        return JsonSerializer.Serialize(this, new JsonSerializerOptions
        {
            WriteIndented = false,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        });
    }

    /// <summary>
    /// Deserialize from JSON string (from ExtensionData attribute)
    /// </summary>
    public static ItemMetadata? FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            return JsonSerializer.Deserialize<ItemMetadata>(json);
        }
        catch
        {
            return null;
        }
    }
}

/// <summary>
/// AI generation hints for content creation
/// </summary>
public class AIGenerationHints
{
    [JsonPropertyName("theme")]
    public string? Theme { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }
}
