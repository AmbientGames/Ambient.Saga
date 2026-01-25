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
    /// Used in UI headers.
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
}
