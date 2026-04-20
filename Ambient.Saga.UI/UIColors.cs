using System.Numerics;

namespace Ambient.Saga.UI;

/// <summary>
/// Shared ImGui color constants for buttons, panel backgrounds, and window chrome.
/// Use with ImGui.PushStyleColor(...) to keep modal styling consistent across games.
/// </summary>
public static class UIColors
{
    // Accept / confirm (green) — 3-tone ramp
    public static readonly Vector4 ButtonAccept = new(0.2f, 0.4f, 0.2f, 1f);
    public static readonly Vector4 ButtonAcceptHovered = new(0.3f, 0.55f, 0.3f, 1f);
    public static readonly Vector4 ButtonAcceptActive = new(0.4f, 0.7f, 0.4f, 1f);

    // Danger / destructive (red) — 3-tone ramp
    public static readonly Vector4 ButtonDanger = new(0.4f, 0.15f, 0.15f, 1f);
    public static readonly Vector4 ButtonDangerHovered = new(0.5f, 0.2f, 0.2f, 1f);
    public static readonly Vector4 ButtonDangerActive = new(0.6f, 0.25f, 0.25f, 1f);

    // Info / secondary (blue) — 3-tone ramp
    public static readonly Vector4 ButtonInfo = new(0.2f, 0.35f, 0.4f, 1f);
    public static readonly Vector4 ButtonInfoHovered = new(0.25f, 0.45f, 0.55f, 1f);
    public static readonly Vector4 ButtonInfoActive = new(0.3f, 0.55f, 0.7f, 1f);

    // Warning (orange) — 2-tone
    public static readonly Vector4 ButtonWarning = new(0.5f, 0.3f, 0.2f, 1f);
    public static readonly Vector4 ButtonWarningHovered = new(0.6f, 0.4f, 0.3f, 1f);

    // Neutral (gray) — 3-tone ramp
    public static readonly Vector4 ButtonNeutral = new(0.30f, 0.30f, 0.30f, 1f);
    public static readonly Vector4 ButtonNeutralHovered = new(0.38f, 0.38f, 0.38f, 1f);
    public static readonly Vector4 ButtonNeutralActive = new(0.45f, 0.45f, 0.45f, 1f);

    // Panel / child backgrounds
    public static readonly Vector4 PanelBgDark = new(0.05f, 0.05f, 0.08f, 0.9f);
    public static readonly Vector4 PanelBgMid = new(0.12f, 0.12f, 0.15f, 0.9f);

    // Window background (used by top-level modal/panel windows)
    public static readonly Vector4 WindowBg = new(0.08f, 0.08f, 0.12f, 0.95f);
}
