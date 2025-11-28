using Ambient.Domain;

namespace Ambient.Saga.StoryGenerator.Models;

/// <summary>
/// Loaded content from a theme directory
/// Contains all catalog data loaded from theme XML files
/// </summary>
public class ThemeContent
{
    public ThemeDefinition Metadata { get; set; } = new();

    // Character archetypes (NPCs, bosses, merchants, etc.)
    public List<AvatarArchetype> CharacterArchetypes { get; set; } = new();

    // Equipment (weapons, armor, accessories)
    public List<Equipment> Equipment { get; set; } = new();

    // Spells and abilities
    public List<Spell> Spells { get; set; } = new();

    // Consumable items (potions, food, materials)
    public List<Consumable> Consumables { get; set; } = new();

    // Tools (pickaxes, shovels, etc.)
    public List<Tool> Tools { get; set; } = new();

    // Character affinities (element types for combat)
    public List<CharacterAffinity> Affinities { get; set; } = new();

    // Factions (once generated - will be null until Faction.xsd is generated)
    public List<object>? Factions { get; set; }

    /// <summary>
    /// Get a random character archetype from the theme
    /// </summary>
    public AvatarArchetype? GetRandomArchetype(Random? random = null)
    {
        if (CharacterArchetypes.Count == 0) return null;
        var rng = random ?? new Random();
        return CharacterArchetypes[rng.Next(CharacterArchetypes.Count)];
    }

    /// <summary>
    /// Get random equipment
    /// NOTE: rarity parameter kept for API compatibility but ignored (Equipment schema has no Rarity property)
    /// </summary>
    public Equipment? GetRandomEquipment(string? rarity = null, Random? random = null)
    {
        // Equipment schema doesn't have Rarity property, so we ignore the rarity parameter
        var pool = Equipment;

        if (pool.Count == 0) return null;
        var rng = random ?? new Random();
        return pool[rng.Next(pool.Count)];
    }

    /// <summary>
    /// Get random spell by affinity
    /// </summary>
    public Spell? GetRandomSpell(string? affinity = null, Random? random = null)
    {
        var pool = string.IsNullOrEmpty(affinity)
            ? Spells
            : Spells.Where(s => s.AffinityRef == affinity).ToList();

        if (pool.Count == 0) return null;
        var rng = random ?? new Random();
        return pool[rng.Next(pool.Count)];
    }

    /// <summary>
    /// Check if theme has content loaded
    /// </summary>
    public bool HasContent => CharacterArchetypes.Count > 0
                            || Equipment.Count > 0
                            || Spells.Count > 0
                            || Consumables.Count > 0
                            || Tools.Count > 0
                            || Affinities.Count > 0;
}
