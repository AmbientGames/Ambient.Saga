using Ambient.Domain.Enums;
using System.Reflection;

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

        avatar.Capabilities = new ItemCollection();
        avatar.Capabilities.Blocks = archetype.SpawnCapabilities.Blocks?.ToArray() ?? [];
        avatar.Capabilities.Tools = archetype.SpawnCapabilities.Tools?.ToArray() ?? [];
        avatar.Capabilities.Equipment = archetype.SpawnCapabilities.Equipment?.ToArray() ?? [];
        avatar.Capabilities.Consumables = archetype.SpawnCapabilities.Consumables?.ToArray() ?? [];
        avatar.Capabilities.Spells = archetype.SpawnCapabilities.Spells?.ToArray() ?? [];
        avatar.Capabilities.BuildingMaterials = archetype.SpawnCapabilities.BuildingMaterials?.ToArray() ?? [];
        avatar.Capabilities.QuestTokens = archetype.SpawnCapabilities.QuestTokens?.ToArray() ?? [];

        SetAvailableBlocks(avatar, avatar.Capabilities.Blocks);
    }

    /// <summary>
    /// Reinitializes avatar after death/respawn from archetype.
    /// </summary>
    public static void ReSpawnFromModelAvatar(AvatarBase avatar, AvatarArchetype archetype)
    {
        CharacterStatsCopier.CopyCharacterStats(archetype.RespawnStats, avatar.Stats);

        avatar.Capabilities.Blocks = archetype.RespawnCapabilities.Blocks?.ToArray() ?? [];
        avatar.Capabilities.Tools = archetype.RespawnCapabilities.Tools?.ToArray() ?? [];
        avatar.Capabilities.Equipment = archetype.RespawnCapabilities.Equipment?.ToArray() ?? [];
        avatar.Capabilities.Consumables = archetype.RespawnCapabilities.Consumables?.ToArray() ?? [];
        avatar.Capabilities.Spells = archetype.RespawnCapabilities.Spells?.ToArray() ?? [];
        avatar.Capabilities.BuildingMaterials = archetype.RespawnCapabilities.BuildingMaterials?.ToArray() ?? [];
        avatar.Capabilities.QuestTokens = archetype.RespawnCapabilities.QuestTokens?.ToArray() ?? [];

        avatar.BlockOwnership.Clear();
        SetAvailableBlocks(avatar, avatar.Capabilities.Blocks);
    }

    private static void SetAvailableBlocks(AvatarBase avatar, BlockEntry[] blocks)
    {
        foreach (var block in blocks)
        {
            if (block?.BlockRef == null) continue;
            avatar.BlockOwnership[block.BlockRef] = block.Quantity; // last one wins, same as before
        }
    }
}