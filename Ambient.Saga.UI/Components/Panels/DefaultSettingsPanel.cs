using ImGuiNET;
using System.Numerics;

namespace Ambient.Saga.UI.Components.Panels;

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
        var io = ImGui.GetIO();
        ImGui.SetNextWindowPos(new Vector2(io.DisplaySize.X * 0.5f, io.DisplaySize.Y * 0.5f), ImGuiCond.Always, new Vector2(0.5f, 0.5f));
        ImGui.SetNextWindowSize(new Vector2(350, 200), ImGuiCond.Always);

        // Style the window
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(20, 20));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 10f);

        var windowFlags = ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove;

        ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.08f, 0.08f, 0.12f, 0.95f));

        if (ImGui.Begin("Settings", ref isOpen, windowFlags))
        {
            ImGui.Spacing();
            ImGui.Spacing();

            // Centered message
            var message = "No settings currently available.";
            var messageSize = ImGui.CalcTextSize(message);
            ImGui.SetCursorPosX((ImGui.GetWindowWidth() - messageSize.X) * 0.5f);
            ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1), message);

            ImGui.Spacing();
            ImGui.Spacing();
            ImGui.Spacing();

            // Close button
            var buttonWidth = 120f;
            ImGui.SetCursorPosX((ImGui.GetWindowWidth() - buttonWidth) * 0.5f);

            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.2f, 0.4f, 0.2f, 1));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.3f, 0.55f, 0.3f, 1));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.4f, 0.7f, 0.4f, 1));
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
