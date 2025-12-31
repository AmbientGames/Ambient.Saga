using System.Diagnostics;
using Ambient.Saga.Presentation.UI.ViewModels;
using ImGuiNET;
using System.Numerics;

namespace Ambient.Saga.UI.Components.Rendering;

/// <summary>
/// Default HUD renderer showing hotkey hints and status information.
/// Renders a bar at the bottom of the screen.
/// </summary>
public class DefaultHudRenderer : IHudRenderer
{
    public void Render(MainViewModel viewModel, ActivePanel activePanel, Vector2 displaySize)
    {
        // Position at bottom of screen
        var hudHeight = 40f;
        ImGui.SetNextWindowPos(new Vector2(0, displaySize.Y - hudHeight));
        ImGui.SetNextWindowSize(new Vector2(displaySize.X, hudHeight));

        var windowFlags = ImGuiWindowFlags.NoTitleBar |
                          ImGuiWindowFlags.NoResize |
                          ImGuiWindowFlags.NoMove |
                          ImGuiWindowFlags.NoScrollbar |
                          ImGuiWindowFlags.NoCollapse |
                          ImGuiWindowFlags.NoBringToFrontOnFocus;

        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(10, 8));
        ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.1f, 0.1f, 0.15f, 0.9f));

        if (ImGui.Begin("##HudBar", windowFlags))
        {
            // Left side: Hotkey hints
            // Only show Map hint if world has a height map (procedural/generated worlds don't)
            if (viewModel.HeightMapImage != null)
            {
                RenderHotkeyHint("M", "Map", activePanel == ActivePanel.Map);
                ImGui.SameLine();
                ImGui.TextColored(new Vector4(0.4f, 0.4f, 0.4f, 1), "|");
                ImGui.SameLine();
            }
            RenderHotkeyHint("C", "Character", activePanel == ActivePanel.Character);
            ImGui.SameLine();
            ImGui.TextColored(new Vector4(0.4f, 0.4f, 0.4f, 1), "|");
            ImGui.SameLine();
            RenderHotkeyHint("I", "World Info", activePanel == ActivePanel.WorldInfo);

            // Dev Tools hint (only when debugger attached)
            if (Debugger.IsAttached)
            {
                ImGui.SameLine();
                ImGui.TextColored(new Vector4(0.4f, 0.4f, 0.4f, 1), "|");
                ImGui.SameLine();
                RenderHotkeyHint("Ins", "Dev Tools", activePanel == ActivePanel.DevTools, isDevTool: true);
            }

            // Center: Status message
            if (!string.IsNullOrEmpty(viewModel.StatusMessage))
            {
                ImGui.SameLine(displaySize.X / 2 - 100);
                ImGui.Text(viewModel.StatusMessage);
            }

            // Right side: Avatar position (if available)
            if (viewModel.HasAvatarPosition)
            {
                var posText = $"({viewModel.AvatarLatitude:F2}, {viewModel.AvatarLongitude:F2})";
                var textWidth = ImGui.CalcTextSize(posText).X;
                ImGui.SameLine(displaySize.X - textWidth - 20);
                ImGui.TextColored(new Vector4(0.7f, 0.9f, 0.7f, 1), posText);
            }

            if (viewModel.IsLoading)
            {
                ImGui.SameLine();
                ImGui.TextColored(new Vector4(1, 1, 0, 1), "Loading...");
            }
        }
        ImGui.End();

        ImGui.PopStyleColor();
        ImGui.PopStyleVar();
    }

    private void RenderHotkeyHint(string key, string label, bool isActive, bool isDevTool = false)
    {
        // Key box - dev tools get orange styling
        Vector4 keyColor;
        Vector4 textColor;

        if (isDevTool)
        {
            keyColor = isActive
                ? new Vector4(0.8f, 0.5f, 0.2f, 1f)  // Orange when active
                : new Vector4(0.4f, 0.25f, 0.1f, 1f); // Dark orange when inactive
            textColor = isActive
                ? new Vector4(1f, 0.8f, 0.5f, 1f)    // Light orange when active
                : new Vector4(0.7f, 0.5f, 0.3f, 1f); // Dim orange when inactive
        }
        else
        {
            keyColor = isActive
                ? new Vector4(0.3f, 0.7f, 0.3f, 1f)  // Green when active
                : new Vector4(0.3f, 0.3f, 0.3f, 1f); // Gray when inactive
            textColor = isActive
                ? new Vector4(1f, 1f, 1f, 1f)        // White when active
                : new Vector4(0.7f, 0.7f, 0.7f, 1f); // Light gray when inactive
        }

        ImGui.PushStyleColor(ImGuiCol.Button, keyColor);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, keyColor);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, keyColor);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(6, 4));

        // Size button to fit text with minimum width
        var textSize = ImGui.CalcTextSize(key);
        var buttonWidth = Math.Max(28, textSize.X + 14);
        ImGui.Button(key, new Vector2(buttonWidth, 26));

        ImGui.PopStyleVar();
        ImGui.PopStyleColor(3);

        ImGui.SameLine();
        ImGui.TextColored(textColor, label);
    }
}
