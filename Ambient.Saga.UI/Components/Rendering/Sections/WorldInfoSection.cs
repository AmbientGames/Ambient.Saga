using Ambient.Saga.UI;
using ImGuiNET;
using System.Numerics;

namespace Ambient.Saga.UI.Components.Rendering.Sections;

/// <summary>
/// Top-right HUD section displaying world/simulation info.
/// Shows navigation info (HudText1) and optional debug info (HudText2).
/// Text is right-aligned with subtle background for readability over 3D content.
/// </summary>
public class WorldInfoSection : IHudSection
{
    // Base values at 96 DPI (1.0 scale)
    private const float LineSpacingBase = 2f;
    private const float SectionSpacingBase = 16f; // Gap between HudText1 and HudText2/Toast
    private const float BackgroundPaddingBase = 4f;
    private const float BackgroundRadiusBase = 3f;

    // DPI-scaled values
    private static float LineSpacing => LineSpacingBase * UIConstants.DpiScale;
    private static float SectionSpacing => SectionSpacingBase * UIConstants.DpiScale;
    private static float BackgroundPadding => BackgroundPaddingBase * UIConstants.DpiScale;
    private static float BackgroundRadius => BackgroundRadiusBase * UIConstants.DpiScale;

    public HudRegion Region => HudRegion.TopRight;
    public int Priority => 0;

    public void Render(HudContext context)
    {
        var drawList = ImGui.GetWindowDrawList();
        var windowPos = ImGui.GetWindowPos();
        var windowSize = ImGui.GetWindowSize();

        var currentY = windowPos.Y;

        // HudText1 contains world/navigation info (direction, location, weather)
        // Always render HudText1 even when toasts are active
        if (!string.IsNullOrEmpty(context.ViewModel.HudText1))
        {
            currentY = RenderTextBlock(drawList, windowPos, windowSize, currentY,
                context.ViewModel.HudText1,
                new Vector4(1f, 0.95f, 0.7f, 1f),           // Warm yellow text
                new Vector4(0.05f, 0.05f, 0.08f, 0.6f));    // Semi-transparent dark bg
        }

        // HudText2 contains debug info (FPS, etc.) - optional, dimmer styling
        // Always show HudText2 (unlimited height for debug, may overlap toasts)
        if (!string.IsNullOrEmpty(context.ViewModel.HudText2))
        {
            if (!string.IsNullOrEmpty(context.ViewModel.HudText1))
                currentY += SectionSpacing;

            RenderTextBlock(drawList, windowPos, windowSize, currentY,
                context.ViewModel.HudText2,
                new Vector4(0.6f, 0.6f, 0.65f, 0.9f),       // Dim gray text
                new Vector4(0.05f, 0.05f, 0.08f, 0.4f));    // More transparent bg
        }
    }

    private float RenderTextBlock(ImDrawListPtr drawList, Vector2 windowPos, Vector2 windowSize,
        float startY, string text, Vector4 textColor, Vector4 bgColor)
    {
        var lines = text.Split('\n');
        var textColorU32 = ImGui.ColorConvertFloat4ToU32(textColor);
        var bgColorU32 = ImGui.ColorConvertFloat4ToU32(bgColor);

        // First pass: calculate block dimensions
        var maxWidth = 0f;
        var totalHeight = 0f;
        var lineHeight = ImGui.CalcTextSize("M").Y;

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                totalHeight += lineHeight * 0.5f; // Half-height for empty lines
            }
            else
            {
                var textSize = ImGui.CalcTextSize(line);
                maxWidth = Math.Max(maxWidth, textSize.X);
                totalHeight += textSize.Y + LineSpacing;
            }
        }
        totalHeight -= LineSpacing; // Remove trailing spacing

        // Draw background rectangle (with padding)
        if (maxWidth > 0 && totalHeight > 0)
        {
            var bgLeft = windowPos.X + windowSize.X - maxWidth - BackgroundPadding * 2;
            var bgTop = startY - BackgroundPadding;
            var bgRight = windowPos.X + windowSize.X;
            var bgBottom = startY + totalHeight + BackgroundPadding;

            drawList.AddRectFilled(
                new Vector2(bgLeft, bgTop),
                new Vector2(bgRight, bgBottom),
                bgColorU32, BackgroundRadius);
        }

        // Second pass: render text lines
        var currentY = startY;
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                currentY += lineHeight * 0.5f;
                continue;
            }

            var textSize = ImGui.CalcTextSize(line);
            var textX = windowPos.X + windowSize.X - textSize.X - BackgroundPadding;
            drawList.AddText(new Vector2(textX, currentY), textColorU32, line);
            currentY += textSize.Y + LineSpacing;
        }

        return currentY;
    }
}
