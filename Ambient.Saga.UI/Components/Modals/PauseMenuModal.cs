using Ambient.Saga.UI;
using Ambient.Saga.UI.Components.Utilities;
using ImGuiNET;
using System.Numerics;

namespace Ambient.Saga.UI.Components.Modals;

/// <summary>
/// Simple pause menu modal with Resume/Settings/Quit options.
/// Triggered when avatar presses ESC with no panels/modals open.
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
        var scale = UIConstants.DpiScale;
        ImGui.SetNextWindowSize(new Vector2(300 * scale, 0), ImGuiCond.Always);

        // Style the window
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(20 * scale, 20 * scale));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 10f * scale);

        var windowFlags = ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.AlwaysAutoResize;

        ImGui.PushStyleColor(ImGuiCol.WindowBg, UIColors.WindowBg);

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
            ImGui.TextColored(UIColors.GoldenYellow, titleText);
            ImGui.PopFont();

            ImGui.Spacing();
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();
            ImGui.Spacing();

            // Use frame height for consistent button sizing
            var buttonHeight = ImGui.GetFrameHeight() * 1.2f;

            // Resume button - full width
            ImGui.PushStyleColor(ImGuiCol.Button, UIColors.ButtonAccept);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, UIColors.ButtonAcceptHovered);
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, UIColors.ButtonAcceptActive);
            if (ImGui.Button("Resume Game", new Vector2(ImGuiSizes.Fill, buttonHeight)))
            {
                isOpen = false;
                ResumeRequested?.Invoke();
            }
            ImGui.PopStyleColor(3);

            ImGui.Spacing();

            // Settings button - full width
            ImGui.PushStyleColor(ImGuiCol.Button, UIColors.ButtonNeutral);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, UIColors.ButtonNeutralHovered);
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, UIColors.ButtonNeutralActive);
            if (ImGui.Button("Settings", new Vector2(ImGuiSizes.Fill, buttonHeight)))
            {
                SettingsRequested?.Invoke();
            }
            ImGui.PopStyleColor(3);

            ImGui.Spacing();

            // Quit button - full width
            ImGui.PushStyleColor(ImGuiCol.Button, UIColors.ButtonDanger);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, UIColors.ButtonDangerHovered);
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, UIColors.ButtonDangerActive);
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
            ImGui.TextColored(UIColors.TextDim, hintText);
        }
        ImGui.End();

        ImGui.PopStyleColor();
        ImGui.PopStyleVar(2);
    }
}
