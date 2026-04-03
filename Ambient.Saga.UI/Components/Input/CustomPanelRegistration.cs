using ImGuiNET;

namespace Ambient.Saga.UI.Components.Input;

/// <summary>
/// Registration for a custom panel key binding.
/// Games register these to add their own hotkey-activated panels
/// alongside the built-in M/C/I/J panels.
/// </summary>
public class CustomPanelRegistration
{
    /// <summary>
    /// ImGui key that activates this panel (e.g. ImGuiKey.F for Social).
    /// </summary>
    public required ImGuiKey Key { get; init; }

    /// <summary>
    /// Display label for the key hint (e.g. "F" or "Tab").
    /// </summary>
    public required string KeyLabel { get; init; }

    /// <summary>
    /// Called when the key is pressed. The game toggles its panel open/closed.
    /// </summary>
    public required Action OnToggle { get; init; }

    /// <summary>
    /// Returns whether this custom panel is currently active (for hint highlighting).
    /// </summary>
    public required Func<bool> IsActive { get; init; }
}
