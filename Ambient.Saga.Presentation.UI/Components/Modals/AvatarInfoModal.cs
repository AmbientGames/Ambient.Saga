using Ambient.Saga.Presentation.UI.ViewModels;
using ImGuiNET;
using System.Numerics;

namespace Ambient.Saga.Presentation.UI.Components.Modals;

/// <summary>
/// Modal showing avatar stats and inventory
/// </summary>
public class AvatarInfoModal
{
    public void Render(MainViewModel viewModel, ref bool isOpen)
    {
        if (!isOpen) return;

        ImGui.SetNextWindowSize(new Vector2(500, 600), ImGuiCond.FirstUseEver);
        if (ImGui.Begin("Avatar Info", ref isOpen))
        {
            if (viewModel.PlayerAvatar != null)
            {
                var avatar = viewModel.PlayerAvatar;

                ImGui.TextColored(new Vector4(1, 1, 0, 1), avatar.DisplayName ?? "Avatar");
                ImGui.TextWrapped(avatar.Description ?? "");

                ImGui.Separator();
                ImGui.TextColored(new Vector4(0.5f, 1, 0.5f, 1), "Stats:");

                if (avatar.Stats != null)
                {
                    RenderStatBar("Health", avatar.Stats.Health, new Vector4(1, 0, 0, 1));
                    RenderStatBar("Stamina", avatar.Stats.Stamina, new Vector4(0, 1, 0, 1));
                    RenderStatBar("Mana", avatar.Stats.Mana, new Vector4(0, 0, 1, 1));
                    RenderStatBar("Strength", avatar.Stats.Strength, new Vector4(1, 0.5f, 0, 1));
                    RenderStatBar("Defense", avatar.Stats.Defense, new Vector4(0.5f, 0.5f, 0.5f, 1));
                    RenderStatBar("Speed", avatar.Stats.Speed, new Vector4(1, 1, 0, 1));
                    RenderStatBar("Magic", avatar.Stats.Magic, new Vector4(0.5f, 0, 1, 1));
                }

                ImGui.Separator();
                ImGui.TextColored(new Vector4(0.5f, 1, 0.5f, 1), "Inventory:");
                ImGui.Text("Equipment, consumables, spells, tools...");
                ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1), "(Full inventory display coming soon)");
            }
            else
            {
                ImGui.TextColored(new Vector4(1, 0, 0, 1), "No avatar created yet.");
                ImGui.Text("Click 'Select Archetype' to create your character.");
            }

            ImGui.End();
        }
    }

    private void RenderStatBar(string label, double value, Vector4 color)
    {
        ImGui.Text($"{label}:");
        ImGui.SameLine(100);
        ImGui.PushStyleColor(ImGuiCol.PlotHistogram, color);
        ImGui.ProgressBar((float)value, new Vector2(-1, 20), $"{value * 100:F0}%");
        ImGui.PopStyleColor();
    }
}
