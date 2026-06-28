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

    /// <summary>
    /// Base speed factor from body weight alone (no loadout).
    /// Lighter archetypes are inherently faster. Range: ~1.0 (lightest) to ~0.85 (heaviest).
    /// Reference weight is 50kg — at or below this you're at full speed.
    /// </summary>
    public static float GetBaseSpeedFactor(AvatarArchetype archetype)
    {
        const float referenceWeight = 50f;
        const float maxPenalty = 0.15f; // Heaviest loses 15% base speed
        const float penaltyRange = 50f; // Weight range over which penalty applies (50–100kg)

        var excess = Math.Max(0, archetype.Weight - referenceWeight);
        var penalty = Math.Min(maxPenalty, excess / penaltyRange * maxPenalty);
        return 1f - penalty;
    }

    /// <summary>
    /// Speed multiplier from loadout weight only (not body weight).
    /// Returns 1.0 at 0% load, 0.5 at 100% load, 0.3 if over capacity.
    /// </summary>
    public static float GetLoadoutSpeedFactor(float totalWeight, float maxWeight)
    {
        if (maxWeight <= 0) return 1f;

        var loadRatio = totalWeight / maxWeight;

        if (loadRatio > 1f)
            return 0.3f;

        return 1f - loadRatio * 0.5f;
    }

    /// <summary>
    /// Combined speed at spawn: base body weight factor * loadout factor.
    /// For display at archetype selection.
    /// </summary>
    public static float GetSpawnSpeedMultiplier(AvatarArchetype archetype, IWorldConfiguration config)
    {
        var baseFactor = GetBaseSpeedFactor(archetype);
        var totalWeight = CalculateTotalWeight(archetype.SpawnCapabilities, config);
        var maxWeight = GetMaxCarryWeight(archetype);
        var loadoutFactor = GetLoadoutSpeedFactor(totalWeight, maxWeight);
        return baseFactor * loadoutFactor;
    }

    /// <summary>
    /// Combined speed in-game: base body weight factor * current loadout factor.
    /// For movement speed calculation.
    /// </summary>
    public static float GetSpeedMultiplier(ItemCollection? capabilities, AvatarArchetype archetype, IWorldConfiguration config)
    {
        var baseFactor = GetBaseSpeedFactor(archetype);
        var totalWeight = CalculateTotalWeight(capabilities, config);
        var maxWeight = GetMaxCarryWeight(archetype);
        var loadoutFactor = GetLoadoutSpeedFactor(totalWeight, maxWeight);
        return baseFactor * loadoutFactor;
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
