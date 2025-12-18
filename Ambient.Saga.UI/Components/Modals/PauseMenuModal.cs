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

    public void Render(ref bool isOpen)
    {
        if (!isOpen) return;

        // Center the window
        var io = ImGui.GetIO();
        ImGui.SetNextWindowPos(new Vector2(io.DisplaySize.X * 0.5f, io.DisplaySize.Y * 0.5f), ImGuiCond.Always, new Vector2(0.5f, 0.5f));
        ImGui.SetNextWindowSize(new Vector2(300, 250), ImGuiCond.Always);

        // Style the window
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(20, 20));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 10f);

        var windowFlags = ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoTitleBar;

        ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.08f, 0.08f, 0.12f, 0.95f));

        if (ImGui.Begin("PauseMenu", ref isOpen, windowFlags))
        {
            // Title
            ImGui.Spacing();
            ImGui.SetWindowFontScale(1.3f);
            var titleText = "PAUSED";
            var titleSize = ImGui.CalcTextSize(titleText);
            ImGui.SetCursorPosX((ImGui.GetWindowWidth() - titleSize.X) * 0.5f);
            ImGui.TextColored(new Vector4(1, 0.9f, 0.5f, 1), titleText);
            ImGui.SetWindowFontScale(1.0f);

            ImGui.Spacing();
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();
            ImGui.Spacing();

            var buttonWidth = 260f;
            var buttonHeight = 40f;

            // Center buttons
            ImGui.SetCursorPosX((ImGui.GetWindowWidth() - buttonWidth) * 0.5f);

            // Resume button
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.2f, 0.4f, 0.2f, 1));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.3f, 0.55f, 0.3f, 1));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.4f, 0.7f, 0.4f, 1));
            if (ImGui.Button("Resume Game", new Vector2(buttonWidth, buttonHeight)))
            {
                isOpen = false;
                ResumeRequested?.Invoke();
            }
            ImGui.PopStyleColor(3);

            ImGui.Spacing();
            ImGui.SetCursorPosX((ImGui.GetWindowWidth() - buttonWidth) * 0.5f);

            // Settings button
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.3f, 0.3f, 0.35f, 1));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.4f, 0.4f, 0.45f, 1));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.5f, 0.5f, 0.55f, 1));
            if (ImGui.Button("Settings", new Vector2(buttonWidth, buttonHeight)))
            {
                SettingsRequested?.Invoke();
            }
            ImGui.PopStyleColor(3);

            ImGui.Spacing();
            ImGui.SetCursorPosX((ImGui.GetWindowWidth() - buttonWidth) * 0.5f);

            // Quit button
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.4f, 0.15f, 0.15f, 1));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.5f, 0.2f, 0.2f, 1));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.6f, 0.25f, 0.25f, 1));
            if (ImGui.Button("Quit to Desktop", new Vector2(buttonWidth, buttonHeight)))
            {
                QuitRequested?.Invoke();
            }
            ImGui.PopStyleColor(3);

            ImGui.Spacing();
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            // Hint
            var hintText = "Press ESC to resume";
            var hintSize = ImGui.CalcTextSize(hintText);
            ImGui.SetCursorPosX((ImGui.GetWindowWidth() - hintSize.X) * 0.5f);
            ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1), hintText);
        }
        ImGui.End();

        ImGui.PopStyleColor();
        ImGui.PopStyleVar(2);
    }
}
