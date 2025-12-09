using ImGuiNET;
using System.Numerics;

namespace Ambient.Saga.Presentation.UI.Services;

/// <summary>
/// ImGui theming system for Ambient.Saga.
/// Call ApplyTheme() after creating the ImGui context.
/// </summary>
public static class ImGuiTheme
{
    public enum ThemePreset
    {
        DarkFantasy,
        ModernDark,
        Cyberpunk
    }

    public static void ApplyTheme(ThemePreset preset = ThemePreset.DarkFantasy)
    {
        var style = ImGui.GetStyle();
        ApplyCommonStyle(style);

        switch (preset)
        {
            case ThemePreset.DarkFantasy:
                ApplyDarkFantasyColors(style);
                break;
            case ThemePreset.ModernDark:
                ApplyModernDarkColors(style);
                break;
            case ThemePreset.Cyberpunk:
                ApplyCyberpunkColors(style);
                break;
        }
    }

    private static void ApplyCommonStyle(ImGuiStylePtr style)
    {
        // Rounding
        style.WindowRounding = 8.0f;
        style.ChildRounding = 6.0f;
        style.FrameRounding = 4.0f;
        style.PopupRounding = 6.0f;
        style.ScrollbarRounding = 12.0f;
        style.GrabRounding = 4.0f;
        style.TabRounding = 6.0f;

        // Borders
        style.WindowBorderSize = 1.0f;
        style.ChildBorderSize = 1.0f;
        style.PopupBorderSize = 1.0f;
        style.FrameBorderSize = 0.0f;

        // Padding
        style.WindowPadding = new Vector2(12, 12);
        style.FramePadding = new Vector2(8, 4);
        style.ItemSpacing = new Vector2(8, 6);
        style.ItemInnerSpacing = new Vector2(6, 4);
        style.ScrollbarSize = 14.0f;

        // Alignment
        style.WindowTitleAlign = new Vector2(0.5f, 0.5f);
        style.ButtonTextAlign = new Vector2(0.5f, 0.5f);

        style.AntiAliasedLines = true;
        style.AntiAliasedFill = true;
    }

    private static void ApplyDarkFantasyColors(ImGuiStylePtr style)
    {
        var colors = style.Colors;

        // Gold accent theme for RPG feel
        var accent = new Vector4(0.85f, 0.65f, 0.20f, 1.00f);
        var accentHover = new Vector4(0.95f, 0.75f, 0.30f, 1.00f);

        colors[(int)ImGuiCol.WindowBg] = new Vector4(0.08f, 0.08f, 0.10f, 0.97f);
        colors[(int)ImGuiCol.ChildBg] = new Vector4(0.12f, 0.12f, 0.14f, 0.50f);
        colors[(int)ImGuiCol.PopupBg] = new Vector4(0.08f, 0.08f, 0.10f, 0.98f);
        colors[(int)ImGuiCol.Border] = new Vector4(0.35f, 0.30f, 0.20f, 0.60f);
        colors[(int)ImGuiCol.FrameBg] = new Vector4(0.18f, 0.18f, 0.22f, 0.70f);
        colors[(int)ImGuiCol.FrameBgHovered] = new Vector4(0.25f, 0.23f, 0.20f, 0.80f);
        colors[(int)ImGuiCol.FrameBgActive] = new Vector4(0.30f, 0.27f, 0.22f, 0.90f);
        colors[(int)ImGuiCol.TitleBg] = new Vector4(0.08f, 0.08f, 0.10f, 1.00f);
        colors[(int)ImGuiCol.TitleBgActive] = new Vector4(0.15f, 0.13f, 0.10f, 1.00f);
        colors[(int)ImGuiCol.ScrollbarBg] = new Vector4(0.08f, 0.08f, 0.10f, 0.60f);
        colors[(int)ImGuiCol.ScrollbarGrab] = new Vector4(0.40f, 0.35f, 0.25f, 0.80f);
        colors[(int)ImGuiCol.ScrollbarGrabHovered] = new Vector4(0.50f, 0.43f, 0.30f, 0.90f);
        colors[(int)ImGuiCol.ScrollbarGrabActive] = accent;
        colors[(int)ImGuiCol.CheckMark] = accent;
        colors[(int)ImGuiCol.SliderGrab] = new Vector4(0.60f, 0.50f, 0.30f, 1.00f);
        colors[(int)ImGuiCol.SliderGrabActive] = accent;
        colors[(int)ImGuiCol.Button] = new Vector4(0.30f, 0.25f, 0.18f, 0.80f);
        colors[(int)ImGuiCol.ButtonHovered] = new Vector4(0.45f, 0.38f, 0.25f, 0.90f);
        colors[(int)ImGuiCol.ButtonActive] = accent;
        colors[(int)ImGuiCol.Header] = new Vector4(0.30f, 0.25f, 0.18f, 0.70f);
        colors[(int)ImGuiCol.HeaderHovered] = new Vector4(0.45f, 0.38f, 0.25f, 0.80f);
        colors[(int)ImGuiCol.HeaderActive] = accent;
        colors[(int)ImGuiCol.Separator] = new Vector4(0.40f, 0.35f, 0.25f, 0.50f);
        colors[(int)ImGuiCol.SeparatorHovered] = accentHover;
        colors[(int)ImGuiCol.Tab] = new Vector4(0.20f, 0.18f, 0.14f, 0.90f);
        colors[(int)ImGuiCol.TabHovered] = accentHover;
        colors[(int)ImGuiCol.TabSelected] = new Vector4(0.35f, 0.30f, 0.20f, 1.00f);
        colors[(int)ImGuiCol.TableHeaderBg] = new Vector4(0.20f, 0.18f, 0.14f, 1.00f);
        colors[(int)ImGuiCol.TableBorderStrong] = new Vector4(0.35f, 0.30f, 0.22f, 1.00f);
        colors[(int)ImGuiCol.TableBorderLight] = new Vector4(0.28f, 0.25f, 0.18f, 1.00f);
        colors[(int)ImGuiCol.Text] = new Vector4(0.92f, 0.90f, 0.85f, 1.00f);
        colors[(int)ImGuiCol.TextDisabled] = new Vector4(0.55f, 0.53f, 0.50f, 1.00f);
        colors[(int)ImGuiCol.ModalWindowDimBg] = new Vector4(0.00f, 0.00f, 0.00f, 0.65f);
        colors[(int)ImGuiCol.ResizeGrip] = new Vector4(0.40f, 0.35f, 0.25f, 0.30f);
        colors[(int)ImGuiCol.ResizeGripHovered] = accentHover;
        colors[(int)ImGuiCol.ResizeGripActive] = accent;
    }

