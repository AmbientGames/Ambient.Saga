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

    public void Render(MainViewModel viewModel, CharacterViewModel character, ref bool isOpen)
    {
        if (!isOpen)
        {
            _hasLooted = false;
            return;
        }

        ImGui.SetNextWindowSize(new Vector2(600, 400), ImGuiCond.FirstUseEver);
        if (ImGui.Begin($"Loot: {character.DisplayName}", ref isOpen))
        {
            ImGui.TextColored(new Vector4(1, 0, 0, 1), $"{character.DisplayName} (Defeated)");
            ImGui.Separator();
            ImGui.Spacing();

            if (_hasLooted)
            {
                ImGui.TextWrapped("You have already looted this character.");
                ImGui.Spacing();
                if (ImGui.Button("Close", new Vector2(120, 30)))
                {
                    isOpen = false;
                }
            }
            else if (character.HasBeenLooted)
            {
                ImGui.TextWrapped("This character has already been looted.");
                ImGui.Spacing();
                if (ImGui.Button("Close", new Vector2(120, 30)))
                {
                    isOpen = false;
                }
            }
            else
            {
                ImGui.TextWrapped("The defeated enemy has dropped the following items:");
                ImGui.Spacing();

                // Show available loot (from character template)
                var characterTemplate = viewModel.CurrentWorld?.Gameplay?.Characters?
                    .FirstOrDefault(c => c.RefName == character.CharacterRef);

                if (characterTemplate?.Capabilities != null)
                {
                    ImGui.BeginChild("LootList", new Vector2(0, 250), ImGuiChildFlags.Borders);

                    var hasAnyLoot = false;

                    if (characterTemplate.Capabilities.Equipment?.Length > 0)
                    {
                        ImGui.TextColored(new Vector4(1, 0.84f, 0, 1), "Equipment:");
                        foreach (var equipment in characterTemplate.Capabilities.Equipment)
                        {
                            ImGui.BulletText($"{equipment.EquipmentRef} (Condition: {equipment.Condition:P0})");
                            hasAnyLoot = true;
                        }
                        ImGui.Spacing();
                    }

                    if (characterTemplate.Capabilities.Consumables?.Length > 0)
                    {
                        ImGui.TextColored(new Vector4(0, 1, 0, 1), "Consumables:");
                        foreach (var consumable in characterTemplate.Capabilities.Consumables)
                        {
                            ImGui.BulletText($"{consumable.ConsumableRef} x{consumable.Quantity}");
                            hasAnyLoot = true;
                        }
                        ImGui.Spacing();
                    }

                    if (characterTemplate.Capabilities.BuildingMaterials?.Length > 0)
                    {
                        ImGui.TextColored(new Vector4(0.6f, 0.4f, 0.2f, 1), "Materials:");
                        foreach (var material in characterTemplate.Capabilities.BuildingMaterials)
                        {
                            ImGui.BulletText($"{material.BuildingMaterialRef} x{material.Quantity}");
                            hasAnyLoot = true;
                        }
                        ImGui.Spacing();
                    }

                    if (!hasAnyLoot)
                    {
                        ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1), "No items to loot.");
                    }

                    ImGui.EndChild();
                }

                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();

                if (ImGui.Button("Take All", new Vector2(150, 30)))
                {
                    _ = LootCharacterAsync(viewModel, character);
                }

                ImGui.SameLine();

                if (ImGui.Button("Leave", new Vector2(150, 30)))
                {
                    isOpen = false;
                }
            }

            ImGui.End();
        }
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
    }
}
