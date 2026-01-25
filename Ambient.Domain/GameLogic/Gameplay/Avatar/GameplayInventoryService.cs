using Ambient.Domain.Contracts;

namespace Ambient.Domain.GameLogic.Gameplay.Avatar;

/// <summary>
/// Service for managing gameplay inventory from IGameplayItemProviders.
/// Works with the avatar's GameplayInventory dictionary.
/// </summary>
public static class GameplayInventoryService
{
    /// <summary>
    /// Gets or creates the inventory dictionary for a specific provider.
    /// </summary>
    public static Dictionary<string, int> GetOrCreateInventory(AvatarBase avatar, string providerName)
    {
        if (!avatar.GameplayInventory.TryGetValue(providerName, out var inventory))
        {
            inventory = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            avatar.GameplayInventory[providerName] = inventory;
        }
        return inventory;
    }

    /// <summary>
    /// Adds items to the avatar's inventory for a specific provider.
    /// </summary>
    public static void AddItem(AvatarBase avatar, string providerName, string refName, int quantity = 1)
    {
        var inventory = GetOrCreateInventory(avatar, providerName);
        if (inventory.TryGetValue(refName, out var existing))
        {
            inventory[refName] = existing + quantity;
        }
        else
        {
            inventory[refName] = quantity;
        }
    }

    /// <summary>
    /// Removes items from the avatar's inventory for a specific provider.
    /// Returns the actual amount removed (may be less if not enough).
    /// </summary>
    public static int RemoveItem(AvatarBase avatar, string providerName, string refName, int quantity = 1)
    {
        var inventory = GetOrCreateInventory(avatar, providerName);
        if (!inventory.TryGetValue(refName, out var existing) || existing <= 0)
        {
            return 0;
        }

        var toRemove = Math.Min(quantity, existing);
        var remaining = existing - toRemove;

        if (remaining <= 0)
        {
            inventory.Remove(refName);
        }
        else
        {
            inventory[refName] = remaining;
        }

        return toRemove;
    }

    /// <summary>
    /// Gets the quantity of an item in the avatar's inventory.
    /// </summary>
    public static int GetQuantity(AvatarBase avatar, string providerName, string refName)
    {
        if (!avatar.GameplayInventory.TryGetValue(providerName, out var inventory))
            return 0;

        return inventory.TryGetValue(refName, out var quantity) ? quantity : 0;
    }

    /// <summary>
    /// Checks if the avatar has at least the specified quantity of an item.
    /// </summary>
    public static bool HasItem(AvatarBase avatar, string providerName, string refName, int quantity = 1)
    {
        return GetQuantity(avatar, providerName, refName) >= quantity;
    }

    /// <summary>
    /// Clears all inventory for a specific provider.
    /// </summary>
    public static void ClearInventory(AvatarBase avatar, string providerName)
    {
        if (avatar.GameplayInventory.TryGetValue(providerName, out var inventory))
        {
            inventory.Clear();
        }
    }

    /// <summary>
    /// Clears all gameplay inventory for all providers.
    /// </summary>
    public static void ClearAllInventory(AvatarBase avatar)
    {
        avatar.GameplayInventory.Clear();
    }

    /// <summary>
    /// Gets all owned items for a provider with their quantities.
    /// </summary>
    public static IEnumerable<(string RefName, int Quantity)> GetOwnedItems(AvatarBase avatar, string providerName)
    {
        if (!avatar.GameplayInventory.TryGetValue(providerName, out var inventory))
            yield break;

        foreach (var kvp in inventory.Where(x => x.Value > 0))
        {
            yield return (kvp.Key, kvp.Value);
        }
    }

    /// <summary>
    /// Gets all owned items for a provider with full item details.
    /// </summary>
    public static IEnumerable<(IGameplayItem Item, int Quantity)> GetOwnedItemsWithDetails(
        AvatarBase avatar,
        IGameplayItemProvider provider)
    {
        foreach (var (refName, quantity) in GetOwnedItems(avatar, provider.Name))
        {
            var item = provider.GetByRefName(refName);
            if (item != null)
            {
                yield return (item, quantity);
            }
        }
    }
}