    private static void ApplyModernDarkColors(ImGuiStylePtr style)
    {
        var colors = style.Colors;

        var accent = new Vector4(0.26f, 0.59f, 0.98f, 1.00f);
        var accentHover = new Vector4(0.40f, 0.70f, 1.00f, 1.00f);

        colors[(int)ImGuiCol.WindowBg] = new Vector4(0.10f, 0.10f, 0.12f, 0.98f);
        colors[(int)ImGuiCol.ChildBg] = new Vector4(0.14f, 0.14f, 0.16f, 0.40f);
        colors[(int)ImGuiCol.PopupBg] = new Vector4(0.08f, 0.08f, 0.10f, 0.98f);
        colors[(int)ImGuiCol.Border] = new Vector4(0.30f, 0.30f, 0.35f, 0.50f);
        colors[(int)ImGuiCol.FrameBg] = new Vector4(0.20f, 0.20f, 0.24f, 1.00f);
        colors[(int)ImGuiCol.FrameBgHovered] = new Vector4(0.25f, 0.25f, 0.30f, 1.00f);
        colors[(int)ImGuiCol.FrameBgActive] = new Vector4(0.30f, 0.30f, 0.36f, 1.00f);
        colors[(int)ImGuiCol.TitleBg] = new Vector4(0.10f, 0.10f, 0.12f, 1.00f);
        colors[(int)ImGuiCol.TitleBgActive] = new Vector4(0.08f, 0.08f, 0.10f, 1.00f);
        colors[(int)ImGuiCol.ScrollbarBg] = new Vector4(0.10f, 0.10f, 0.12f, 0.60f);
        colors[(int)ImGuiCol.ScrollbarGrab] = new Vector4(0.35f, 0.35f, 0.40f, 0.80f);
        colors[(int)ImGuiCol.ScrollbarGrabHovered] = new Vector4(0.45f, 0.45f, 0.50f, 0.90f);
        colors[(int)ImGuiCol.ScrollbarGrabActive] = accent;
        colors[(int)ImGuiCol.CheckMark] = accent;
        colors[(int)ImGuiCol.SliderGrab] = new Vector4(0.45f, 0.55f, 0.75f, 1.00f);
        colors[(int)ImGuiCol.SliderGrabActive] = accent;
        colors[(int)ImGuiCol.Button] = new Vector4(0.22f, 0.22f, 0.28f, 1.00f);
        colors[(int)ImGuiCol.ButtonHovered] = new Vector4(0.30f, 0.40f, 0.60f, 1.00f);
        colors[(int)ImGuiCol.ButtonActive] = accent;
        colors[(int)ImGuiCol.Header] = new Vector4(0.22f, 0.22f, 0.28f, 1.00f);
        colors[(int)ImGuiCol.HeaderHovered] = new Vector4(0.30f, 0.40f, 0.55f, 1.00f);
        colors[(int)ImGuiCol.HeaderActive] = accent;
        colors[(int)ImGuiCol.Separator] = new Vector4(0.35f, 0.35f, 0.42f, 0.50f);
        colors[(int)ImGuiCol.Tab] = new Vector4(0.16f, 0.16f, 0.20f, 1.00f);
        colors[(int)ImGuiCol.TabHovered] = accentHover;
        colors[(int)ImGuiCol.TabSelected] = new Vector4(0.22f, 0.30f, 0.45f, 1.00f);
        colors[(int)ImGuiCol.TableHeaderBg] = new Vector4(0.18f, 0.18f, 0.22f, 1.00f);
        colors[(int)ImGuiCol.TableBorderStrong] = new Vector4(0.30f, 0.30f, 0.36f, 1.00f);
        colors[(int)ImGuiCol.Text] = new Vector4(0.90f, 0.90f, 0.92f, 1.00f);
        colors[(int)ImGuiCol.TextDisabled] = new Vector4(0.50f, 0.50f, 0.55f, 1.00f);
        colors[(int)ImGuiCol.ModalWindowDimBg] = new Vector4(0.00f, 0.00f, 0.00f, 0.60f);
    }

