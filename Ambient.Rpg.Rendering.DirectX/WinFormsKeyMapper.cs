using System.Windows.Forms;
using ImGuiNET;

namespace Ambient.Rpg.Rendering.DirectX;

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

        // Panel hotkeys (M=Map, C=Character, I=Inventory, J=Journal, F=Social)
        Keys.F => ImGuiKey.F,
        Keys.M => ImGuiKey.M,
        Keys.I => ImGuiKey.I,
        Keys.J => ImGuiKey.J,

        // Function keys (F1=World Info, F2=Toggle Dev Info, F12=Dev Tools)
        Keys.F1 => ImGuiKey.F1,
        Keys.F2 => ImGuiKey.F2,
        Keys.F12 => ImGuiKey.F12,

        // Number keys 1-9 (hotbar slots)
        Keys.D1 => ImGuiKey._1,
        Keys.D2 => ImGuiKey._2,
        Keys.D3 => ImGuiKey._3,
        Keys.D4 => ImGuiKey._4,
        Keys.D5 => ImGuiKey._5,
        Keys.D6 => ImGuiKey._6,
        Keys.D7 => ImGuiKey._7,
        Keys.D8 => ImGuiKey._8,
        Keys.D9 => ImGuiKey._9,

        // Numpad keys 1-9 (also hotbar slots)
        Keys.NumPad1 => ImGuiKey.Keypad1,
        Keys.NumPad2 => ImGuiKey.Keypad2,
        Keys.NumPad3 => ImGuiKey.Keypad3,
        Keys.NumPad4 => ImGuiKey.Keypad4,
        Keys.NumPad5 => ImGuiKey.Keypad5,
        Keys.NumPad6 => ImGuiKey.Keypad6,
        Keys.NumPad7 => ImGuiKey.Keypad7,
        Keys.NumPad8 => ImGuiKey.Keypad8,
        Keys.NumPad9 => ImGuiKey.Keypad9,

        _ => ImGuiKey.None
    };
}
