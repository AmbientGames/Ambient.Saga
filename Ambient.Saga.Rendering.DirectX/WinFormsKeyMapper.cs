using System.Windows.Forms;
using ImGuiNET;

namespace Ambient.Saga.Rendering.DirectX;

/// <summary>
/// Maps Windows Forms Keys to ImGui keys.
/// Shared by all WinForms-based DirectX applications using ImGui.
/// </summary>
public static class WinFormsKeyMapper
{
    /// <summary>
    /// Maps a Windows Forms key to the corresponding ImGui key.
    /// Returns ImGuiKey.None for unmapped keys.
    /// </summary>
    public static ImGuiKey MapKeyToImGui(Keys key) => key switch
    {
        // Navigation keys
        Keys.Tab => ImGuiKey.Tab,
        Keys.Left => ImGuiKey.LeftArrow,
        Keys.Right => ImGuiKey.RightArrow,
        Keys.Up => ImGuiKey.UpArrow,
        Keys.Down => ImGuiKey.DownArrow,
        Keys.PageUp => ImGuiKey.PageUp,
        Keys.PageDown => ImGuiKey.PageDown,
        Keys.Home => ImGuiKey.Home,
        Keys.End => ImGuiKey.End,
        Keys.Insert => ImGuiKey.Insert,
        Keys.Delete => ImGuiKey.Delete,
        Keys.Back => ImGuiKey.Backspace,
        Keys.Space => ImGuiKey.Space,
        Keys.Enter => ImGuiKey.Enter,
        Keys.Escape => ImGuiKey.Escape,

        // Text editing keys (for clipboard operations)
        Keys.A => ImGuiKey.A,
        Keys.C => ImGuiKey.C,
        Keys.V => ImGuiKey.V,
        Keys.X => ImGuiKey.X,
        Keys.Y => ImGuiKey.Y,
        Keys.Z => ImGuiKey.Z,

        // Panel hotkeys (M=Map, C=Character, I=Inventory, J=Journal)
        Keys.M => ImGuiKey.M,
        Keys.I => ImGuiKey.I,
        Keys.J => ImGuiKey.J,

        // Function keys (F1=World Info, F2=Toggle Dev Info, F12=Dev Tools)
        Keys.F1 => ImGuiKey.F1,
        Keys.F2 => ImGuiKey.F2,
        Keys.F12 => ImGuiKey.F12,

        _ => ImGuiKey.None
    };
}
