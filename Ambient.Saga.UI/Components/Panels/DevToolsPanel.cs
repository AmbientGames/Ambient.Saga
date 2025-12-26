using Ambient.Saga.Presentation.UI.ViewModels;
using ImGuiNET;
using System.Diagnostics;
using System.Numerics;

namespace Ambient.Saga.UI.Components.Panels;

/// <summary>
/// Developer tools panel - only visible when debugger is attached.
/// Contains testing utilities, spawn commands, and debug features.
/// </summary>
public class DevToolsPanel
{
    /// <summary>
    /// Returns true if dev tools should be available (debugger attached).
    /// </summary>
    public static bool IsAvailable => Debugger.IsAttached;

    public void Render(MainViewModel viewModel)
    {
        if (!IsAvailable)
            return;

        ImGui.TextColored(new Vector4(1, 0.5f, 0, 1), "DEV TOOLS");
        ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1), "(Debugger Attached)");
        ImGui.Separator();

        // Steam Testing Section
        ImGui.Spacing();
        if (ImGui.CollapsingHeader("Steam Testing", ImGuiTreeNodeFlags.DefaultOpen))
        {
            ImGui.Indent(10);

            if (ImGui.Button("Test Achievement (ACH_HEAVY_FIRE)", new Vector2(-10, 25)))
            {
                if (viewModel.TestSteamAchievementCommand?.CanExecute(null) == true)
                {
                    viewModel.TestSteamAchievementCommand.Execute(null);
                }
            }

            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Directly set ACH_HEAVY_FIRE achievement to Steam and query status");
            }

            ImGui.Unindent(10);
        }

        // Character Spawning Section
        ImGui.Spacing();
        if (ImGui.CollapsingHeader("Character Spawning", ImGuiTreeNodeFlags.DefaultOpen))
        {
            ImGui.Indent(10);

            if (ImGui.Button("Spawn Character", new Vector2(-10, 25)))
            {
                if (viewModel.ViewCharactersCommand.CanExecute(null))
                {
                    viewModel.ViewCharactersCommand.Execute(null);
                }
            }

            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Open character selection to spawn NPCs for testing");
            }

            ImGui.Unindent(10);
        }

        // Debug Info Section
        ImGui.Spacing();
        if (ImGui.CollapsingHeader("Debug Info"))
        {
            ImGui.Indent(10);

            // Avatar position
            if (viewModel.PlayerAvatar != null)
            {
                ImGui.Text($"Avatar Position:");
                ImGui.Text($"  Lat: {viewModel.PlayerAvatar.Z:F6}");
                ImGui.Text($"  Lon: {viewModel.PlayerAvatar.X:F6}");
            }

            // Current world info
            if (viewModel.CurrentWorld != null)
            {
                ImGui.Spacing();
                ImGui.Text($"World: {viewModel.CurrentWorld.WorldConfiguration?.DisplayName ?? "Unknown"}");
                ImGui.Text($"Sagas: {viewModel.CurrentWorld.Gameplay?.SagaArcs?.Length ?? 0}");
                ImGui.Text($"Characters: {viewModel.CurrentWorld.Gameplay?.Characters?.Length ?? 0}");
            }

            // Active character count
            ImGui.Spacing();
            ImGui.Text($"Active Characters: {viewModel.Characters?.Count ?? 0}");

            ImGui.Unindent(10);
        }

        // Transaction Log Section (for Saga debugging)
        ImGui.Spacing();
        if (ImGui.CollapsingHeader("Saga Debug"))
        {
            ImGui.Indent(10);

            if (viewModel.Sagas?.Count > 0)
            {
                foreach (var saga in viewModel.Sagas.Take(5))
                {
                    ImGui.BulletText($"{saga.DisplayName}");
                }

                if (viewModel.Sagas.Count > 5)
                {
                    ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1), $"... and {viewModel.Sagas.Count - 5} more");
                }
            }
            else
            {
                ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1), "No active sagas");
            }

            ImGui.Unindent(10);
        }
    }
}
