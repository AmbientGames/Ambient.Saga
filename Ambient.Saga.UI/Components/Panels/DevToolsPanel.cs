using Ambient.Saga.Presentation.UI.ViewModels;
using Ambient.Saga.UI.Components.Modals;
using ImGuiNET;
using System.Diagnostics;
using System.Numerics;

namespace Ambient.Saga.UI.Components.Panels;

/// <summary>
/// Types of dev test characters that can be spawned for interaction testing.
/// </summary>
public enum DevCharacterType
{
    /// <summary>Friendly NPC for dialogue testing</summary>
    NPC,
    /// <summary>Merchant for trade window testing</summary>
    Merchant,
    /// <summary>Boss enemy for combat testing</summary>
    Boss,
    /// <summary>Regular hostile for combat testing</summary>
    Hostile
}

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

    public void Render(SagaMainViewModel viewModel, ModalManager modalManager)
    {
        if (!IsAvailable)
            return;

        ImGui.TextColored(new Vector4(1, 0.5f, 0, 1), "DEV TOOLS");
        ImGui.TextColored(UIColors.TextDisabled, "(Debugger Attached)");
        ImGui.Separator();

        // Steam Testing Section
        ImGui.Spacing();
        var devButtonHeight = ImGui.GetFrameHeight();
        if (ImGui.CollapsingHeader("Steam Testing", ImGuiTreeNodeFlags.DefaultOpen))
        {
            ImGui.Indent(10 * UIConstants.DpiScale);

            if (ImGui.Button("Test Achievement (ACH_HEAVY_FIRE)", new Vector2(-10, devButtonHeight)))
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

            ImGui.Unindent(10 * UIConstants.DpiScale);
        }

        // Interaction Testing Section
        ImGui.Spacing();
        if (ImGui.CollapsingHeader("Interaction Testing", ImGuiTreeNodeFlags.DefaultOpen))
        {
            ImGui.Indent(10 * UIConstants.DpiScale);

            ImGui.TextColored(UIColors.TextMuted, "Spawn & interact with test characters:");
            ImGui.Spacing();

            // NPC Dialogue Test
            if (ImGui.Button("Test NPC Dialogue", new Vector2(-10, devButtonHeight)))
            {
                _ = SpawnAndOpenModalAsync(viewModel, modalManager, DevCharacterType.NPC, "Dialogue");
            }
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Spawn a friendly NPC and start dialogue");
            }

            // Merchant Dialogue (can transition to trade)
            if (ImGui.Button("Test Merchant Dialogue", new Vector2(-10, devButtonHeight)))
            {
                _ = SpawnAndOpenModalAsync(viewModel, modalManager, DevCharacterType.Merchant, "Dialogue");
            }
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Spawn a merchant with dialogue (test dialogue -> trade transition)");
            }

            // Boss Dialogue (can transition to combat)
            if (ImGui.Button("Test Boss Dialogue", new Vector2(-10, devButtonHeight)))
            {
                _ = SpawnAndOpenModalAsync(viewModel, modalManager, DevCharacterType.Boss, "Dialogue");
            }
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Spawn a boss with dialogue (test dialogue -> combat transition)");
            }

            // Hostile Dialogue (can transition to combat)
            if (ImGui.Button("Test Hostile Dialogue", new Vector2(-10, devButtonHeight)))
            {
                _ = SpawnAndOpenModalAsync(viewModel, modalManager, DevCharacterType.Hostile, "Dialogue");
            }
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Spawn a hostile with dialogue (test dialogue -> combat transition)");
            }

            ImGui.Spacing();
            ImGui.TextColored(UIColors.TextDisabled, "Direct modal tests:");

            // Direct Trade Modal
            if (ImGui.Button("Direct Trade Modal", new Vector2(-10, devButtonHeight)))
            {
                _ = SpawnAndOpenModalAsync(viewModel, modalManager, DevCharacterType.Merchant, "MerchantTrade");
            }
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Open trade window directly (skip dialogue)");
            }

            // Direct Battle Modal
            if (ImGui.Button("Direct Battle Modal", new Vector2(-10, devButtonHeight)))
            {
                _ = SpawnAndOpenModalAsync(viewModel, modalManager, DevCharacterType.Boss, "BossBattle");
            }
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Open battle window directly (skip dialogue)");
            }

            ImGui.Unindent(10 * UIConstants.DpiScale);
        }

        // Character Spawning Section (legacy - view characters)
        ImGui.Spacing();
        if (ImGui.CollapsingHeader("Character Browser"))
        {
            ImGui.Indent(10 * UIConstants.DpiScale);

            if (ImGui.Button("View All Characters", new Vector2(-10, devButtonHeight)))
            {
                if (viewModel.ViewCharactersCommand.CanExecute(null))
                {
                    viewModel.ViewCharactersCommand.Execute(null);
                }
            }

            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Open character list to browse defined characters");
            }

            ImGui.Unindent(10 * UIConstants.DpiScale);
        }

        // Debug Info Section
        ImGui.Spacing();
        if (ImGui.CollapsingHeader("Debug Info"))
        {
            ImGui.Indent(10 * UIConstants.DpiScale);

            // Avatar position
            if (viewModel.Avatar != null)
            {
                ImGui.Text($"Avatar Position:");
                ImGui.Text($"  Lat: {viewModel.Avatar.Z:F6}");
                ImGui.Text($"  Lon: {viewModel.Avatar.X:F6}");
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

            ImGui.Unindent(10 * UIConstants.DpiScale);
        }

        // Transaction Log Section (for Saga debugging)
        ImGui.Spacing();
        if (ImGui.CollapsingHeader("Saga Debug"))
        {
            ImGui.Indent(10 * UIConstants.DpiScale);

            if (viewModel.Sagas?.Count > 0)
            {
                foreach (var saga in viewModel.Sagas.Take(5))
                {
                    ImGui.BulletText($"{saga.DisplayName}");
                }

                if (viewModel.Sagas.Count > 5)
                {
                    ImGui.TextColored(UIColors.TextDisabled, $"... and {viewModel.Sagas.Count - 5} more");
                }
            }
            else
            {
                ImGui.TextColored(UIColors.TextDisabled, "No active sagas");
            }

            ImGui.Unindent(10 * UIConstants.DpiScale);
        }
    }

    /// <summary>
    /// Helper to spawn a dev character and open the appropriate modal.
    /// </summary>
    private static async Task SpawnAndOpenModalAsync(
        SagaMainViewModel viewModel,
        ModalManager modalManager,
        DevCharacterType characterType,
        string modalName)
    {
        try
        {
            var character = await viewModel.SpawnDevCharacterAsync(characterType);
            if (character != null)
            {
                var context = new CharacterContext(viewModel, character);
                modalManager.OpenRegisteredModal(modalName, context);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[DevTools] Error in SpawnAndOpenModalAsync: {ex.Message}");
            viewModel.AddToastMessage($"Error: {ex.Message}", Overlay.MessageType.Error, 5f);
        }
    }
}
