using System.Numerics;

namespace Ambient.Saga.UI.Components.Rendering;

/// <summary>
/// Defines the region where a HUD section should be rendered.
/// </summary>
public enum HudRegion
{
    /// <summary>Left side of the HUD bar</summary>
    Left,
    /// <summary>Center of the HUD bar</summary>
    Center,
    /// <summary>Right side of the HUD bar</summary>
    Right
}

/// <summary>
/// Interface for a modular HUD section that can be composed into a HUD bar.
/// Implement this to create custom HUD elements (tool displays, health bars, etc.)
/// </summary>
public interface IHudSection
{
    /// <summary>
    /// The region where this section should be rendered.
    /// </summary>
    HudRegion Region { get; }

    /// <summary>
    /// Priority for ordering within the same region (lower = rendered first/leftmost).
    /// </summary>
    int Priority { get; }

    /// <summary>
    /// Render the section content.
    /// </summary>
    /// <param name="context">Context providing access to view model and display info</param>
    void Render(HudContext context);
}
