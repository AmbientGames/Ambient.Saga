namespace Ambient.Domain.Contracts;

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
}
