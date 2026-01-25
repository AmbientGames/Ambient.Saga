using ImGuiNET;
using System.Numerics;

namespace Ambient.Saga.UI.Components.Rendering.Sections;

/// <summary>
/// Bottom-left HUD section displaying resource bars (HP, Stamina, Mana).
/// Shows spendable resources as horizontal bars with percentage fill.
/// Bars are sized dynamically based on available region width.
/// </summary>
public class ResourceBarsSection : IHudSection
{
    // Bar styling - proportions relative to available space
    private const float LabelWidthRatio = 0.18f;  // Label takes 18% of region
    private const float BarWidthRatio = 0.75f;    // Bar takes 75% of region
    private const float MinBarWidth = 60f;        // Minimum bar width
    private const float MaxBarWidth = 180f;       // Maximum bar width
    private const float BarHeightRatio = 0.28f;   // Each bar is 28% of available height
    private const float MinBarHeight = 10f;
    private const float MaxBarHeight = 18f;
    private const float VerticalPadding = 4f;     // Padding at top/bottom

    public HudRegion Region => HudRegion.BottomLeft;
    public int Priority => 0;

    public void Render(HudContext context)
    {
        var stats = context.ViewModel.PlayerAvatar?.Stats;
        if (stats == null)
            return;

        var drawList = ImGui.GetWindowDrawList();
        var startPos = ImGui.GetCursorScreenPos();
        var style = ImGui.GetStyle();

        // Calculate dimensions based on available space
        var availableWidth = context.LeftRegionWidth - style.WindowPadding.X;
        var availableHeight = context.HudHeight - style.WindowPadding.Y * 2 - VerticalPadding * 2;

        // Calculate bar dimensions
        var labelWidth = Math.Max(20f, availableWidth * LabelWidthRatio);
        var barWidth = Math.Clamp(availableWidth * BarWidthRatio, MinBarWidth, MaxBarWidth);

        // Count how many bars we'll show
        var barCount = stats.Mana > 0 ? 3 : 2;
        var totalBarHeight = availableHeight - VerticalPadding;
        var barHeight = Math.Clamp(totalBarHeight / barCount * 0.7f, MinBarHeight, MaxBarHeight);
        var barSpacing = (totalBarHeight - barHeight * barCount) / (barCount + 1);

        var currentY = startPos.Y + VerticalPadding + barSpacing;

        // Health bar (red)
        RenderBar(drawList, startPos.X, currentY, labelWidth, barWidth, barHeight,
            "HP", stats.Health, 1.0f,
            new Vector4(0.8f, 0.2f, 0.2f, 1f),
            new Vector4(0.3f, 0.1f, 0.1f, 1f));
        currentY += barHeight + barSpacing;

        // Stamina bar (green)
        RenderBar(drawList, startPos.X, currentY, labelWidth, barWidth, barHeight,
            "ST", stats.Stamina, 1.0f,
            new Vector4(0.2f, 0.7f, 0.3f, 1f),
            new Vector4(0.1f, 0.25f, 0.1f, 1f));
        currentY += barHeight + barSpacing;

        // Mana bar (blue) - only show if character has mana
        if (stats.Mana > 0)
        {
            RenderBar(drawList, startPos.X, currentY, labelWidth, barWidth, barHeight,
                "MP", stats.Mana, 1.0f,
                new Vector4(0.3f, 0.4f, 0.9f, 1f),
                new Vector4(0.1f, 0.15f, 0.35f, 1f));
        }

        // Advance cursor past all bars
        ImGui.Dummy(new Vector2(labelWidth + barWidth, availableHeight));
    }

    private void RenderBar(ImDrawListPtr drawList, float x, float y,
        float labelWidth, float barWidth, float barHeight,
        string label, float current, float max, Vector4 fillColor, Vector4 bgColor)
    {
        var fraction = Math.Clamp(current / max, 0f, 1f);
        var textSize = ImGui.CalcTextSize(label);
        var labelPos = new Vector2(x, y + (barHeight - textSize.Y) / 2);
        var barX = x + labelWidth;

        // Label
        drawList.AddText(labelPos, ImGui.ColorConvertFloat4ToU32(new Vector4(0.8f, 0.8f, 0.8f, 1f)), label);

        // Background
        drawList.AddRectFilled(
            new Vector2(barX, y),
            new Vector2(barX + barWidth, y + barHeight),
            ImGui.ColorConvertFloat4ToU32(bgColor), 3f);

        // Fill
        if (fraction > 0)
        {
            drawList.AddRectFilled(
                new Vector2(barX, y),
                new Vector2(barX + barWidth * fraction, y + barHeight),
                ImGui.ColorConvertFloat4ToU32(fillColor), 3f);
        }

        // Border
        drawList.AddRect(
            new Vector2(barX, y),
            new Vector2(barX + barWidth, y + barHeight),
            ImGui.ColorConvertFloat4ToU32(new Vector4(0.4f, 0.4f, 0.4f, 0.8f)), 3f);

        // Percentage text (only if bar is wide enough)
        var percentText = $"{(int)(fraction * 100)}%";
        var percentTextSize = ImGui.CalcTextSize(percentText);
        if (barWidth > percentTextSize.X + 10 && barHeight >= percentTextSize.Y)
        {
            var textPos = new Vector2(
                barX + (barWidth - percentTextSize.X) / 2,
                y + (barHeight - percentTextSize.Y) / 2);
            drawList.AddText(textPos, ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 0.9f)), percentText);
        }
    }
}
