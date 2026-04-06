using ImGuiNET;

namespace Ambient.Saga.UI.Components.Panels;

/// <summary>
/// Interface for panels in the panel system.
/// Panels are hotkey-activated, toggle on/off, and only one is active at a time.
/// Same pattern as IModal but for persistent, toggleable UI panels.
/// </summary>
public interface IPanel
{
    /// <summary>
    /// Unique name for this panel.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// ImGui key that toggles this panel (e.g. ImGuiKey.F for Social).
    /// </summary>
    ImGuiKey Key { get; }

    /// <summary>
    /// Display label for the key hint in the HUD (e.g. "F").
    /// </summary>
    string KeyLabel { get; }

    /// <summary>
    /// Whether this panel is currently available. Unavailable panels are hidden from the HUD.
    /// </summary>
    bool IsAvailable => true;

    /// <summary>
    /// Called when the panel is about to open.
    /// </summary>
    void OnOpening() { }

    /// <summary>
    /// Render the panel. Set isOpen to false to close.
    /// </summary>
    void Render(object? context, ref bool isOpen);

    /// <summary>
    /// Called when the panel has been closed.
    /// </summary>
    void OnClosed() { }
}
