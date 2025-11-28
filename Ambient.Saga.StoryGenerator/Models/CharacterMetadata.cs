using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ambient.Saga.StoryGenerator.Models;

/// <summary>
/// Metadata stored in ExtensionData for characters (avatar archetypes, NPCs, bosses, merchants)
///
/// CORE MATCHING: characterType - what type of character this is
/// OPTIONAL AI HINTS: role, difficulty, tags - for AI generation and organization
///
/// Users define their own strings - no enforcement (e.g., "Warrior", "Netrunner", "Jock", "CustomClass123")
/// </summary>
public class CharacterMetadata
{
    /// <summary>
    /// [REQUIRED] What type of character this is
    /// Examples: "Warrior", "Mage", "Netrunner", "Fixer", "Jock", "Nerd"
    /// Matching: items.Where(i => i.Metadata.SuitableFor.Contains(character.Metadata.CharacterType))
    /// </summary>
    [JsonPropertyName("characterType")]
    public string CharacterType { get; set; } = "";

    /// <summary>
    /// [OPTIONAL - AI Hint] Character role for organization
    /// Examples: "DPS", "Tank", "Healer", "Support"
    /// </summary>
    [JsonPropertyName("role")]
    public string? Role { get; set; }

    /// <summary>
    /// [OPTIONAL - AI Hint] Difficulty level for NPCs/Bosses
    /// Examples: "Easy", "Normal", "Hard", "Epic"
    /// </summary>
    [JsonPropertyName("difficulty")]
    public string? Difficulty { get; set; }

    /// <summary>
    /// [OPTIONAL - AI Hint] Descriptive tags for flexible categorization
    /// Examples: ["aggressive", "ranged"], ["stealth", "melee"], ["support", "magic"]
    /// </summary>
    [JsonPropertyName("tags")]
    public List<string> Tags { get; set; } = new();

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
    public static CharacterMetadata? FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            return JsonSerializer.Deserialize<CharacterMetadata>(json);
        }
        catch
        {
            return null;
        }
    }
}
