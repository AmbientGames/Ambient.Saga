using Ambient.Saga.UI;
using Ambient.Saga.UI.Components.Utilities;
using ImGuiNET;
using System.Numerics;

namespace Ambient.Saga.UI.Components.Modals;

/// <summary>
/// Simple pause menu modal with Resume/Settings/Quit options.
/// Triggered when player presses ESC with no panels/modals open.
/// </summary>
public class PauseMenuModal
{
    public event Action? ResumeRequested;
    public event Action? SettingsRequested;
    public event Action? QuitRequested;

    public void Render(ref bool isOpen, ModalStack modalStack)
    {
        if (!isOpen)
        {
            return;
        }

        // Center the window using helper
        ImGuiHelpers.CenterNextWindow();
        ImGui.SetNextWindowSize(new Vector2(300, 300), ImGuiCond.Always);

        // Style the window
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(20, 20));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 10f);

        var windowFlags = ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoTitleBar;

        ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.08f, 0.08f, 0.12f, 0.95f));

        if (ImGui.Begin("PauseMenu", ref isOpen, windowFlags))
        {
            // Check for ESC key to close pause menu using modal stack coordination
            // Only respond if we're the top modal and ESC was just pressed
            bool escKeyDown = ImGui.IsKeyDown(ImGuiKey.Escape);
            if (modalStack.IsTopModal("PauseMenu") && modalStack.WasEscJustPressed(escKeyDown))
            {
                isOpen = false;
                ResumeRequested?.Invoke();
            }

            // Title - centered using available width
            ImGui.Spacing();
            ImGui.PushFont(UIConstants.FontTitle);
            var titleText = "PAUSED";
            var titleSize = ImGui.CalcTextSize(titleText);
            var avail = ImGui.GetContentRegionAvail();
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + (avail.X - titleSize.X) * 0.5f);
            ImGui.TextColored(new Vector4(1, 0.9f, 0.5f, 1), titleText);
            ImGui.PopFont();

            ImGui.Spacing();
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();
            ImGui.Spacing();

            // Use frame height for consistent button sizing
            var buttonHeight = ImGui.GetFrameHeight() * 1.2f;

            // Resume button - full width
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.2f, 0.4f, 0.2f, 1));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.3f, 0.55f, 0.3f, 1));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.4f, 0.7f, 0.4f, 1));
            if (ImGui.Button("Resume Game", new Vector2(ImGuiSizes.Fill, buttonHeight)))
            {
                isOpen = false;
                ResumeRequested?.Invoke();
            }
            ImGui.PopStyleColor(3);

            ImGui.Spacing();

            // Settings button - full width
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.3f, 0.3f, 0.35f, 1));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.4f, 0.4f, 0.45f, 1));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.5f, 0.5f, 0.55f, 1));
            if (ImGui.Button("Settings", new Vector2(ImGuiSizes.Fill, buttonHeight)))
            {
                SettingsRequested?.Invoke();
            }
            ImGui.PopStyleColor(3);

            ImGui.Spacing();

            // Quit button - full width
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.4f, 0.15f, 0.15f, 1));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.5f, 0.2f, 0.2f, 1));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.6f, 0.25f, 0.25f, 1));
            if (ImGui.Button("Quit to Desktop", new Vector2(ImGuiSizes.Fill, buttonHeight)))
            {
                QuitRequested?.Invoke();
            }
            ImGui.PopStyleColor(3);

            ImGui.Spacing();
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            // Hint - centered using available width
            var hintText = "Press ESC to resume";
            var hintSize = ImGui.CalcTextSize(hintText);
            var hintAvail = ImGui.GetContentRegionAvail();
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + (hintAvail.X - hintSize.X) * 0.5f);
            ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1), hintText);
        }
        ImGui.End();

        ImGui.PopStyleColor();
        ImGui.PopStyleVar(2);
    }
}
