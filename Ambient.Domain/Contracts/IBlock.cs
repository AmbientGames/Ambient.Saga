namespace Ambient.Domain.Contracts;

/// <summary>
/// Interface for block types that can be traded.
/// Implemented by block classes in game-specific domain projects.
/// Extends ITradeable with block-specific properties for UI display.
/// </summary>
public interface IBlock : ITradeable
{
    /// <summary>
    /// Optional description for tooltips and detail views.
    /// </summary>
    string? Description { get; }

    /// <summary>
    /// Optional texture reference for displaying block image in UI.
    /// </summary>
    string? TextureRef { get; }

    /// <summary>
    /// Optional substance type (e.g., "Stone", "Wood", "Metal") for categorization.
    /// </summary>
    string? SubstanceRef { get; }
}
