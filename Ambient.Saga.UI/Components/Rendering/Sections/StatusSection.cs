using ImGuiNET;
using System.Numerics;

namespace Ambient.Saga.UI.Components.Rendering.Sections;

/// <summary>
/// Center HUD section that displays survival essentials and hotkey hints.
/// - Resource bars (HP/Stamina/Mana)
/// - Temperature warning (only when abnormal)
/// - Panel hotkey hints
/// </summary>
public class StatusSection : IHudSection
{
    // Temperature thresholds (body temp in Celsius - 37 is normal)
    private const float NormalTemperature = 37f;
    private const float ColdThreshold = 35f;   // Hypothermia warning
    private const float HotThreshold = 39f;    // Hyperthermia warning

    // Resource bar styling
    private const float BarWidth = 100f;
    private const float BarHeight = 14f;
    private const float BarSpacing = 6f;

    public HudRegion Region => HudRegion.Center;
    public int Priority => 0;

    public void Render(HudContext context)
    {
        // Resource bars first (if avatar exists)
        RenderResourceBars(context);

        ImGui.SameLine(0, 20);

        // Temperature warning (only if abnormal)
        RenderTemperatureWarning(context);

        ImGui.SameLine(0, 20);

        // Hotkey hints
        RenderHotkeyHints(context);
    }

    private void RenderResourceBars(HudContext context)
    {
        var stats = context.ViewModel.PlayerAvatar?.Stats;
        if (stats == null)
            return;

        // Health bar (red)
        RenderResourceBar("HP", stats.Health, 1.0f,
            new Vector4(0.8f, 0.2f, 0.2f, 1f),
            new Vector4(0.3f, 0.1f, 0.1f, 1f));
        ImGui.SameLine(0, BarSpacing);

        // Stamina bar (green)
        RenderResourceBar("ST", stats.Stamina, 1.0f,
            new Vector4(0.2f, 0.7f, 0.3f, 1f),
            new Vector4(0.1f, 0.25f, 0.1f, 1f));
        ImGui.SameLine(0, BarSpacing);

        // Mana bar (blue)
        RenderResourceBar("MP", stats.Mana, 1.0f,
            new Vector4(0.3f, 0.4f, 0.9f, 1f),
            new Vector4(0.1f, 0.15f, 0.35f, 1f));
    }

    private void RenderResourceBar(string label, float current, float max, Vector4 fillColor, Vector4 bgColor)
    {
        var pos = ImGui.GetCursorScreenPos();
        var drawList = ImGui.GetWindowDrawList();

        var fraction = Math.Clamp(current / max, 0f, 1f);

        // Background
        drawList.AddRectFilled(pos, new Vector2(pos.X + BarWidth, pos.Y + BarHeight),
            ImGui.ColorConvertFloat4ToU32(bgColor), 3f);

        // Fill
        if (fraction > 0)
        {
            drawList.AddRectFilled(pos, new Vector2(pos.X + BarWidth * fraction, pos.Y + BarHeight),
                ImGui.ColorConvertFloat4ToU32(fillColor), 3f);
        }

        // Border
        drawList.AddRect(pos, new Vector2(pos.X + BarWidth, pos.Y + BarHeight),
            ImGui.ColorConvertFloat4ToU32(new Vector4(0.4f, 0.4f, 0.4f, 0.8f)), 3f);

        // Label centered in bar
        var labelText = $"{label} {(int)(fraction * 100)}%";
        var textSize = ImGui.CalcTextSize(labelText);
        var textPos = new Vector2(
            pos.X + (BarWidth - textSize.X) / 2,
            pos.Y + (BarHeight - textSize.Y) / 2);
        drawList.AddText(textPos, ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 0.9f)), labelText);

        // Advance cursor
        ImGui.Dummy(new Vector2(BarWidth, BarHeight));
    }

    private void RenderTemperatureWarning(HudContext context)
    {
        var stats = context.ViewModel.PlayerAvatar?.Stats;
        if (stats == null)
            return;

        var temp = stats.Temperature;
        if (temp >= ColdThreshold && temp <= HotThreshold)
            return; // Normal - no warning

        var (text, color) = temp < ColdThreshold
            ? ("COLD", new Vector4(0.4f, 0.7f, 1f, 1f))
            : ("HOT", new Vector4(1f, 0.5f, 0.2f, 1f));

        // Flashing effect
        var flash = (float)Math.Sin(ImGui.GetTime() * 4) * 0.3f + 0.7f;
        var flashedColor = color with { W = flash };

        ImGui.TextColored(flashedColor, text);
    }

    private void RenderHotkeyHints(HudContext context)
    {
        var separatorColor = new Vector4(0.3f, 0.3f, 0.3f, 1f);

        // Only show Map hint if world has a height map
        if (context.HasMap)
        {
            RenderHotkeyHint("M", "Map", context.ActivePanel == ActivePanel.Map);
            ImGui.SameLine();
            ImGui.TextColored(separatorColor, "|");
            ImGui.SameLine();
        }

        RenderHotkeyHint("C", "Character", context.ActivePanel == ActivePanel.Character);
        ImGui.SameLine();
        ImGui.TextColored(separatorColor, "|");
        ImGui.SameLine();

        RenderHotkeyHint("I", "Inventory", context.ActivePanel == ActivePanel.Inventory);
        ImGui.SameLine();
        ImGui.TextColored(separatorColor, "|");
        ImGui.SameLine();

        RenderHotkeyHint("J", "Journal", context.ActivePanel == ActivePanel.Journal);
    }

    private void RenderHotkeyHint(string key, string label, bool isActive)
    {
        Vector4 keyColor = isActive
            ? new Vector4(0.3f, 0.7f, 0.3f, 1f)
            : new Vector4(0.3f, 0.3f, 0.3f, 1f);
        Vector4 textColor = isActive
            ? new Vector4(1f, 1f, 1f, 1f)
            : new Vector4(0.6f, 0.6f, 0.6f, 1f);

        ImGui.PushStyleColor(ImGuiCol.Button, keyColor);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, keyColor);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, keyColor);

        var textSize = ImGui.CalcTextSize(key);
        var style = ImGui.GetStyle();
        var buttonWidth = textSize.X + style.FramePadding.X * 2;
        var buttonHeight = textSize.Y + style.FramePadding.Y * 2;
        ImGui.Button(key, new Vector2(buttonWidth, buttonHeight));

        ImGui.PopStyleColor(3);

        ImGui.SameLine();
        ImGui.TextColored(textColor, label);
    }
}
