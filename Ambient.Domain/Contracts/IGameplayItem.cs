namespace Ambient.Domain.Contracts;

/// <summary>
/// Interface for game-specific items that can be displayed in inventory, traded, and placed on hotbar.
/// This is the abstract contract for gameplay elements - the game defines what categories exist
/// (e.g., "Block", "Tool", "Seed", "Vehicle").
/// </summary>
public interface IGameplayItem : ITradeable
{
    /// <summary>
    /// Game-defined category for this item (e.g., "Block", "Tool", "Material").
    /// Used for grouping in UI and filtering.
    /// </summary>
    string Category { get; }

    /// <summary>
    /// Optional description for tooltips and detail views.
    /// </summary>
    string? Description { get; }

    /// <summary>
    /// Optional texture reference for displaying item image in UI.
    /// </summary>
    string? TextureRef { get; }
}
