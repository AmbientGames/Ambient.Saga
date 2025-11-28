using Ambient.Domain;
using Ambient.Saga.StoryGenerator.Models;

namespace Ambient.Saga.StoryGenerator;

/// <summary>
/// Generates theme-aware narrative content for characters, factions, and dialogue
/// Replaces [AI: ...] placeholders with contextual, theme-appropriate content
/// </summary>
public class ThemeAwareContentGenerator
{
    private readonly ThemeContent? _theme;
    private readonly Random _random;

    public ThemeAwareContentGenerator(ThemeContent? theme, int seed = 42)
    {
        _theme = theme;
        _random = new Random(seed);
    }

    #region Character Equipment Selection (NPCs/Bosses/Merchants)

    /// <summary>
    /// Generic equipment selection for NPCs using ExtensionData metadata
    /// Uses characterType as suitableFor filter, powerLevel based on difficultyTier
    /// </summary>
    public List<(string EquipmentRef, double Condition)> SelectCharacterEquipment(
        string characterType,
        string difficultyTier)
    {
        if (_theme?.Equipment == null || _theme.Equipment.Count == 0)
        {
            return new List<(string, double)> { ("GenericWeapon", 1.0) };
        }

        // Generic selection: filter by metadata
        var items = _theme.Equipment
            .Select(e => new { Item = e, Metadata = ItemMetadata.FromJson(e.ExtensionData) })
            .Where(x => x.Metadata != null)
            .ToArray();

        // Prefer items matching difficulty tier's power level
        var powerLevel = difficultyTier switch
        {
            "Easy" => "Starter",
            "Normal" => "Basic",
            "Hard" => "Advanced",
            "Epic" => "Epic",
            _ => "Starter"
        };

        var filtered = items.Where(x => x.Metadata!.PowerLevel == powerLevel).ToArray();
        var pool = filtered.Length > 0 ? filtered : items;

        // Select 1-2 random items
        var itemCount = characterType == "Boss" ? 2 : 1;
        return SelectRandomItems(pool, itemCount, itemCount);
    }

    /// <summary>
    /// Generic spell selection for NPCs using ExtensionData metadata
    /// </summary>
    public List<(string SpellRef, double Condition)> SelectCharacterSpells(
        string characterType,
        string difficultyTier)
    {
        if (_theme?.Spells == null || _theme.Spells.Count == 0)
        {
            return new List<(string, double)> { ("GenericAttack", 1.0) };
        }

        // Spell count based on difficulty
        var spellCount = difficultyTier switch
        {
            "Easy" => 1,
            "Normal" => 2,
            "Hard" => 3,
            "Epic" => 4,
            _ => 1
        };

        // Select random spells (can be enhanced with metadata filtering later)
        var spells = _theme.Spells.OrderBy(x => _random.Next()).Take(spellCount)
            .Select(s => (s.RefName, 1.0))
            .ToList();

        return spells.Count > 0 ? spells : new List<(string, double)> { ("GenericAttack", 1.0) };
    }

    #endregion

    #region Avatar Archetype Loadout Selection

    /// <summary>
    /// Simple equipment selection: items.Where(i => i.Metadata.SuitableFor.Contains(characterType))
    /// Optionally prefers matching affinity (reads from entity's AffinityRef property)
    /// </summary>
    public List<(string EquipmentRef, double Condition)> SelectAvatarEquipment(
        string characterType,
        string? preferredAffinity = null)
    {
        if (_theme?.Equipment == null || _theme.Equipment.Count == 0)
        {
            return new List<(string, double)> { ("GenericWeapon", 1.0) };
        }

        // Simple matching: suitableFor.Contains(characterType)
        var matches = _theme.Equipment
            .Select(e => new { Item = e, Metadata = ItemMetadata.FromJson(e.ExtensionData) })
            .Where(x => x.Metadata != null && x.Metadata.SuitableFor.Contains(characterType))
            .ToArray();

        // No matches? Pick randomly from all
        if (matches.Length == 0)
        {
            var randomItem = _theme.Equipment[_random.Next(_theme.Equipment.Count)];
            return new List<(string, double)> { (randomItem.RefName, 1.0) };
        }

        // Optionally prefer matching affinity (if specified)
        var pool = matches;
        if (!string.IsNullOrEmpty(preferredAffinity))
        {
            var affinityMatches = matches.Where(x => x.Item.AffinityRef == preferredAffinity).ToArray();
            if (affinityMatches.Length > 0)
                pool = affinityMatches;
        }

        // Select 1-2 random items
        return SelectRandomItems(pool, 1, 2);
    }

    /// <summary>
    /// Helper: Randomly select N items from a pool without duplicates
    /// </summary>
    private List<(string RefName, double Condition)> SelectRandomItems(
        dynamic[] itemPool,
        int minCount,
        int maxCount)
    {
        var result = new List<(string, double)>();
        if (itemPool.Length == 0) return result;

        var count = _random.Next(minCount, Math.Min(maxCount + 1, itemPool.Length + 1));
        var selectedIndices = new HashSet<int>();

        while (result.Count < count && selectedIndices.Count < itemPool.Length)
        {
            var index = _random.Next(itemPool.Length);
            if (selectedIndices.Add(index))
            {
                result.Add((itemPool[index].Item.RefName, 1.0));
            }
        }

        return result.Count > 0 ? result : new List<(string, double)> { ("GenericWeapon", 1.0) };
    }

