using Ambient.Domain.Contracts;

namespace Ambient.Domain.GameLogic.Gameplay.Avatar;

public static class CarryWeightCalculator
{
    private const int DefaultMaxCarryWeight = 50000;

    public static int GetMaxCarryWeight(AvatarArchetype? archetype)
    {
        return archetype?.MaxCarryWeight ?? DefaultMaxCarryWeight;
    }

    public static int CalculateTotalWeight(ItemCollection? capabilities, IWorldConfiguration config)
    {
        if (capabilities == null) return 0;

        var total = 0;

        if (capabilities.Blocks != null)
        {
            foreach (var b in capabilities.Blocks)
                total += (int)b.Quantity * config.BlockWeight;
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
                total += (int)c.Quantity * config.ConsumableWeight;
        }

        if (capabilities.BuildingMaterials != null)
        {
            foreach (var m in capabilities.BuildingMaterials)
                total += (int)m.Quantity * config.BuildingMaterialWeight;
        }

        // QuestTokens are always weightless

        return total;
    }

    public static int GetRemainingCapacity(ItemCollection? capabilities, AvatarArchetype? archetype, IWorldConfiguration config)
    {
        var max = GetMaxCarryWeight(archetype);
        var current = CalculateTotalWeight(capabilities, config);
        return max - current;
    }

    public static bool WouldExceedCapacity(ItemCollection? capabilities, AvatarArchetype? archetype, IWorldConfiguration config, int additionalWeight)
    {
        return GetRemainingCapacity(capabilities, archetype, config) < additionalWeight;
    }

    public static int GetCategoryWeight(string categoryName, IWorldConfiguration config)
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
