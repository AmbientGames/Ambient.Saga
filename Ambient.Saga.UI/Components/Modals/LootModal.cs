using Ambient.Saga.Presentation.UI.ViewModels;
using Ambient.Saga.Engine.Application.Commands.Saga;
using ImGuiNET;
using System.Numerics;

namespace Ambient.Saga.Presentation.UI.Components.Modals;

/// <summary>
/// Modal for looting defeated characters using CQRS pattern
/// </summary>
public class LootModal
{
    private bool _hasLooted = false;
    private bool _isLooting = false;

    public void Render(MainViewModel viewModel, CharacterViewModel character, ref bool isOpen)
    {
        if (!isOpen)
        {
            _hasLooted = false;
            _isLooting = false;
            return;
        }

        // Center the window
        var io = ImGui.GetIO();
        ImGui.SetNextWindowPos(new Vector2(io.DisplaySize.X * 0.5f, io.DisplaySize.Y * 0.5f), ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));
        ImGui.SetNextWindowSize(new Vector2(550, 450), ImGuiCond.FirstUseEver);

        // Style the window
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(20, 20));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 10f);

        var windowFlags = ImGuiWindowFlags.NoCollapse;

        if (ImGui.Begin($"Loot###LootModal", ref isOpen, windowFlags))
        {
            // Header showing defeated enemy
            ImGui.SetWindowFontScale(1.2f);
            ImGui.TextColored(new Vector4(1, 0.4f, 0.4f, 1), character.DisplayName);
            ImGui.SetWindowFontScale(1.0f);
            ImGui.SameLine();
            ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1), "- Defeated");

            ImGui.Separator();
            ImGui.Spacing();

            if (_hasLooted)
            {
                RenderAlreadyLooted(ref isOpen);
            }
            else if (character.HasBeenLooted)
            {
                RenderAlreadyLooted(ref isOpen);
            }
            else
            {
                RenderLootAvailable(viewModel, character, ref isOpen);
            }
        }
        ImGui.End();

        ImGui.PopStyleVar(2);
    }

    private void RenderAlreadyLooted(ref bool isOpen)
    {
        ImGui.Spacing();
        ImGui.Spacing();

        var text = "You have already looted this enemy.";
        var textSize = ImGui.CalcTextSize(text);
        ImGui.SetCursorPosX((ImGui.GetWindowWidth() - textSize.X) * 0.5f);
        ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1), text);

        ImGui.Spacing();
        ImGui.Spacing();
        ImGui.Spacing();

        var buttonWidth = 120f;
        ImGui.SetCursorPosX((ImGui.GetWindowWidth() - buttonWidth) * 0.5f);
        if (ImGui.Button("Close", new Vector2(buttonWidth, 35)))
        {
            isOpen = false;
        }
    }

    private void RenderLootAvailable(MainViewModel viewModel, CharacterViewModel character, ref bool isOpen)
    {
        ImGui.TextColored(new Vector4(0.9f, 0.9f, 0.6f, 1), "Dropped Items:");
        ImGui.Spacing();

        // Show available loot (from character template)
        var characterTemplate = viewModel.CurrentWorld?.Gameplay?.Characters?
            .FirstOrDefault(c => c.RefName == character.CharacterRef);

        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.05f, 0.05f, 0.08f, 0.9f));
        ImGui.BeginChild("LootList", new Vector2(0, -60), ImGuiChildFlags.Borders);

        if (characterTemplate?.Capabilities != null)
        {
            var hasAnyLoot = false;

            if (characterTemplate.Capabilities.Equipment?.Length > 0)
            {
                ImGui.Spacing();
                ImGui.TextColored(new Vector4(0.6f, 0.7f, 1, 1), "Equipment:");
                foreach (var equipment in characterTemplate.Capabilities.Equipment)
                {
                    ImGui.Indent(15);
                    ImGui.TextColored(new Vector4(0.9f, 0.9f, 0.85f, 1), equipment.EquipmentRef);
                    ImGui.SameLine();
                    ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1), $"({equipment.Condition:P0})");
                    ImGui.Unindent(15);
                    hasAnyLoot = true;
                }
                ImGui.Spacing();
            }

            if (characterTemplate.Capabilities.Consumables?.Length > 0)
            {
                ImGui.Spacing();
                ImGui.TextColored(new Vector4(0.4f, 1, 0.6f, 1), "Consumables:");
                foreach (var consumable in characterTemplate.Capabilities.Consumables)
                {
                    ImGui.Indent(15);
                    ImGui.TextColored(new Vector4(0.9f, 0.9f, 0.85f, 1), consumable.ConsumableRef);
                    ImGui.SameLine();
                    ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1), $"x{consumable.Quantity}");
                    ImGui.Unindent(15);
                    hasAnyLoot = true;
                }
                ImGui.Spacing();
            }

            if (characterTemplate.Capabilities.BuildingMaterials?.Length > 0)
            {
                ImGui.Spacing();
                ImGui.TextColored(new Vector4(0.8f, 0.6f, 0.4f, 1), "Materials:");
                foreach (var material in characterTemplate.Capabilities.BuildingMaterials)
                {
                    ImGui.Indent(15);
                    ImGui.TextColored(new Vector4(0.9f, 0.9f, 0.85f, 1), material.BuildingMaterialRef);
                    ImGui.SameLine();
                    ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1), $"x{material.Quantity}");
                    ImGui.Unindent(15);
                    hasAnyLoot = true;
                }
                ImGui.Spacing();
            }

            if (characterTemplate.Capabilities.Tools?.Length > 0)
            {
                ImGui.Spacing();
                ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.9f, 1), "Tools:");
                foreach (var tool in characterTemplate.Capabilities.Tools)
                {
                    ImGui.Indent(15);
                    ImGui.TextColored(new Vector4(0.9f, 0.9f, 0.85f, 1), tool.ToolRef);
                    ImGui.SameLine();
                    ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1), $"({tool.Condition:P0})");
                    ImGui.Unindent(15);
                    hasAnyLoot = true;
                }
                ImGui.Spacing();
            }

            if (!hasAnyLoot)
            {
                ImGui.Spacing();
                var noLootText = "No items to loot.";
                var textSize = ImGui.CalcTextSize(noLootText);
                ImGui.SetCursorPosX((ImGui.GetWindowWidth() - textSize.X) * 0.5f);
                ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1), noLootText);
            }
        }
        else
        {
            ImGui.Spacing();
            var noLootText = "No items to loot.";
            var textSize = ImGui.CalcTextSize(noLootText);
            ImGui.SetCursorPosX((ImGui.GetWindowWidth() - textSize.X) * 0.5f);
            ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1), noLootText);
        }

        ImGui.EndChild();
        ImGui.PopStyleColor();

        ImGui.Spacing();

        // Action buttons centered
        var buttonWidth = 130f;
        var totalWidth = buttonWidth * 2 + 20;
        ImGui.SetCursorPosX((ImGui.GetWindowWidth() - totalWidth) * 0.5f);

        if (_isLooting)
        {
            ImGui.BeginDisabled();
        }

        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.2f, 0.4f, 0.2f, 1));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.3f, 0.55f, 0.3f, 1));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.4f, 0.7f, 0.4f, 1));
        if (ImGui.Button(_isLooting ? "Looting..." : "Take All", new Vector2(buttonWidth, 38)) && !_isLooting)
        {
            _isLooting = true;
            _ = LootCharacterAsync(viewModel, character);
        }
        ImGui.PopStyleColor(3);

        if (_isLooting)
        {
            ImGui.EndDisabled();
        }

        ImGui.SameLine();

        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.4f, 0.25f, 0.25f, 1));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.5f, 0.3f, 0.3f, 1));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.6f, 0.35f, 0.35f, 1));
        if (ImGui.Button("Leave", new Vector2(buttonWidth, 38)))
        {
            isOpen = false;
        }
        ImGui.PopStyleColor(3);
    }

    private async Task LootCharacterAsync(MainViewModel viewModel, CharacterViewModel character)
    {
        if (viewModel.CurrentWorld == null || viewModel.PlayerAvatar == null)
            return;

        try
        {
            var command = new LootCharacterCommand
            {
                AvatarId = viewModel.PlayerAvatar.AvatarId,
                SagaArcRef = character.SagaRef,
                CharacterInstanceId = character.CharacterInstanceId,
                Avatar = viewModel.PlayerAvatar
            };

            var result = await viewModel.Mediator.Send(command);

            if (result.Successful)
            {
                _hasLooted = true;
                character.HasBeenLooted = true;
                viewModel.ActivityLog?.Insert(0, $"💰 Looted {character.DisplayName}");

                // Update avatar from result if available
                if (result.UpdatedAvatar != null)
                {
                    viewModel.PlayerAvatar = result.UpdatedAvatar;
                }
            }
            else
            {
                viewModel.ActivityLog?.Insert(0, $"❌ Failed to loot: {result.ErrorMessage}");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error looting character: {ex.Message}");
            viewModel.ActivityLog?.Insert(0, $"❌ Error looting: {ex.Message}");
        }
        finally
        {
            _isLooting = false;
        }
    }
}
