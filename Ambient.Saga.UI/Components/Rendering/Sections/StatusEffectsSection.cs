using Ambient.Domain.Enums;
using Ambient.Saga.UI;
using ImGuiNET;
using System.Numerics;

namespace Ambient.Saga.UI.Components.Rendering.Sections;

/// <summary>
/// Top-left HUD section displaying status effects and body temperature.
/// Shows debuffs, buffs, and environmental warnings as icons/states.
/// Temperature is shown as a visual gauge centered on normal body temp (37°C).
/// </summary>
public class StatusEffectsSection : IHudSection
{
    // Temperature gauge styling base values at 96 DPI (1.0 scale)
    private const float GaugeWidthBase = 80f;
    private const float GaugeHeightBase = 8f;
    private const float GaugeSpacingBase = 16f;
    private const float GaugeLabelSpacingBase = 2f;
    private const float TempTextSpacingBase = 4f;
    private const float GaugeRoundingBase = 2f;

    // DPI-scaled values
    private static float GaugeWidth => GaugeWidthBase * UIConstants.DpiScale;
    private static float GaugeHeight => GaugeHeightBase * UIConstants.DpiScale;
    private static float GaugeSpacing => GaugeSpacingBase * UIConstants.DpiScale;
    private static float GaugeLabelSpacing => GaugeLabelSpacingBase * UIConstants.DpiScale;
    private static float TempTextSpacing => TempTextSpacingBase * UIConstants.DpiScale;
    private static float GaugeRounding => GaugeRoundingBase * UIConstants.DpiScale;

    public HudRegion Region => HudRegion.TopLeft;
    public int Priority => 0;

    public void Render(HudContext context)
    {
        var stats = context.ViewModel.Avatar?.Stats;
        if (stats == null)
            return;

        var drawList = ImGui.GetWindowDrawList();
        var windowPos = ImGui.GetWindowPos();
        var currentY = windowPos.Y;

        // Body temperature indicator - visual gauge
        RenderTemperatureGauge(drawList, windowPos.X, currentY, stats.Temperature);
        currentY += GaugeHeight + GaugeSpacing;

        // Status effects (debuffs/buffs) - placeholder for future expansion
        // When status effects are implemented, they would render here as icons
        // Example: bleeding, poisoned, burning, frozen, buffed, etc.
    }

    private void RenderTemperatureGauge(ImDrawListPtr drawList, float x, float y, float bodyTemp)
    {
        // Determine temperature state and colors
        Vector4 statusColor;
        Vector4 bgColor;
        string statusLabel;

        if (bodyTemp < ThermalConstants.ColdCritical)
        {
            statusColor = new Vector4(0.2f, 0.4f, 1f, 1f);      // Deep blue
            bgColor = new Vector4(0.1f, 0.15f, 0.3f, 0.8f);
            statusLabel = "CRITICAL";
        }
        else if (bodyTemp < ThermalConstants.ColdWarning)
        {
            statusColor = new Vector4(0.4f, 0.7f, 1f, 1f);      // Light blue
            bgColor = new Vector4(0.15f, 0.2f, 0.3f, 0.8f);
            statusLabel = "Cold";
        }
        else if (bodyTemp > ThermalConstants.HotCritical)
        {
            statusColor = new Vector4(1f, 0.2f, 0.2f, 1f);      // Deep red
            bgColor = new Vector4(0.3f, 0.1f, 0.1f, 0.8f);
            statusLabel = "CRITICAL";
        }
        else if (bodyTemp > ThermalConstants.HotWarning)
        {
            statusColor = new Vector4(1f, 0.5f, 0.2f, 1f);      // Orange
            bgColor = new Vector4(0.3f, 0.2f, 0.1f, 0.8f);
            statusLabel = "Hot";
        }
        else
        {
            // Normal - don't show anything when temperature is healthy
            return;
        }

        // Flash effect for warnings
        var isWarning = bodyTemp < ThermalConstants.ColdWarning || bodyTemp > ThermalConstants.HotWarning;
        if (isWarning)
        {
            var flash = (float)Math.Sin(ImGui.GetTime() * 4) * 0.3f + 0.7f;
            statusColor = statusColor with { W = flash };
        }

        // Calculate gauge fill based on deviation from normal
        // Gauge shows how far from critical you are (full = near normal, empty = at critical)
        float fillFraction;
        if (bodyTemp < ThermalConstants.NormalBodyTemp)
        {
            // Cold side: ThermalConstants.ColdCritical (0%) -> Normal (100%)
            fillFraction = Math.Clamp((bodyTemp - ThermalConstants.ColdCritical) / (ThermalConstants.NormalBodyTemp - ThermalConstants.ColdCritical), 0f, 1f);
        }
        else
        {
            // Hot side: Normal (100%) -> ThermalConstants.HotCritical (0%)
            fillFraction = Math.Clamp(1f - (bodyTemp - ThermalConstants.NormalBodyTemp) / (ThermalConstants.HotCritical - ThermalConstants.NormalBodyTemp), 0f, 1f);
        }

        // Draw label
        var labelText = statusLabel;
        var labelSize = ImGui.CalcTextSize(labelText);
        drawList.AddText(new Vector2(x, y), ImGui.ColorConvertFloat4ToU32(statusColor), labelText);

        // Draw gauge below label
        var gaugeY = y + labelSize.Y + GaugeLabelSpacing;

        // Background
        drawList.AddRectFilled(
            new Vector2(x, gaugeY),
            new Vector2(x + GaugeWidth, gaugeY + GaugeHeight),
            ImGui.ColorConvertFloat4ToU32(bgColor), GaugeRounding);

        // Fill (inverted - full when healthy, empty when critical)
        if (fillFraction > 0)
        {
            drawList.AddRectFilled(
                new Vector2(x, gaugeY),
                new Vector2(x + GaugeWidth * fillFraction, gaugeY + GaugeHeight),
                ImGui.ColorConvertFloat4ToU32(statusColor), GaugeRounding);
        }

        // Border
        drawList.AddRect(
            new Vector2(x, gaugeY),
            new Vector2(x + GaugeWidth, gaugeY + GaugeHeight),
            ImGui.ColorConvertFloat4ToU32(new Vector4(0.5f, 0.5f, 0.5f, 0.6f)), GaugeRounding);

        // Temperature value (small, to the right of gauge)
        var tempText = $"{bodyTemp:0.0}°";
        var tempSize = ImGui.CalcTextSize(tempText);
        drawList.AddText(
            new Vector2(x + GaugeWidth + TempTextSpacing, gaugeY + (GaugeHeight - tempSize.Y) / 2),
            ImGui.ColorConvertFloat4ToU32(statusColor with { W = 0.8f }),
            tempText);
    }
}