    private static void ApplyCyberpunkColors(ImGuiStylePtr style)
    {
        var colors = style.Colors;

        var cyan = new Vector4(0.00f, 0.90f, 0.90f, 1.00f);
        var magenta = new Vector4(0.90f, 0.20f, 0.60f, 1.00f);

        colors[(int)ImGuiCol.WindowBg] = new Vector4(0.06f, 0.06f, 0.10f, 0.96f);
        colors[(int)ImGuiCol.ChildBg] = new Vector4(0.10f, 0.10f, 0.15f, 0.50f);
        colors[(int)ImGuiCol.PopupBg] = new Vector4(0.04f, 0.04f, 0.08f, 0.98f);
        colors[(int)ImGuiCol.Border] = new Vector4(0.00f, 0.50f, 0.55f, 0.50f);
        colors[(int)ImGuiCol.FrameBg] = new Vector4(0.15f, 0.15f, 0.22f, 1.00f);
        colors[(int)ImGuiCol.FrameBgHovered] = new Vector4(0.18f, 0.20f, 0.30f, 1.00f);
        colors[(int)ImGuiCol.FrameBgActive] = new Vector4(0.20f, 0.25f, 0.38f, 1.00f);
        colors[(int)ImGuiCol.TitleBg] = new Vector4(0.06f, 0.06f, 0.10f, 1.00f);
        colors[(int)ImGuiCol.TitleBgActive] = new Vector4(0.08f, 0.10f, 0.18f, 1.00f);
        colors[(int)ImGuiCol.ScrollbarBg] = new Vector4(0.06f, 0.06f, 0.10f, 0.60f);
        colors[(int)ImGuiCol.ScrollbarGrab] = new Vector4(0.20f, 0.35f, 0.45f, 0.80f);
        colors[(int)ImGuiCol.ScrollbarGrabHovered] = new Vector4(0.00f, 0.60f, 0.65f, 0.90f);
        colors[(int)ImGuiCol.ScrollbarGrabActive] = cyan;
        colors[(int)ImGuiCol.CheckMark] = magenta;
        colors[(int)ImGuiCol.SliderGrab] = new Vector4(0.00f, 0.65f, 0.70f, 1.00f);
        colors[(int)ImGuiCol.SliderGrabActive] = cyan;
        colors[(int)ImGuiCol.Button] = new Vector4(0.12f, 0.20f, 0.28f, 1.00f);
        colors[(int)ImGuiCol.ButtonHovered] = new Vector4(0.00f, 0.45f, 0.50f, 1.00f);
        colors[(int)ImGuiCol.ButtonActive] = cyan;
        colors[(int)ImGuiCol.Header] = new Vector4(0.35f, 0.12f, 0.28f, 0.80f);
        colors[(int)ImGuiCol.HeaderHovered] = new Vector4(0.55f, 0.18f, 0.40f, 0.90f);
        colors[(int)ImGuiCol.HeaderActive] = magenta;
        colors[(int)ImGuiCol.Separator] = new Vector4(0.00f, 0.45f, 0.50f, 0.50f);
        colors[(int)ImGuiCol.Tab] = new Vector4(0.10f, 0.12f, 0.18f, 1.00f);
        colors[(int)ImGuiCol.TabHovered] = cyan;
        colors[(int)ImGuiCol.TabSelected] = new Vector4(0.00f, 0.35f, 0.40f, 1.00f);
        colors[(int)ImGuiCol.TableHeaderBg] = new Vector4(0.12f, 0.14f, 0.22f, 1.00f);
        colors[(int)ImGuiCol.TableBorderStrong] = new Vector4(0.00f, 0.40f, 0.45f, 1.00f);
        colors[(int)ImGuiCol.Text] = new Vector4(0.90f, 0.92f, 0.95f, 1.00f);
        colors[(int)ImGuiCol.TextDisabled] = new Vector4(0.45f, 0.48f, 0.55f, 1.00f);
        colors[(int)ImGuiCol.ModalWindowDimBg] = new Vector4(0.02f, 0.02f, 0.05f, 0.70f);
    }
}
