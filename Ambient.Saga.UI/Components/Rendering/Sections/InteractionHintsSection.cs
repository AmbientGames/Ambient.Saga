using Ambient.Saga.UI;
using Ambient.Saga.UI.Configuration;
using ImGuiNET;
using System.Diagnostics;
using System.Numerics;

namespace Ambient.Saga.UI.Components.Rendering.Sections;

/// <summary>
/// Bottom-right HUD section displaying panel hotkeys.
/// Shows keyboard shortcuts for M/C/I/J panels with active state indication.
/// Compact design that fits within the right region of the bottom bar.
/// </summary>
public class InteractionHintsSection : IHudSection
{
    // Base values at 96 DPI (1.0 scale)
    private const float KeySpacingBase = 2f;
    private const float GroupSpacingBase = 8f;
    private const float KeyPaddingBase = 4f;
    private const float KeyHeightOffsetBase = 6f;
    private const float KeyRoundingBase = 3f;

    // DPI-scaled values
    private static float KeySpacing => KeySpacingBase * UIConstants.DpiScale;
    private static float GroupSpacing => GroupSpacingBase * UIConstants.DpiScale;
    private static float KeyPadding => KeyPaddingBase * UIConstants.DpiScale;
    private static float KeyHeightOffset => KeyHeightOffsetBase * UIConstants.DpiScale;
    private static float KeyRounding => KeyRoundingBase * UIConstants.DpiScale;

    public HudRegion Region => HudRegion.BottomRight;
    public int Priority => 0;

    public void Render(HudContext context)
    {
        var isDebug = Debugger.IsAttached;
        var drawList = ImGui.GetWindowDrawList();
        var startPos = ImGui.GetCursorScreenPos();

        // Calculate total width needed to right-align
        var totalWidth = CalculateTotalWidth(context, isDebug);
        var availableWidth = context.RightRegionWidth;

        // Start position (right-aligned within region, vertically centered in HUD)
        var currentX = startPos.X + Math.Max(0, availableWidth - totalWidth);
        var centerY = startPos.Y + context.HudHeight / 2;

        // Gameplay hotkeys
        if (context.HasMap)
        {
            currentX = RenderCompactKey(drawList, currentX, centerY, "M", context.ActivePanel == ActivePanel.Map);
            currentX += KeySpacing;
        }

        currentX = RenderCompactKey(drawList, currentX, centerY, "C", context.ActivePanel == ActivePanel.Character);
        currentX += KeySpacing;

        currentX = RenderCompactKey(drawList, currentX, centerY, "I", context.ActivePanel == ActivePanel.Inventory);
        currentX += KeySpacing;

        currentX = RenderCompactKey(drawList, currentX, centerY, "J", context.ActivePanel == ActivePanel.Journal);

        // Developer keys (only when debugger attached)
        if (isDebug)
        {
            currentX += GroupSpacing;

            // Separator line
            var sepColor = ImGui.ColorConvertFloat4ToU32(new Vector4(0.4f, 0.4f, 0.4f, 0.6f));
            var keyHeight = ImGui.CalcTextSize("M").Y + KeyHeightOffset;
            drawList.AddLine(
                new Vector2(currentX, centerY - keyHeight / 2),
                new Vector2(currentX, centerY + keyHeight / 2),
                sepColor, UIConstants.DpiScale);
            currentX += GroupSpacing;

            currentX = RenderCompactKey(drawList, currentX, centerY, "F1", context.ActivePanel == ActivePanel.WorldInfo, isDev: true);
            currentX += KeySpacing;

            currentX = RenderCompactKey(drawList, currentX, centerY, "F2", GameConfiguration.ShowDeveloperInfo, isDev: true);
            currentX += KeySpacing;

            RenderCompactKey(drawList, currentX, centerY, "F12", context.ActivePanel == ActivePanel.DevTools, isDev: true);
        }

        // Reserve space
        ImGui.Dummy(new Vector2(totalWidth, context.HudHeight));
    }

    private float CalculateTotalWidth(HudContext context, bool isDebug)
    {
        var width = 0f;
        var keyHorizontalPadding = KeyPadding * 2; // Horizontal padding per key (left + right)

        // Gameplay keys
        if (context.HasMap)
        {
            width += ImGui.CalcTextSize("M").X + keyHorizontalPadding + KeySpacing;
        }
        width += ImGui.CalcTextSize("C").X + keyHorizontalPadding + KeySpacing;
        width += ImGui.CalcTextSize("I").X + keyHorizontalPadding + KeySpacing;
        width += ImGui.CalcTextSize("J").X + keyHorizontalPadding;

        // Dev keys
        if (isDebug)
        {
            width += GroupSpacing * 2 + UIConstants.DpiScale; // Separator
            width += ImGui.CalcTextSize("F1").X + keyHorizontalPadding + KeySpacing;
            width += ImGui.CalcTextSize("F2").X + keyHorizontalPadding + KeySpacing;
            width += ImGui.CalcTextSize("F12").X + keyHorizontalPadding;
        }

        return width;
    }

    private float RenderCompactKey(ImDrawListPtr drawList, float x, float centerY, string key, bool isActive, bool isDev = false)
    {
        var textSize = ImGui.CalcTextSize(key);
        var keyWidth = textSize.X + KeyPadding * 2;
        var keyHeight = textSize.Y + KeyPadding;

        var keyRect = new Vector2(x, centerY - keyHeight / 2);
        var keyRectEnd = new Vector2(x + keyWidth, centerY + keyHeight / 2);

        // Background color based on state
        Vector4 bgColor, textColor;
        if (isDev)
        {
            bgColor = isActive
                ? new Vector4(0.5f, 0.35f, 0.15f, 0.9f)   // Orange-brown active
                : new Vector4(0.25f, 0.2f, 0.1f, 0.7f);   // Dark brown inactive
            textColor = isActive
                ? new Vector4(1f, 0.9f, 0.7f, 1f)
                : new Vector4(0.6f, 0.5f, 0.3f, 1f);
        }
        else
        {
            bgColor = isActive
                ? new Vector4(0.2f, 0.5f, 0.25f, 0.9f)    // Green active
                : new Vector4(0.2f, 0.2f, 0.22f, 0.7f);   // Dark gray inactive
            textColor = isActive
                ? new Vector4(1f, 1f, 1f, 1f)
                : new Vector4(0.55f, 0.55f, 0.55f, 1f);
        }

        // Draw rounded rectangle background
        drawList.AddRectFilled(keyRect, keyRectEnd, ImGui.ColorConvertFloat4ToU32(bgColor), KeyRounding);

        // Draw subtle border
        var borderColor = isActive
            ? new Vector4(0.5f, 0.7f, 0.5f, 0.5f)
            : new Vector4(0.35f, 0.35f, 0.35f, 0.4f);
        drawList.AddRect(keyRect, keyRectEnd, ImGui.ColorConvertFloat4ToU32(borderColor), KeyRounding);

        // Draw key text centered
        var textPos = new Vector2(
            x + (keyWidth - textSize.X) / 2,
            centerY - textSize.Y / 2);
        drawList.AddText(textPos, ImGui.ColorConvertFloat4ToU32(textColor), key);

        return x + keyWidth;
    }
}
