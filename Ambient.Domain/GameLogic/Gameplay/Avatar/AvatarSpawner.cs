using Ambient.Domain.Enums;
using System.Reflection;

namespace Ambient.Domain.GameLogic.Gameplay.Avatar;

/// <summary>
/// Static utility class that handles avatar spawning and respawning logic for Saga-owned item types.
/// Blocks, Tools, and BuildingMaterials are handled by IGameplayItemProvider in Core.
/// </summary>
public static class AvatarSpawner
{
    /// <summary>
    /// Initializes a new avatar with capabilities from archetype (Saga-owned types only).
    /// </summary>
    public static void SpawnFromModelAvatar(AvatarBase avatar, AvatarArchetype archetype)
    {
        // todo: this seems like a hack - Stats should really be initialized at this point IMO
        avatar.Stats = new CharacterStats();
        CharacterStatsCopier.CopyCharacterStats(archetype.SpawnStats, avatar.Stats);

        avatar.ArchetypeBias = archetype.ArchetypeBias;

        avatar.Capabilities = new ItemCollection();
        avatar.Capabilities.Equipment = archetype.SpawnCapabilities.Equipment?.ToArray() ?? [];
        avatar.Capabilities.Consumables = archetype.SpawnCapabilities.Consumables?.ToArray() ?? [];
        avatar.Capabilities.Spells = archetype.SpawnCapabilities.Spells?.ToArray() ?? [];
        avatar.Capabilities.QuestTokens = archetype.SpawnCapabilities.QuestTokens?.ToArray() ?? [];
    }

    /// <summary>
    /// Reinitializes avatar after death/respawn from archetype (Saga-owned types only).
    /// </summary>
    public static void ReSpawnFromModelAvatar(AvatarBase avatar, AvatarArchetype archetype)
    {
        CharacterStatsCopier.CopyCharacterStats(archetype.RespawnStats, avatar.Stats);

        avatar.Capabilities.Equipment = archetype.RespawnCapabilities.Equipment?.ToArray() ?? [];
        avatar.Capabilities.Consumables = archetype.RespawnCapabilities.Consumables?.ToArray() ?? [];
        avatar.Capabilities.Spells = archetype.RespawnCapabilities.Spells?.ToArray() ?? [];
        avatar.Capabilities.QuestTokens = archetype.RespawnCapabilities.QuestTokens?.ToArray() ?? [];
    }
}
