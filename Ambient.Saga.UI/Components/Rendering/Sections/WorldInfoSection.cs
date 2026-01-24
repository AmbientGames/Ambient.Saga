using ImGuiNET;
using System.Numerics;

namespace Ambient.Saga.UI.Components.Rendering.Sections;

/// <summary>
/// Top-right HUD section displaying world/simulation info.
/// Shows time of day, weather, and debug info (HudTextRight from Schema).
/// </summary>
public class WorldInfoSection : IHudSection
{
    public HudRegion Region => HudRegion.TopRight;
    public int Priority => 0;

    public void Render(HudContext context)
    {
        var drawList = ImGui.GetWindowDrawList();
        var windowPos = ImGui.GetWindowPos();
        var windowSize = ImGui.GetWindowSize();

        // Right-align text in this region
        var textColor = ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 0f, 1f)); // Yellow
        var dimColor = ImGui.ColorConvertFloat4ToU32(new Vector4(0.7f, 0.7f, 0.7f, 0.8f)); // Dim gray

        var currentY = windowPos.Y;

        // HudText1 contains world/navigation info from Schema (direction, location, weather)
        if (!string.IsNullOrEmpty(context.ViewModel.HudText1))
        {
            var lines = context.ViewModel.HudText1.Split('\n');
            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    currentY += ImGui.CalcTextSize("M").Y;
                    continue;
                }

                var textSize = ImGui.CalcTextSize(line);
                var textX = windowPos.X + windowSize.X - textSize.X;
                drawList.AddText(new Vector2(textX, currentY), textColor, line);
                currentY += textSize.Y + 2f;
            }
        }

        // HudText2 contains debug info from Schema (FPS, etc.) - optional
        if (!string.IsNullOrEmpty(context.ViewModel.HudText2))
        {
            // Add a small gap between world info and debug info
            if (!string.IsNullOrEmpty(context.ViewModel.HudText1))
                currentY += 4f;

            var lines = context.ViewModel.HudText2.Split('\n');
            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    currentY += ImGui.CalcTextSize("M").Y;
                    continue;
                }

                var textSize = ImGui.CalcTextSize(line);
                var textX = windowPos.X + windowSize.X - textSize.X;
                drawList.AddText(new Vector2(textX, currentY), dimColor, line); // Dimmer for debug
                currentY += textSize.Y + 2f;
            }
        }
    }
}