    /// <summary>
    /// Simple spell selection: items.Where(i => i.Metadata.SuitableFor.Contains(characterType))
    /// Optionally prefers matching affinity (reads from entity's AffinityRef property)
    /// </summary>
    public List<(string SpellRef, double Condition)> SelectAvatarSpells(
        string characterType,
        string? preferredAffinity = null)
    {
        if (_theme?.Spells == null || _theme.Spells.Count == 0)
        {
            return new List<(string, double)> { ("GenericAttack", 1.0) };
        }

        // Simple matching: suitableFor.Contains(characterType)
        var matches = _theme.Spells
            .Select(s => new { Item = s, Metadata = ItemMetadata.FromJson(s.ExtensionData) })
            .Where(x => x.Metadata != null && x.Metadata.SuitableFor.Contains(characterType))
            .ToArray();

        // No matches? Pick randomly from all
        if (matches.Length == 0)
        {
            var randomSpell = _theme.Spells[_random.Next(_theme.Spells.Count)];
            return new List<(string, double)> { (randomSpell.RefName, 1.0) };
        }

        // Optionally prefer matching affinity (if specified)
        var pool = matches;
        if (!string.IsNullOrEmpty(preferredAffinity))
        {
            var affinityMatches = matches.Where(x => x.Item.AffinityRef == preferredAffinity).ToArray();
            if (affinityMatches.Length > 0)
                pool = affinityMatches;
        }

        // Select 1-2 random spells
        return SelectRandomItems(pool, 1, 2);
    }

    /// <summary>
    /// Select consumables from theme for avatar starting loadout.
    /// Picks 2-4 random consumables from the theme.
    /// </summary>
    public List<(string ConsumableRef, int Quantity)> SelectAvatarConsumables(
        string characterType)
    {
        if (_theme?.Consumables == null || _theme.Consumables.Count == 0)
        {
            return new List<(string, int)>();
        }

        // Select 2-4 random consumables from theme
        var consumables = new List<(string, int)>();
        var selectedCount = Math.Min(_random.Next(2, 5), _theme.Consumables.Count);
        var selectedIndices = new HashSet<int>();

        while (consumables.Count < selectedCount && selectedIndices.Count < _theme.Consumables.Count)
        {
            var index = _random.Next(_theme.Consumables.Count);
            if (selectedIndices.Add(index))
            {
                var quantity = _random.Next(2, 6); // 2-5 of each item
                consumables.Add((_theme.Consumables[index].RefName, quantity));
            }
        }

        return consumables;
    }

    /// <summary>
    /// Select theme-appropriate starting tools for avatar archetypes
    /// </summary>
    public List<(string ToolRef, double Condition)> SelectAvatarTools()
    {
        if (_theme?.Tools == null || _theme.Tools.Count == 0)
        {
            // Fallback to generic tools
            return new List<(string, double)>
            {
                ("Multitool", 0.8),
                ("Pickaxe", 0.7),
                ("Axe", 0.7),
                ("Spade", 0.7)
            };
        }

        var tools = new List<(string, double)>();

        // Find multitool
        var multitools = _theme.Tools.Where(t =>
            t.DisplayName.Contains("multi", StringComparison.OrdinalIgnoreCase)).ToArray();

        // Find pickaxe
        var pickaxes = _theme.Tools.Where(t =>
            t.DisplayName.Contains("pick", StringComparison.OrdinalIgnoreCase)).ToArray();

        // Find axe
        var axes = _theme.Tools.Where(t =>
            t.DisplayName.Contains("axe", StringComparison.OrdinalIgnoreCase)).ToArray();

        // Find shovel/spade
        var shovels = _theme.Tools.Where(t =>
            t.DisplayName.Contains("shovel", StringComparison.OrdinalIgnoreCase) ||
            t.DisplayName.Contains("spade", StringComparison.OrdinalIgnoreCase)).ToArray();

        // Add tools if found
        if (multitools.Length > 0)
            tools.Add((multitools[_random.Next(multitools.Length)].RefName, 0.8));
        if (pickaxes.Length > 0)
            tools.Add((pickaxes[_random.Next(pickaxes.Length)].RefName, 0.7));
        if (axes.Length > 0)
            tools.Add((axes[_random.Next(axes.Length)].RefName, 0.7));
        if (shovels.Length > 0)
            tools.Add((shovels[_random.Next(shovels.Length)].RefName, 0.7));

        // Fallback if no tools found
        if (tools.Count == 0)
        {
            // Just add whatever tools are available
            foreach (var tool in _theme.Tools.Take(4))
            {
                tools.Add((tool.RefName, 0.7));
            }
        }

        // If still nothing, use generic fallback
        if (tools.Count == 0)
        {
            tools.Add(("Multitool", 0.8));
            tools.Add(("Pickaxe", 0.7));
        }

        return tools;
    }

