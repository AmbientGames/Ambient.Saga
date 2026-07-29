using Ambient.Rpg.Ui.ViewModels;
using ImGuiNET;
using System.Diagnostics;
using System.Numerics;

namespace Ambient.Rpg.Ui.Components.Panels;

using Ambient.Rpg.Rendering.DirectX;
using Ambient.Rpg.Ui;

/// <summary>
/// Renders the map legend showing what map markers mean.
/// Simple for players, with extra detail when debugger is attached.
/// </summary>
public static class MapLegend
{
    /// <summary>
    /// Render the complete map legend in a collapsible format.
    /// </summary>
    public static void Render()
    {
        if (ImGui.CollapsingHeader("Legend", ImGuiTreeNodeFlags.DefaultOpen))
        {
            ImGui.Indent(5 * UIConstants.DpiScale);

            RenderLocationsLegend();
            ImGui.Spacing();

            RenderTriggersLegend();
            ImGui.Spacing();

            RenderCharactersLegend();

            // Developer section - only when debugger attached
            if (Debugger.IsAttached)
            {
                ImGui.Spacing();
                RenderDevLegend();
            }

            ImGui.Unindent(5 * UIConstants.DpiScale);
        }
    }

    /// <summary>
    /// Render locations (arc feature dots) - status-based coloring.
    /// </summary>
    private static void RenderLocationsLegend()
    {
        ImGui.TextColored(UIColors.TextHighlight, "Locations:");
        ImGui.Spacing();
        ImGui.Indent(10 * UIConstants.DpiScale);

        // Status-based colors - matches ArcColors
        RenderLegendCircle(ArcColors.Available, "Available", filled: true);
        RenderLegendCircle(ArcColors.Locked, "Locked", filled: true);
        RenderLegendCircle(ArcColors.Complete, "Complete", filled: true);

        ImGui.Unindent(10 * UIConstants.DpiScale);
        ImGui.TextColored(UIColors.TextDim, "Hover for details");
    }

    /// <summary>
    /// Render trigger rings legend - matches TriggerColors.
    /// Completed triggers are hidden, so not shown in legend.
    /// </summary>
    private static void RenderTriggersLegend()
    {
        ImGui.TextColored(UIColors.TextHighlight, "Trigger Rings:");
        ImGui.Spacing();
        ImGui.Indent(10 * UIConstants.DpiScale);

        // Status-based colors - matches TriggerColors (Complete hidden, not shown)
        RenderLegendCircle(TriggerColors.AvailableColor, "Available", filled: false);
        RenderLegendCircle(TriggerColors.LockedColor, "Locked", filled: false);

        ImGui.Unindent(10 * UIConstants.DpiScale);
    }

    /// <summary>
    /// Render characters legend - simple alive/dead/you.
    /// </summary>
    private static void RenderCharactersLegend()
    {
        ImGui.TextColored(UIColors.TextHighlight, "Characters:");
        ImGui.Spacing();
        ImGui.Indent(10 * UIConstants.DpiScale);

        // Matches MainViewModel character coloring
        RenderLegendCircle(new Vector4(1f, 0.65f, 0f, 1f), "Alive", filled: true);    // Orange
        RenderLegendCircle(UIColors.TextDisabled, "Dead", filled: true);  // Gray
        RenderLegendCircle(new Vector4(0f, 1f, 1f, 1f), "You", filled: true);         // Cyan

        ImGui.Unindent(10 * UIConstants.DpiScale);
    }

    /// <summary>
    /// Developer-only legend section with additional context.
    /// Only shown when debugger is attached.
    /// </summary>
    private static void RenderDevLegend()
    {
        ImGui.Separator();
        ImGui.TextColored(new Vector4(1f, 0.5f, 0f, 1f), "Dev Info:");
        ImGui.Spacing();
        ImGui.Indent(10 * UIConstants.DpiScale);

        ImGui.TextColored(UIColors.TextDim, "Hover shows:");
        ImGui.BulletText("Feature type");
        ImGui.BulletText("Arc/Character ref");
        ImGui.BulletText("Interaction status");
        ImGui.BulletText("Quest tokens");

        ImGui.Unindent(10 * UIConstants.DpiScale);
    }

    private static void RenderLegendCircle(Vector4 color, string label, bool filled)
    {
        var drawList = ImGui.GetWindowDrawList();
        var cursorPos = ImGui.GetCursorScreenPos();
        var scale = UIConstants.DpiScale;

        // Draw circle at current position (scaled offsets to align with text)
        var textHeight = ImGui.GetTextLineHeight();
        var radius = 4f * scale;
        var circleCenter = new Vector2(cursorPos.X + radius, cursorPos.Y + textHeight / 2);
        var circleColor = ImGui.ColorConvertFloat4ToU32(color);

        if (filled)
        {
            drawList.AddCircleFilled(circleCenter, radius, circleColor, 12);
        }
        else
        {
            drawList.AddCircle(circleCenter, radius, circleColor, 12, 2.0f * scale);
        }

        // Move cursor past the circle and render text
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + radius * 2 + 8 * scale);
        ImGui.Text(label);
    }
}
