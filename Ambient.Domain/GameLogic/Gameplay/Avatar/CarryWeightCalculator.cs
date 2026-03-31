using Ambient.Domain.Contracts;

namespace Ambient.Domain.GameLogic.Gameplay.Avatar;

public static class CarryWeightCalculator
{
    /// <summary>
    /// Multiplier applied to Weight * Strength to derive maximum carry capacity.
    /// </summary>
    private const float CarryWeightMultiplier = 5f;

    public static float GetMaxCarryWeight(AvatarArchetype archetype)
    {
        var strength = archetype.SpawnStats?.Strength ?? 0.1f;
        return archetype.Weight * strength * CarryWeightMultiplier;
    }

    public static float CalculateTotalWeight(ItemCollection? capabilities, IWorldConfiguration config)
    {
        if (capabilities == null) return 0;

        var total = 0f;

        if (capabilities.Blocks != null)
        {
            foreach (var b in capabilities.Blocks)
                total += b.Quantity * config.BlockWeight;
        }

        if (capabilities.Equipment != null)
            total += capabilities.Equipment.Length * config.EquipmentWeight;

        if (capabilities.Tools != null)
            total += capabilities.Tools.Length * config.ToolWeight;

        if (capabilities.Spells != null)
            total += capabilities.Spells.Length * config.SpellWeight;

        if (capabilities.Consumables != null)
        {
            foreach (var c in capabilities.Consumables)
                total += c.Quantity * config.ConsumableWeight;
        }

        if (capabilities.BuildingMaterials != null)
        {
            foreach (var m in capabilities.BuildingMaterials)
                total += m.Quantity * config.BuildingMaterialWeight;
        }

        // QuestTokens are always weightless

        return total;
    }

    public static float GetRemainingCapacity(ItemCollection? capabilities, AvatarArchetype archetype, IWorldConfiguration config)
    {
        var max = GetMaxCarryWeight(archetype);
        var current = CalculateTotalWeight(capabilities, config);
        return max - current;
    }

    public static bool WouldExceedCapacity(ItemCollection? capabilities, AvatarArchetype archetype, IWorldConfiguration config, float additionalWeight)
    {
        return GetRemainingCapacity(capabilities, archetype, config) < additionalWeight;
    }

    public static float GetCategoryWeight(string categoryName, IWorldConfiguration config)
    {
        return categoryName switch
        {
            "block" => config.BlockWeight,
            "equipment" => config.EquipmentWeight,
            "tool" => config.ToolWeight,
            "spell" => config.SpellWeight,
            "consumable" => config.ConsumableWeight,
            "buildingmaterial" => config.BuildingMaterialWeight,
            _ => 0
        };
    }
}
