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
        // Calculate HUD height based on text size + padding
        var textHeight = ImGui.CalcTextSize("M").Y;
        var style = ImGui.GetStyle();
        var buttonHeight = textHeight + style.FramePadding.Y * 2;
        var hudHeight = buttonHeight + style.WindowPadding.Y * 2;

        // Position at bottom of screen
        ImGui.SetNextWindowPos(new Vector2(0, displaySize.Y - hudHeight));
        ImGui.SetNextWindowSize(new Vector2(displaySize.X, hudHeight));

        var windowFlags = ImGuiWindowFlags.NoTitleBar |
                          ImGuiWindowFlags.NoResize |
                          ImGuiWindowFlags.NoMove |
                          ImGuiWindowFlags.NoScrollbar |
                          ImGuiWindowFlags.NoCollapse |
                          ImGuiWindowFlags.NoBringToFrontOnFocus;

        ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.1f, 0.1f, 0.15f, 0.9f));

        if (ImGui.Begin("##HudBar", windowFlags))
        {
            // Left side: Gameplay hotkey hints
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
            RenderHotkeyHint("I", "Inventory", activePanel == ActivePanel.Inventory);
            ImGui.SameLine();
            ImGui.TextColored(new Vector4(0.4f, 0.4f, 0.4f, 1), "|");
            ImGui.SameLine();
            RenderHotkeyHint("J", "Journal", activePanel == ActivePanel.Journal);

            // Right side: Developer tools (F1=World Info, F12=Dev Tools)
            // Only shown when debugger is attached
            if (Debugger.IsAttached)
            {
                // Calculate right-side content width for positioning
                var f12Width = CalcHotkeyHintWidth("F12", "Dev Tools");
                var f1Width = CalcHotkeyHintWidth("F1", "World Info");
                var devToolsStartX = displaySize.X - f12Width - f1Width - 40;

                // Render developer keys at calculated position
                ImGui.SameLine(devToolsStartX);
                RenderHotkeyHint("F1", "World Info", activePanel == ActivePanel.WorldInfo, isDevelopment: true);
                ImGui.SameLine();
                ImGui.TextColored(new Vector4(0.4f, 0.4f, 0.4f, 1), "|");
                ImGui.SameLine();
                RenderHotkeyHint("F12", "Dev Tools", activePanel == ActivePanel.DevTools, isDevelopment: true);
            }
        }
        ImGui.End();

        ImGui.PopStyleColor();
    }

    /// <summary>
    /// Calculate the width of a hotkey hint (key button + label) for layout purposes.
    /// </summary>
    private float CalcHotkeyHintWidth(string key, string label)
    {
        var style = ImGui.GetStyle();
        var keySize = ImGui.CalcTextSize(key);
        var labelSize = ImGui.CalcTextSize(label);
        var buttonWidth = keySize.X + style.FramePadding.X * 2;
        return buttonWidth + style.ItemSpacing.X + labelSize.X;
    }

    private void RenderHotkeyHint(string key, string label, bool isActive, bool isDevelopment = false)
    {
        // Key box - development keys (F1, F12) get orange styling
        Vector4 keyColor;
        Vector4 textColor;

        if (isDevelopment)
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

        // Auto-size button based on text content
        var textSize = ImGui.CalcTextSize(key);
        var style = ImGui.GetStyle();
        var buttonWidth = textSize.X + style.FramePadding.X * 2;
        var buttonHeight = textSize.Y + style.FramePadding.Y * 2;
        ImGui.Button(key, new Vector2(buttonWidth, buttonHeight));

        ImGui.PopStyleColor(3);

        ImGui.SameLine();
        ImGui.TextColored(textColor, label);
    }
}
