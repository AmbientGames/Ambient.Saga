using ImGuiNET;
using System.Numerics;

namespace Ambient.Saga.UI.Components.Panels;

/// <summary>
/// Default settings panel with common game settings.
/// Provides a basic template that clients can replace with their own ISettingsPanel implementation.
/// 
/// Current settings:
/// - Audio volume (placeholder - not functional)
/// - Graphics quality (placeholder - not functional)
/// - Controls display (informational)
/// 
/// To customize: Implement ISettingsPanel and pass to ModalManager constructor.
/// </summary>
public class DefaultSettingsPanel : ISettingsPanel
{
    private float _masterVolume = 0.8f;
    private float _musicVolume = 0.7f;
    private float _sfxVolume = 0.9f;
    private int _graphicsQuality = 1; // 0=Low, 1=Medium, 2=High
    private bool _fullscreen = true;
    private bool _vsync = true;

    public void Render(ref bool isOpen)
    {
        if (!isOpen) return;

        // Center the window
        var io = ImGui.GetIO();
        ImGui.SetNextWindowPos(new Vector2(io.DisplaySize.X * 0.5f, io.DisplaySize.Y * 0.5f), ImGuiCond.FirstUseEver, new Vector2(0.5f, 0.5f));
        ImGui.SetNextWindowSize(new Vector2(500, 600), ImGuiCond.FirstUseEver);

        // Style the window
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(20, 20));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 10f);

        var windowFlags = ImGuiWindowFlags.NoCollapse;

        ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.08f, 0.08f, 0.12f, 0.95f));

        if (ImGui.Begin("Settings", ref isOpen, windowFlags))
        {
            // Title
            ImGui.SetWindowFontScale(1.2f);
            ImGui.TextColored(new Vector4(1, 0.9f, 0.5f, 1), "Game Settings");
            ImGui.SetWindowFontScale(1.0f);
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            // Audio Section
            if (ImGui.CollapsingHeader("Audio", ImGuiTreeNodeFlags.DefaultOpen))
            {
                ImGui.Indent(10);
                
                ImGui.Text("Master Volume:");
                ImGui.SliderFloat("##MasterVolume", ref _masterVolume, 0.0f, 1.0f, $"{_masterVolume * 100:F0}%%");
                ImGui.Spacing();

                ImGui.Text("Music Volume:");
                ImGui.SliderFloat("##MusicVolume", ref _musicVolume, 0.0f, 1.0f, $"{_musicVolume * 100:F0}%%");
                ImGui.Spacing();

                ImGui.Text("Sound Effects:");
                ImGui.SliderFloat("##SFXVolume", ref _sfxVolume, 0.0f, 1.0f, $"{_sfxVolume * 100:F0}%%");
                ImGui.Spacing();

                ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.4f, 1), "? Audio settings are not functional in this demo");
                
                ImGui.Unindent(10);
                ImGui.Spacing();
            }

            // Graphics Section
            if (ImGui.CollapsingHeader("Graphics", ImGuiTreeNodeFlags.DefaultOpen))
            {
                ImGui.Indent(10);
                
                ImGui.Text("Graphics Quality:");
                var qualityNames = new[] { "Low", "Medium", "High" };
                ImGui.Combo("##GraphicsQuality", ref _graphicsQuality, qualityNames, qualityNames.Length);
                ImGui.Spacing();

                ImGui.Checkbox("Fullscreen", ref _fullscreen);
                ImGui.Checkbox("V-Sync", ref _vsync);
                ImGui.Spacing();

                ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.4f, 1), "? Graphics settings are not functional in this demo");
                
                ImGui.Unindent(10);
                ImGui.Spacing();
            }

            // Controls Section
            if (ImGui.CollapsingHeader("Controls", ImGuiTreeNodeFlags.DefaultOpen))
            {
                ImGui.Indent(10);
                
                ImGui.TextColored(new Vector4(0.9f, 0.9f, 0.6f, 1), "Keyboard Controls:");
                ImGui.Spacing();
                
                ImGui.BulletText("M - Toggle Map");
                ImGui.BulletText("C - Toggle Character Info");
                ImGui.BulletText("I - Toggle World Info");
                ImGui.BulletText("ESC - Close Panel / Pause Menu");
                ImGui.Spacing();

                ImGui.TextColored(new Vector4(0.9f, 0.9f, 0.6f, 1), "Mouse Controls:");
                ImGui.Spacing();
                
                ImGui.BulletText("Left Click - Move Avatar / Interact");
                ImGui.BulletText("Right Drag - Pan Map (when zoomed)");
                ImGui.BulletText("Scroll - Zoom Map");
                ImGui.Spacing();

                ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.4f, 1), "?? Controls can be customized via IInputHandler");
                
                ImGui.Unindent(10);
                ImGui.Spacing();
            }

            // Info Section
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();
            
            ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1), "This is a default settings panel.");
            ImGui.TextWrapped("Replace it with your own by implementing ISettingsPanel and passing it to ModalManager.");

            ImGui.Spacing();
            ImGui.Spacing();

            // Close button
            var buttonWidth = 150f;
            ImGui.SetCursorPosX((ImGui.GetWindowWidth() - buttonWidth) * 0.5f);
            
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.2f, 0.4f, 0.2f, 1));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.3f, 0.55f, 0.3f, 1));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.4f, 0.7f, 0.4f, 1));
            if (ImGui.Button("Close", new Vector2(buttonWidth, 40)))
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
