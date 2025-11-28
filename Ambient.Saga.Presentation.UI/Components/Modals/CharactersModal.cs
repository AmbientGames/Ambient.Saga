using Ambient.Saga.Presentation.UI.ViewModels;
using ImGuiNET;
using System.Numerics;

namespace Ambient.Saga.Presentation.UI.Components.Modals;

/// <summary>
/// Modal showing all world characters (clickable to interact)
/// </summary>
public class CharactersModal
{
    public void Render(MainViewModel viewModel, ref bool isOpen, ModalManager modalManager)
    {
        if (!isOpen) return;

        ImGui.SetNextWindowSize(new Vector2(700, 600), ImGuiCond.FirstUseEver);
        if (ImGui.Begin("Characters", ref isOpen))
        {
            ImGui.TextColored(new Vector4(1, 1, 0, 1), "WORLD CHARACTERS");
            ImGui.Separator();

            if (viewModel.Characters.Count > 0)
            {
                if (ImGui.BeginTable("CharactersTable", 4, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY))
                {
                    ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthFixed, 150);
                    ImGui.TableSetupColumn("Type", ImGuiTableColumnFlags.WidthFixed, 100);
                    ImGui.TableSetupColumn("Location", ImGuiTableColumnFlags.WidthFixed, 150);
                    ImGui.TableSetupColumn("Status", ImGuiTableColumnFlags.WidthStretch);
                    ImGui.TableHeadersRow();

                    foreach (var character in viewModel.Characters)
                    {
                        ImGui.TableNextRow();

                        ImGui.TableNextColumn();
                        var typeColor = character.CharacterType switch
                        {
                            "Boss" => new Vector4(1, 0, 0, 1),
                            "Merchant" => new Vector4(1, 0.84f, 0, 1),
                            "Quest" => new Vector4(0, 0, 1, 1),
                            "Encounter" => new Vector4(0, 1, 0, 1),
                            _ => new Vector4(1, 1, 1, 1)
                        };

                        // Make character name clickable
                        if (ImGui.Selectable(character.DisplayName))
                        {
                            modalManager.OpenCharacterInteraction(character);
                            isOpen = false; // Close characters list when selecting
                        }

                        ImGui.TableNextColumn();
                        ImGui.TextColored(typeColor, character.CharacterType);

                        ImGui.TableNextColumn();
                        ImGui.Text($"Pixel: ({character.PixelX:F0}, {character.PixelY:F0})");

                        ImGui.TableNextColumn();
                        ImGui.Text(character.IsAlive ? "Alive" : "Defeated");
                    }

                    ImGui.EndTable();
                }
            }
            else
            {
                ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1), "No characters in current world.");
            }

            ImGui.End();
        }
    }
}