    #endregion

    #region Faction Names

    /// <summary>
    /// Generate theme-appropriate faction name based on category using theme metadata
    /// </summary>
    public string GenerateFactionName(string category, string? threadRef = null)
    {
        var themeName = _theme?.Metadata?.DisplayName ?? "Faction";

        // Generate generic but theme-flavored faction names
        return category switch
        {
            "City" => $"{themeName} Guard",
            "Military" => $"{themeName} Defense Force",
            "Merchant" => $"{themeName} Trade Guild",
            "Guild" => $"{themeName} Craftsmen",
            "Religious" => $"{themeName} Order",
            "Wilderness" => $"{themeName} Rangers",
            "Bandit" => $"{themeName} Outlaws",
            _ => $"{themeName} {category}"
        };
    }

    /// <summary>
    /// Generate theme-appropriate faction description
    /// </summary>
    public string GenerateFactionDescription(string category, string factionName)
    {
        return (category, factionName) switch
        {
            ("City", _) => $"Protectors of civilization and urban order. {factionName} maintains peace in settled areas.",
            ("Military", _) => $"Elite fighting force sworn to defend the realm. {factionName} answers only to the highest authority.",
            ("Merchant", _) => $"Influential trading organization controlling commerce. {factionName} values profit and fair dealing.",
            ("Guild", _) => $"Association of skilled craftsmen and professionals. {factionName} upholds quality standards.",
            ("Religious", _) => $"Spiritual guardians devoted to higher purpose. {factionName} guides the faithful.",
            ("Wilderness", _) => $"Independent survivalists living off the land. {factionName} respects nature's balance.",
            ("Bandit", _) => $"Outlaws operating outside the law. {factionName} takes what it needs by force.",
            _ => $"Organized group with shared interests and goals."
        };
    }

    #endregion

    #region Character Greetings

    /// <summary>
    /// Generate character greeting based on location and role
    /// Generic templates that work for any theme
    /// </summary>
    public string GenerateCharacterGreeting(
        string characterType,
        string locationDisplayName,
        string? personality = null)
    {
        return characterType switch
        {
            "Boss" => $"Halt! I am the guardian of {locationDisplayName}. None may pass without proving their worth!",
            "Merchant" => $"Welcome, traveler! You've reached {locationDisplayName}. I have wares to offer - take a look!",
            "NPC" => $"Greetings, friend. Are you also visiting {locationDisplayName}? It's quite a sight to behold.",
            "QuestGiver" => $"Oh, thank goodness! Someone's come to {locationDisplayName}. I desperately need assistance!",
            _ => $"Welcome to {locationDisplayName}."
        };
    }

    #endregion

    #region Battle Dialogue

    /// <summary>
    /// Generate battle dialogue for a specific trigger point
    /// Generic templates that work for any theme
    /// </summary>
    public string GenerateBattleDialogue(
        string triggerType,
        string characterDisplayName,
        string locationDisplayName)
    {
        return triggerType switch
        {
            "battle_opening" => $"So, you wish to challenge me? Very well! I, {characterDisplayName}, guardian of {locationDisplayName}, accept!",
            "battle_first_blood" => $"You've drawn first blood... Impressive! But this battle has only just begun!",
            "battle_berserk" => $"Enough holding back! WITNESS MY TRUE POWER! I will defend {locationDisplayName} with everything I have!",
            "battle_retreat" => $"You're stronger than I thought... I must be more careful now!",
            "battle_last_stand" => $"*breathing heavily* I'm nearly finished... Will you show mercy, or end this?",
            "battle_defeated" => $"You have... bested me... {locationDisplayName}... is yours... You've earned it...",
            _ => "..."
        };
    }

    #endregion

    #region Dialogue Choices

    /// <summary>
    /// Generate contextual dialogue choices
    /// </summary>
    public List<(string Text, string Purpose)> GenerateDialogueChoices(string characterType, string context)
    {
        var choices = new List<(string, string)>();

        switch (characterType)
        {
            case "Boss":
                choices.Add(("I accept your challenge!", "battle"));
                choices.Add(("Tell me about this place", "info"));
                choices.Add(("I'm not ready yet", "leave"));
                break;

            case "Merchant":
                choices.Add(("Show me your wares", "trade"));
                choices.Add(("Can we negotiate on price?", "bargain"));
                choices.Add(("What news from the road?", "rumors"));
                choices.Add(("I'll come back later", "leave"));
                break;

            case "NPC":
                choices.Add(("What can you tell me about this area?", "info"));
                choices.Add(("Have you seen anything unusual?", "rumors"));
                choices.Add(("Safe travels, friend", "leave"));
                break;

            case "QuestGiver":
                choices.Add(("What do you need help with?", "quest_details"));
                choices.Add(("I'll help you!", "accept_quest"));
                choices.Add(("Sorry, I can't right now", "decline"));
                break;

            default:
                choices.Add(("Hello", "greeting"));
                choices.Add(("Goodbye", "leave"));
                break;
        }

        return choices;
    }

    #endregion
}
