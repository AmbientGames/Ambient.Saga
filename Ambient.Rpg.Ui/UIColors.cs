using System.Numerics;

namespace Ambient.Rpg.Ui;

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

    // Warning (orange) — 3-tone ramp
    public static readonly Vector4 ButtonWarning = new(0.5f, 0.3f, 0.2f, 1f);
    public static readonly Vector4 ButtonWarningHovered = new(0.6f, 0.4f, 0.3f, 1f);
    public static readonly Vector4 ButtonWarningActive = new(0.7f, 0.5f, 0.4f, 1f);

    // Affinity / spirit (teal) — 3-tone ramp
    public static readonly Vector4 ButtonAffinity = new(0.15f, 0.35f, 0.35f, 1f);
    public static readonly Vector4 ButtonAffinityHovered = new(0.2f, 0.5f, 0.5f, 1f);
    public static readonly Vector4 ButtonAffinityActive = new(0.25f, 0.65f, 0.65f, 1f);

    // Neutral (gray) — 3-tone ramp
    public static readonly Vector4 ButtonNeutral = new(0.30f, 0.30f, 0.30f, 1f);
    public static readonly Vector4 ButtonNeutralHovered = new(0.38f, 0.38f, 0.38f, 1f);
    public static readonly Vector4 ButtonNeutralActive = new(0.45f, 0.45f, 0.45f, 1f);

    // Panel / child backgrounds
    public static readonly Vector4 PanelBgDark = new(0.05f, 0.05f, 0.08f, 0.9f);
    public static readonly Vector4 PanelBgMid = new(0.12f, 0.12f, 0.15f, 0.9f);

    // Window background (used by top-level modal/panel windows)
    public static readonly Vector4 WindowBg = new(0.08f, 0.08f, 0.12f, 0.95f);

    // Text roles — use with ImGui.TextColored. Values match what the panels/modals
    // historically hardcoded inline; keep the palette here, not at call sites.
    public static readonly Vector4 TextMuted = new(0.7f, 0.7f, 0.7f, 1f);      // secondary text, descriptions
    public static readonly Vector4 TextDim = new(0.6f, 0.6f, 0.6f, 1f);        // hints, fine print
    public static readonly Vector4 TextDisabled = new(0.5f, 0.5f, 0.5f, 1f);   // unavailable / inactive
    public static readonly Vector4 TextSuccess = new(0.5f, 1f, 0.5f, 1f);      // positive values, confirmations
    public static readonly Vector4 TextInfo = new(0.5f, 0.8f, 1f, 1f);         // informational accents
    public static readonly Vector4 TextHighlight = new(0.8f, 0.8f, 1f, 1f);    // sub-headers, emphasized labels
    public static readonly Vector4 TextWarning = new(1f, 0.8f, 0.5f, 1f);      // cautions, requirements
    public static readonly Vector4 TextDanger = new(1f, 0.5f, 0.5f, 1f);       // negative values, threats
    public static readonly Vector4 TextError = new(1f, 0.3f, 0.3f, 1f);        // errors, critical warnings
    public static readonly Vector4 Gold = new(1f, 0.843f, 0f, 1f);             // currency, rewards, titles
    public static readonly Vector4 GoldenYellow = new(1f, 0.9f, 0.4f, 1f);     // turn prompts, flee/escape accents
    public static readonly Vector4 TextSuccessBright = new(0.3f, 1f, 0.3f, 1f); // victory, [COMPLETED], unlocks
    public static readonly Vector4 TextTitleGreen = new(0.4f, 0.9f, 0.4f, 1f);  // friendly character-name titles

    // HUD resource bars (shared by DefaultHudRenderer and ResourceBarsSection)
    public static readonly Vector4 BarHealth = new(0.8f, 0.2f, 0.2f, 1f);
    public static readonly Vector4 BarHealthBg = new(0.3f, 0.1f, 0.1f, 1f);
    public static readonly Vector4 BarStamina = new(0.2f, 0.7f, 0.3f, 1f);
    public static readonly Vector4 BarStaminaBg = new(0.1f, 0.25f, 0.1f, 1f);
    public static readonly Vector4 BarMana = new(0.3f, 0.4f, 0.9f, 1f);
    public static readonly Vector4 BarManaBg = new(0.1f, 0.15f, 0.35f, 1f);
    public static readonly Vector4 BarBorder = new(0.4f, 0.4f, 0.4f, 0.8f);
    public static readonly Vector4 BarText = new(1f, 1f, 1f, 0.9f);

    // HUD hotkey badges (canonical values = the sectioned HUD's; both renderers use these)
    public static readonly Vector4 HotkeyActiveBg = new(0.2f, 0.5f, 0.25f, 0.9f);
    public static readonly Vector4 HotkeyInactiveBg = new(0.2f, 0.2f, 0.22f, 0.7f);
    public static readonly Vector4 HotkeyActiveText = new(1f, 1f, 1f, 1f);
    public static readonly Vector4 HotkeyDevActiveBg = new(0.5f, 0.35f, 0.15f, 0.9f);
    public static readonly Vector4 HotkeyDevInactiveBg = new(0.25f, 0.2f, 0.1f, 0.7f);
    public static readonly Vector4 HotkeyDevActiveText = new(1f, 0.9f, 0.7f, 1f);
    public static readonly Vector4 HotkeyDevInactiveText = new(0.6f, 0.5f, 0.3f, 1f);
}
