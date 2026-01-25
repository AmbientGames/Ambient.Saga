using Ambient.Domain.Contracts;

namespace Ambient.Domain.GameLogic.Gameplay.Avatar;

/// <summary>
/// Handles spawning and respawning of gameplay items from IGameplayItemProviders.
/// Works alongside AvatarSpawner which handles the schema-based capabilities.
/// </summary>
public static class GameplayItemSpawner
{
    /// <summary>
    /// Applies starting items from all providers on initial spawn.
    /// Call this after AvatarSpawner.SpawnFromModelAvatar().
    /// </summary>
    /// <param name="avatar">The avatar being spawned.</param>
    /// <param name="providers">The gameplay item providers.</param>
    /// <param name="archetypeRef">Optional archetype for conditional loadouts.</param>
    public static void ApplySpawnItems(
        AvatarBase avatar,
        IEnumerable<IGameplayItemProvider> providers,
        string? archetypeRef = null)
    {
        foreach (var provider in providers)
        {
            ApplySpawnItems(avatar, provider, archetypeRef);
        }
    }

    /// <summary>
    /// Applies starting items from a single provider on initial spawn.
    /// </summary>
    public static void ApplySpawnItems(
        AvatarBase avatar,
        IGameplayItemProvider provider,
        string? archetypeRef = null)
    {
        // Clear any existing inventory for this provider
        GameplayInventoryService.ClearInventory(avatar, provider.Name);

        // Apply spawn items
        foreach (var item in provider.GetSpawnItems(archetypeRef))
        {
            GameplayInventoryService.AddItem(avatar, provider.Name, item.RefName, item.Quantity);
        }

        // Set first item as current if provider has items and nothing selected
        if (provider.CurrentItemRef == null)
        {
            var firstItem = provider.GetSpawnItems(archetypeRef).FirstOrDefault();
            if (firstItem != null)
            {
                provider.CurrentItemRef = firstItem.RefName;
            }
        }
    }

    /// <summary>
    /// Applies respawn items from all providers after death.
    /// Call this after AvatarSpawner.ReSpawnFromModelAvatar().
    /// </summary>
    /// <param name="avatar">The avatar being respawned.</param>
    /// <param name="providers">The gameplay item providers.</param>
    /// <param name="archetypeRef">Optional archetype for conditional loadouts.</param>
    public static void ApplyRespawnItems(
        AvatarBase avatar,
        IEnumerable<IGameplayItemProvider> providers,
        string? archetypeRef = null)
    {
        foreach (var provider in providers)
        {
            ApplyRespawnItems(avatar, provider, archetypeRef);
        }
    }

    /// <summary>
    /// Applies respawn items from a single provider after death.
    /// </summary>
    public static void ApplyRespawnItems(
        AvatarBase avatar,
        IGameplayItemProvider provider,
        string? archetypeRef = null)
    {
        // Clear if provider requests it
        if (provider.ClearOnRespawn)
        {
            GameplayInventoryService.ClearInventory(avatar, provider.Name);
            provider.CurrentItemRef = null;
        }

        // Apply respawn items
        foreach (var item in provider.GetRespawnItems(archetypeRef))
        {
            GameplayInventoryService.AddItem(avatar, provider.Name, item.RefName, item.Quantity);
        }

        // Set first item as current if nothing selected
        if (provider.CurrentItemRef == null)
        {
            var firstItem = provider.GetRespawnItems(archetypeRef).FirstOrDefault();
            if (firstItem != null)
            {
                provider.CurrentItemRef = firstItem.RefName;
            }
        }
    }

    /// <summary>
    /// Full spawn helper that calls both AvatarSpawner and GameplayItemSpawner.
    /// </summary>
    public static void FullSpawn(
        AvatarBase avatar,
        AvatarArchetype archetype,
        IEnumerable<IGameplayItemProvider> providers)
    {
        // First, apply schema-based capabilities
        AvatarSpawner.SpawnFromModelAvatar(avatar, archetype);

        // Then, apply gameplay item provider items
        ApplySpawnItems(avatar, providers, archetype.RefName);
    }

    /// <summary>
    /// Full respawn helper that calls both AvatarSpawner and GameplayItemSpawner.
    /// </summary>
    public static void FullRespawn(
        AvatarBase avatar,
        AvatarArchetype archetype,
        IEnumerable<IGameplayItemProvider> providers)
    {
        // First, apply schema-based capabilities
        AvatarSpawner.ReSpawnFromModelAvatar(avatar, archetype);

        // Then, apply gameplay item provider items
        ApplyRespawnItems(avatar, providers, archetype.RefName);
    }
}
