using Ambient.Saga.UI;
using ImGuiNET;
using System.Numerics;

namespace Ambient.Saga.UI.Components.Rendering.Sections;

/// <summary>
/// Bottom-left HUD section displaying resource bars (HP, Stamina, Mana).
/// Shows spendable resources as horizontal bars with percentage fill.
/// Fixed height matching the hotbar (40px total).
/// </summary>
public class ResourceBarsSection : IHudSection
{
    // Bar styling base values at 96 DPI (1.0 scale)
    private const float LabelWidthBase = 24f;
    private const float BarWidthBase = 80f;
    private const float BarHeightBase = 10f;
    private const float BarSpacingBase = 2f;
    private const float ContentHeightBase = 40f;
    private const float BarRoundingBase = 3f;

    // DPI-scaled values
    private static float LabelWidth => LabelWidthBase * UIConstants.DpiScale;
    private static float BarWidth => BarWidthBase * UIConstants.DpiScale;
    private static float BarHeight => BarHeightBase * UIConstants.DpiScale;
    private static float BarSpacing => BarSpacingBase * UIConstants.DpiScale;
    private static float ContentHeight => ContentHeightBase * UIConstants.DpiScale;
    private static float BarRounding => BarRoundingBase * UIConstants.DpiScale;

    public HudRegion Region => HudRegion.BottomLeft;
    public int Priority => 0;

    public void Render(HudContext context)
    {
        var stats = context.ViewModel.PlayerAvatar?.Stats;
        if (stats == null)
            return;

        var drawList = ImGui.GetWindowDrawList();
        var startPos = ImGui.GetCursorScreenPos();

        // Calculate how many bars and total height
        var barCount = stats.Mana > 0 ? 3 : 2;
        var totalBarsHeight = (BarHeight * barCount) + (BarSpacing * (barCount - 1));

        // Center bars vertically within the content area
        var baseY = startPos.Y + (context.HudHeight - totalBarsHeight) / 2f;
        var currentY = baseY;

        // Health bar (red)
        RenderBar(drawList, startPos.X, currentY, LabelWidth, BarWidth, BarHeight,
            "HP", stats.Health, 1.0f,
            new Vector4(0.8f, 0.2f, 0.2f, 1f),
            new Vector4(0.3f, 0.1f, 0.1f, 1f));
        currentY += BarHeight + BarSpacing;

        // Stamina bar (green)
        RenderBar(drawList, startPos.X, currentY, LabelWidth, BarWidth, BarHeight,
            "ST", stats.Stamina, 1.0f,
            new Vector4(0.2f, 0.7f, 0.3f, 1f),
            new Vector4(0.1f, 0.25f, 0.1f, 1f));
        currentY += BarHeight + BarSpacing;

        // Mana bar (blue) - only show if character has mana
        if (stats.Mana > 0)
        {
            RenderBar(drawList, startPos.X, currentY, LabelWidth, BarWidth, BarHeight,
                "MP", stats.Mana, 1.0f,
                new Vector4(0.3f, 0.4f, 0.9f, 1f),
                new Vector4(0.1f, 0.15f, 0.35f, 1f));
        }

        // Advance cursor past all bars
        ImGui.Dummy(new Vector2(LabelWidth + BarWidth, ContentHeight));
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
            ImGui.ColorConvertFloat4ToU32(bgColor), BarRounding);

        // Fill
        if (fraction > 0)
        {
            drawList.AddRectFilled(
                new Vector2(barX, y),
                new Vector2(barX + barWidth * fraction, y + barHeight),
                ImGui.ColorConvertFloat4ToU32(fillColor), BarRounding);
        }

        // Border
        drawList.AddRect(
            new Vector2(barX, y),
            new Vector2(barX + barWidth, y + barHeight),
            ImGui.ColorConvertFloat4ToU32(new Vector4(0.4f, 0.4f, 0.4f, 0.8f)), BarRounding);

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
