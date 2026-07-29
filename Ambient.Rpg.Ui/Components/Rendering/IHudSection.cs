using System.Numerics;

namespace Ambient.Rpg.Ui.Components.Rendering;

/// <summary>
/// Defines the region where a HUD section should be rendered.
/// Layout:
/// ┌─────────────────────────────────────────────────────────────┐
/// │ [TopLeft]                                      [TopRight]   │
/// │  Status effects,                               World info,  │
/// │  body temp icon                                time/weather │
/// │                                                             │
/// │                        3D WORLD                             │
/// │                                                             │
/// ├─────────────────────────────────────────────────────────────┤
/// │ [BottomLeft] │    [BottomCenter]      │ [BottomRight]       │
/// │  HP/Stamina  │   Blocks hotbar (ext)  │  Interaction hints  │
/// └─────────────────────────────────────────────────────────────┘
/// </summary>
public enum HudRegion
{
    /// <summary>Top-left corner overlay - status effects, debuffs, body temp</summary>
    TopLeft,
    /// <summary>Top-right corner overlay - world info, time, weather, minimap</summary>
    TopRight,
    /// <summary>Bottom bar left - resource bars (HP/Stamina/Mana)</summary>
    BottomLeft,
    /// <summary>Bottom bar center - extension slot for blocks/tools (the host application owns this)</summary>
    BottomCenter,
    /// <summary>Bottom bar right - interaction hints, context actions</summary>
    BottomRight
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
