using Ambient.Rpg.Rendering.DirectX;
using Ambient.Rpg.Ui.Components.Utilities;
using ImGuiNET;
using System.Numerics;

namespace Ambient.Rpg.Ui.Components.Panels;

/// <summary>
/// Default settings panel placeholder.
/// Shows a simple message indicating no settings are currently available.
///
/// To customize: Implement ISettingsPanel and pass to ModalManager constructor.
/// </summary>
public class DefaultSettingsPanel : ISettingsPanel
{
    public void Render(ref bool isOpen)
    {
        if (!isOpen) return;

        // Center the window
        var scale = UIConstants.DpiScale;
        ImGuiHelpers.CenterNextWindow();
        ImGui.SetNextWindowSize(new Vector2(350 * scale, 0), ImGuiCond.Always);

        // Style the window
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(20 * scale, 20 * scale));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 10f * scale);

        var windowFlags = ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.AlwaysAutoResize;

        ImGui.PushStyleColor(ImGuiCol.WindowBg, UIColors.WindowBg);

        if (ImGui.Begin("Settings", ref isOpen, windowFlags))
        {
            ImGui.Spacing();
            ImGui.Spacing();

            // Centered message
            var message = "No settings currently available.";
            var messageSize = ImGui.CalcTextSize(message);
            ImGui.SetCursorPosX((ImGui.GetWindowWidth() - messageSize.X) * 0.5f);
            ImGui.TextColored(UIColors.TextMuted, message);

            ImGui.Spacing();
            ImGui.Spacing();
            ImGui.Spacing();

            // Close button
            var buttonWidth = 120f * UIConstants.DpiScale;
            ImGui.SetCursorPosX((ImGui.GetWindowWidth() - buttonWidth) * 0.5f);

            ImGui.PushStyleColor(ImGuiCol.Button, UIColors.ButtonAccept);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, UIColors.ButtonAcceptHovered);
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, UIColors.ButtonAcceptActive);
            if (ImGui.Button("Close", new Vector2(buttonWidth, ImGui.GetFrameHeight() * 1.2f)))
            {
                isOpen = false;
            }
            ImGui.PopStyleColor(3);
        }
        ImGui.End();

        ImGui.PopStyleColor();
        ImGui.PopStyleVar(2);
    }
}
