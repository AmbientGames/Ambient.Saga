using Ambient.Saga.WorldForge.Models;

namespace Ambient.Saga.WorldForge;

/// <summary>
/// Resolves item references from theme content.
/// Provides fallbacks when theme items are not available.
/// Extracted from QuestGenerator to follow Single Responsibility Principle.
/// </summary>
public class ThemeItemResolver
{
    private readonly ThemeContent? _theme;
    private readonly Random _random;

    public ThemeItemResolver(ThemeContent? theme, Random random)
    {
        _theme = theme;
        _random = random;
    }

    /// <summary>
    /// Get a random equipment RefName from theme, or generate a generic one if theme not available
    /// </summary>
    public string GetRandomEquipmentRef(string contextHint)
    {
        if (_theme?.Equipment != null && _theme.Equipment.Count > 0)
        {
            var randomEquipment = _theme.Equipment[_random.Next(_theme.Equipment.Count)];
            return randomEquipment.RefName;
        }
        // Fallback: generate generic RefName from context
        return $"EQUIPMENT_{contextHint.ToUpper().Replace(" ", "_")}";
    }

    /// <summary>
    /// Get a random consumable RefName from theme, or generate a generic one if theme not available
    /// </summary>
    public string GetRandomConsumableRef(string contextHint)
    {
        if (_theme?.Consumables != null && _theme.Consumables.Count > 0)
        {
            var randomConsumable = _theme.Consumables[_random.Next(_theme.Consumables.Count)];
            return randomConsumable.RefName;
        }
        // Fallback: generate generic RefName from context
        return $"CONSUMABLE_{contextHint.ToUpper().Replace(" ", "_")}";
    }

    /// <summary>
    /// Get a random character archetype RefName from theme, or generate a generic one if theme not available
    /// </summary>
    public string GetRandomCharacterRef(string characterType)
    {
        if (_theme?.CharacterArchetypes != null && _theme.CharacterArchetypes.Count > 0)
        {
            // Try to find a matching archetype by type
            var matchingArchetypes = _theme.CharacterArchetypes
                .Where(a => a.RefName.Contains(characterType, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (matchingArchetypes.Count > 0)
            {
                return matchingArchetypes[_random.Next(matchingArchetypes.Count)].RefName;
            }

            // No match, return random archetype
            var randomArchetype = _theme.CharacterArchetypes[_random.Next(_theme.CharacterArchetypes.Count)];
            return randomArchetype.RefName;
        }
        // Fallback: generate generic RefName
        return $"NPC_{characterType.ToUpper()}";
    }

    /// <summary>
    /// Get a random item RefName (any type) from theme
    /// </summary>
    public string GetRandomItemRef(string contextHint)
    {
        // Try equipment first (most common quest items)
        if (_theme?.Equipment != null && _theme.Equipment.Count > 0)
        {
            var randomEquipment = _theme.Equipment[_random.Next(_theme.Equipment.Count)];
            return randomEquipment.RefName;
        }
        // Then tools
        if (_theme?.Tools != null && _theme.Tools.Count > 0)
        {
            var randomTool = _theme.Tools[_random.Next(_theme.Tools.Count)];
            return randomTool.RefName;
        }
        // Fallback: generate generic RefName
        return $"ITEM_{contextHint.ToUpper().Replace(" ", "_")}";
    }
}
