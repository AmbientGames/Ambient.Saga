using ImGuiNET;
using System.Numerics;

namespace Ambient.Saga.UI.Components.Rendering.Sections;

/// <summary>
/// Top-left HUD section displaying status effects and body temperature.
/// Shows debuffs, buffs, and environmental warnings as icons/states.
/// </summary>
public class StatusEffectsSection : IHudSection
{
    // Temperature thresholds (body temp in Celsius - 37 is normal)
    private const float NormalTemperature = 37f;
    private const float ColdThreshold = 35f;   // Hypothermia warning
    private const float HotThreshold = 39f;    // Hyperthermia warning

    public HudRegion Region => HudRegion.TopLeft;
    public int Priority => 0;

    public void Render(HudContext context)
    {
        var stats = context.ViewModel.PlayerAvatar?.Stats;
        if (stats == null)
            return;

        var drawList = ImGui.GetWindowDrawList();
        var windowPos = ImGui.GetWindowPos();
        var currentY = windowPos.Y;

        // Body temperature indicator
        RenderTemperatureStatus(drawList, windowPos.X, currentY, stats.Temperature);
        currentY += 20f;

        // Status effects (debuffs/buffs) - placeholder for future expansion
        // When status effects are implemented, they would render here as icons
        // Example: bleeding, poisoned, burning, frozen, buffed, etc.
        //
        // World/environment info (HudTextLeft/Right) renders in TopRight via WorldInfoSection
    }

    private void RenderTemperatureStatus(ImDrawListPtr drawList, float x, float y, float bodyTemp)
    {
        // Determine temperature state
        string statusText;
        Vector4 statusColor;

        if (bodyTemp < ColdThreshold)
        {
            statusText = "[COLD]";
            statusColor = new Vector4(0.4f, 0.7f, 1f, 1f); // Blue
        }
        else if (bodyTemp > HotThreshold)
        {
            statusText = "[HOT]";
            statusColor = new Vector4(1f, 0.5f, 0.2f, 1f); // Orange
        }
        else
        {
            // Normal temperature - show subtle indicator
            statusText = $"{bodyTemp:0.0}C";
            statusColor = new Vector4(0.6f, 0.8f, 0.6f, 0.8f); // Faded green
        }

        // Flash effect for warnings
        if (bodyTemp < ColdThreshold || bodyTemp > HotThreshold)
        {
            var flash = (float)Math.Sin(ImGui.GetTime() * 4) * 0.3f + 0.7f;
            statusColor = statusColor with { W = flash };
        }

        drawList.AddText(new Vector2(x, y), ImGui.ColorConvertFloat4ToU32(statusColor), statusText);
    }
}
