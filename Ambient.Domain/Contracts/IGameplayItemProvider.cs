namespace Ambient.Domain.Contracts;

/// <summary>
/// Represents a starting item with quantity for spawn/respawn.
/// </summary>
public record StartingItem(string RefName, int Quantity = 1);

/// <summary>
/// Provides gameplay item catalog and lookup functionality.
/// Implemented by game-specific projects to provide their item types.
///
/// This is the extensibility point for games to plug in their own item systems
/// (blocks, tools, seeds, vehicles, etc.) without the RPG system needing to know
/// the specific types.
/// </summary>
public interface IGameplayItemProvider
{
    /// <summary>
    /// Display name for this provider (e.g., "Blocks", "Seeds", "Vehicles").
    /// Used in UI headers and for Provider attribute matching in dialogue conditions/actions.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// The currently selected item's RefName, or null if none selected.
    /// Each provider tracks its own selection independently.
    /// </summary>
    string? CurrentItemRef { get; set; }

    /// <summary>
    /// Gets the currently selected item, or null if none selected.
    /// </summary>
    IGameplayItem? CurrentItem { get; }

    /// <summary>
    /// Gets all available items in the catalog.
    /// </summary>
    IEnumerable<IGameplayItem> GetAll();

    /// <summary>
    /// Gets all items in a specific category.
    /// </summary>
    /// <param name="category">The category to filter by.</param>
    IEnumerable<IGameplayItem> GetByCategory(string category);

    /// <summary>
    /// Looks up an item by its reference name.
    /// </summary>
    /// <param name="refName">The reference name of the item to find.</param>
    /// <returns>The item if found, null otherwise.</returns>
    IGameplayItem? GetByRefName(string refName);

    /// <summary>
    /// Gets all categories that have items.
    /// Used by UI to dynamically build category tabs/sections.
    /// </summary>
    IEnumerable<string> GetCategories();

    /// <summary>
    /// Gets the starting items for initial spawn.
    /// Can vary by archetype if needed.
    /// </summary>
    /// <param name="archetypeRef">Optional archetype reference for conditional loadouts.</param>
    /// <returns>Items with quantities to grant on spawn.</returns>
    IEnumerable<StartingItem> GetSpawnItems(string? archetypeRef = null);

    /// <summary>
    /// Gets the items for respawn (after death).
    /// Defaults to same as spawn if not overridden.
    /// Can vary by archetype if needed.
    /// </summary>
    /// <param name="archetypeRef">Optional archetype reference for conditional loadouts.</param>
    /// <returns>Items with quantities to grant on respawn.</returns>
    IEnumerable<StartingItem> GetRespawnItems(string? archetypeRef = null);

    /// <summary>
    /// Whether to clear existing inventory on respawn before applying respawn items.
    /// Default is true (start fresh on death).
    /// </summary>
    bool ClearOnRespawn { get; }

    // ===== AVATAR INVENTORY MANAGEMENT =====
    // These methods allow the dialogue system to manage items through the provider.

    /// <summary>
    /// Gets the quantity of a specific item in the avatar's inventory.
    /// For presence-based items, returns 1 if present, 0 if not.
    /// </summary>
    /// <param name="avatar">The avatar to check.</param>
    /// <param name="refName">The item reference name.</param>
    /// <returns>Quantity of the item (0 if not present).</returns>
    int GetAvatarItemQuantity(AvatarBase avatar, string refName);

    /// <summary>
    /// Gives an item to the avatar's inventory.
    /// For quantity-based items, adds to quantity.
    /// For presence-based items, adds if not already present.
    /// </summary>
    /// <param name="avatar">The avatar to give the item to.</param>
    /// <param name="refName">The item reference name.</param>
    /// <param name="quantity">Quantity to give (default 1).</param>
    void GiveAvatarItem(AvatarBase avatar, string refName, int quantity = 1);

    /// <summary>
    /// Takes an item from the avatar's inventory.
    /// For quantity-based items, reduces quantity.
    /// For presence-based items, removes if present.
    /// </summary>
    /// <param name="avatar">The avatar to take the item from.</param>
    /// <param name="refName">The item reference name.</param>
    /// <param name="quantity">Quantity to take (default 1).</param>
    void TakeAvatarItem(AvatarBase avatar, string refName, int quantity = 1);
}
