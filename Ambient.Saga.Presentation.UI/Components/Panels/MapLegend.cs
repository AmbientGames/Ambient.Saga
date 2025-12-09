using ImGuiNET;
using System.Numerics;

namespace Ambient.Saga.Presentation.UI.Components.Panels;

/// <summary>
/// Renders the map legend (Features, Trigger Rings, Spawned Characters).
/// Used by MapViewPanel to show legend alongside the map.
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

            RenderFeaturesLegend();
            ImGui.Spacing();

            RenderTriggerRingsLegend();
            ImGui.Spacing();

            RenderSpawnedCharactersLegend();

            ImGui.Unindent(5);
        }
    }

    /// <summary>
    /// Render just the features legend section.
    /// </summary>
    public static void RenderFeaturesLegend()
    {
        ImGui.TextColored(new Vector4(0.8f, 0.8f, 1, 1), "Features:");
        ImGui.Spacing();

        // Structure (Blue shades)
        RenderFeatureLegend("Structure",
            new Vector4(0.118f, 0.565f, 1.0f, 1.0f),    // #1E90FF - Available
            new Vector4(0.275f, 0.510f, 0.706f, 1.0f),  // #4682B4 - Locked
            new Vector4(0.502f, 0.502f, 0.502f, 1.0f)); // #808080 - Complete

        // Landmark (Red shades)
        RenderFeatureLegend("Landmark",
            new Vector4(1.0f, 0.271f, 0.0f, 1.0f),      // #FF4500 - Available
            new Vector4(0.698f, 0.133f, 0.133f, 1.0f),  // #B22222 - Locked
            new Vector4(0.502f, 0.502f, 0.502f, 1.0f)); // #808080 - Complete

        // Quest Signpost (Green shades)
        RenderFeatureLegend("Quest Signpost",
            new Vector4(0.196f, 0.804f, 0.196f, 1.0f),  // #32CD32 - Available
            new Vector4(0.420f, 0.557f, 0.137f, 1.0f),  // #6B8E23 - Locked
            new Vector4(0.502f, 0.502f, 0.502f, 1.0f)); // #808080 - Complete

        // Hint text about hovering
        ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1), "Hover to see triggers");
    }

    /// <summary>
    /// Render just the trigger rings legend section.
    /// </summary>
    public static void RenderTriggerRingsLegend()
    {
        ImGui.TextColored(new Vector4(0.8f, 0.8f, 1, 1), "Trigger Rings:");
        ImGui.Spacing();
        ImGui.Indent(10);
        RenderLegendCircle(new Vector4(0.196f, 0.804f, 0.196f, 1.0f), "Available", filled: false); // #32CD32
        RenderLegendCircle(new Vector4(1, 0, 0, 1), "Locked", filled: false);
        RenderLegendCircle(new Vector4(0.502f, 0.502f, 0.502f, 1.0f), "Complete", filled: false); // #808080
        ImGui.Unindent(10);
    }

    /// <summary>
    /// Render just the spawned characters legend section.
    /// </summary>
    public static void RenderSpawnedCharactersLegend()
    {
        ImGui.TextColored(new Vector4(0.8f, 0.8f, 1, 1), "Characters:");
        ImGui.Spacing();
        ImGui.Indent(10);
        RenderLegendCircle(new Vector4(1, 0, 0, 1), "Boss", filled: true); // Red
        RenderLegendCircle(new Vector4(1, 0.843f, 0, 1), "Merchant", filled: true); // Gold
        RenderLegendCircle(new Vector4(0, 0, 1, 1), "NPC", filled: true); // Blue
        RenderLegendCircle(new Vector4(0, 1, 1, 1), "Player", filled: true); // Cyan
        RenderLegendCircle(new Vector4(0, 1, 0, 1), "You", filled: true); // Lime
        ImGui.Unindent(10);
    }

    private static void RenderFeatureLegend(string label, Vector4 availableColor, Vector4 lockedColor, Vector4 completeColor)
    {
        // Feature type name
        ImGui.Text(label);
        ImGui.Indent(10);

        // Three-dot status legend - draw circles instead of Unicode
        RenderLegendCircle(availableColor, "Available", filled: true);
        RenderLegendCircle(lockedColor, "Locked", filled: true);
        RenderLegendCircle(completeColor, "Complete", filled: true);

        ImGui.Unindent(10);
        ImGui.Spacing();
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
