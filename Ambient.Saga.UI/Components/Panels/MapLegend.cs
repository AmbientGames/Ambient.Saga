using Ambient.Saga.UI.ViewModels;
using ImGuiNET;
using System.Diagnostics;
using System.Numerics;

namespace Ambient.Saga.UI.Components.Panels;

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
            ImGui.Indent(5);

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

            ImGui.Unindent(5);
        }
    }

    /// <summary>
    /// Render locations (saga feature dots) - status-based coloring.
    /// </summary>
    private static void RenderLocationsLegend()
    {
        ImGui.TextColored(new Vector4(0.8f, 0.8f, 1, 1), "Locations:");
        ImGui.Spacing();
        ImGui.Indent(10);

        // Status-based colors - matches FeatureColors
        RenderLegendCircle(FeatureColors.Available, "Available", filled: true);
        RenderLegendCircle(FeatureColors.Locked, "Locked", filled: true);
        RenderLegendCircle(FeatureColors.Complete, "Complete", filled: true);

        ImGui.Unindent(10);
        ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1), "Hover for details");
    }

    /// <summary>
    /// Render trigger rings legend - matches TriggerColors.
    /// Completed triggers are hidden, so not shown in legend.
    /// </summary>
    private static void RenderTriggersLegend()
    {
        ImGui.TextColored(new Vector4(0.8f, 0.8f, 1, 1), "Trigger Rings:");
        ImGui.Spacing();
        ImGui.Indent(10);

        // Status-based colors - matches TriggerColors (Complete hidden, not shown)
        RenderLegendCircle(TriggerColors.AvailableColor, "Available", filled: false);
        RenderLegendCircle(TriggerColors.LockedColor, "Locked", filled: false);

        ImGui.Unindent(10);
    }

    /// <summary>
    /// Render characters legend - simple alive/dead/you.
    /// </summary>
    private static void RenderCharactersLegend()
    {
        ImGui.TextColored(new Vector4(0.8f, 0.8f, 1, 1), "Characters:");
        ImGui.Spacing();
        ImGui.Indent(10);

        // Matches MainViewModel character coloring
        RenderLegendCircle(new Vector4(1f, 0.65f, 0f, 1f), "Alive", filled: true);    // Orange
        RenderLegendCircle(new Vector4(0.5f, 0.5f, 0.5f, 1f), "Dead", filled: true);  // Gray
        RenderLegendCircle(new Vector4(0f, 1f, 1f, 1f), "You", filled: true);         // Cyan

        ImGui.Unindent(10);
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
        ImGui.Indent(10);

        ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1), "Hover shows:");
        ImGui.BulletText("Feature type");
        ImGui.BulletText("Saga/Character ref");
        ImGui.BulletText("Interaction status");
        ImGui.BulletText("Quest tokens");

        ImGui.Unindent(10);
    }

    private static void RenderLegendCircle(Vector4 color, string label, bool filled)
    {
        var drawList = ImGui.GetWindowDrawList();
        var cursorPos = ImGui.GetCursorScreenPos();

        // Draw circle at current position
        var circleCenter = new Vector2(cursorPos.X + 5, cursorPos.Y + 7); // Offset to align with text
        var radius = 4f;
        var circleColor = ImGui.ColorConvertFloat4ToU32(color);

        if (filled)
        {
            drawList.AddCircleFilled(circleCenter, radius, circleColor, 12);
        }
        else
        {
            drawList.AddCircle(circleCenter, radius, circleColor, 12, 2.0f);
        }

        // Move cursor past the circle and render text
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 15);
        ImGui.Text(label);
    }
}
