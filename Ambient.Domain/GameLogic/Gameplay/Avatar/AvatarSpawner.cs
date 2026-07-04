using Ambient.Domain.Enums;

namespace Ambient.Domain.GameLogic.Gameplay.Avatar;

/// <summary>
/// Static utility class that handles avatar spawning and respawning logic, including setting up initial blocks, tools, and vital statistics based on archetype definitions.
/// </summary>
public static class AvatarSpawner
{
    /// <summary>
    /// Initializes a new avatar with blocks and capabilities from archetype.
    /// </summary>
    public static void SpawnFromModelAvatar(AvatarBase avatar, AvatarArchetype archetype)
    {
        // todo: this seems like a hack - Stats should really be initialized at this point IMO
        avatar.Stats = new CharacterStats();
        CharacterStatsCopier.CopyCharacterStats(archetype.SpawnStats, avatar.Stats);

        avatar.ArchetypeBias = archetype.ArchetypeBias;
        avatar.BaseSpeedFactor = CarryWeightCalculator.GetBaseSpeedFactor(archetype);

        avatar.Capabilities = new ItemCollection();
        avatar.Capabilities.Blocks = archetype.SpawnCapabilities.Blocks?.ToArray() ?? [];
        avatar.Capabilities.Tools = archetype.SpawnCapabilities.Tools?.ToArray() ?? [];
        avatar.Capabilities.Equipment = archetype.SpawnCapabilities.Equipment?.ToArray() ?? [];
        avatar.Capabilities.Consumables = archetype.SpawnCapabilities.Consumables?.ToArray() ?? [];
        avatar.Capabilities.Spells = archetype.SpawnCapabilities.Spells?.ToArray() ?? [];
        avatar.Capabilities.BuildingMaterials = archetype.SpawnCapabilities.BuildingMaterials?.ToArray() ?? [];
    }

    /// <summary>
    /// Reinitializes avatar after death/respawn from archetype.
    /// </summary>
    public static void ReSpawnFromModelAvatar(AvatarBase avatar, AvatarArchetype archetype)
    {
        // Respawn resets vitals to archetype values but must not wipe earned
        // progression — credits, experience and level persist through death.
        // (A deliberate death penalty, if ever wanted, belongs here as an
        // explicit rule, not as a side effect of the blanket stat copy.)
        var credits = avatar.Stats.Credits;
        var experience = avatar.Stats.Experience;
        var level = avatar.Stats.Level;

        CharacterStatsCopier.CopyCharacterStats(archetype.RespawnStats, avatar.Stats);

        avatar.Stats.Credits = credits;
        avatar.Stats.Experience = experience;
        avatar.Stats.Level = level;

        avatar.Capabilities.Blocks = archetype.RespawnCapabilities.Blocks?.ToArray() ?? [];
        avatar.Capabilities.Tools = archetype.RespawnCapabilities.Tools?.ToArray() ?? [];
        avatar.Capabilities.Equipment = archetype.RespawnCapabilities.Equipment?.ToArray() ?? [];
        avatar.Capabilities.Consumables = archetype.RespawnCapabilities.Consumables?.ToArray() ?? [];
        avatar.Capabilities.Spells = archetype.RespawnCapabilities.Spells?.ToArray() ?? [];
        avatar.Capabilities.BuildingMaterials = archetype.RespawnCapabilities.BuildingMaterials?.ToArray() ?? [];
    }

}