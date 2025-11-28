using Ambient.Saga.Presentation.UI.ViewModels;
using ImGuiNET;
using System.Numerics;

namespace Ambient.Saga.Presentation.UI.Components.Modals;

/// <summary>
/// Modal showing complete world catalog
/// </summary>
public class WorldCatalogModal
{
    public void Render(MainViewModel viewModel, ref bool isOpen)
    {
        if (!isOpen) return;

        ImGui.SetNextWindowSize(new Vector2(800, 700), ImGuiCond.FirstUseEver);
        if (ImGui.Begin("World Catalog", ref isOpen))
        {
            ImGui.TextColored(new Vector4(1, 1, 0, 1), "WORLD CATALOG");
            ImGui.Separator();

            ImGui.Text("Complete world data catalog:");
            ImGui.BulletText("All items, equipment, consumables");
            ImGui.BulletText("Spells, skills, and abilities");
            ImGui.BulletText("Materials, blocks, and resources");
            ImGui.BulletText("Characters, quests, and lore");

            ImGui.Spacing();
            ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1), "(Full catalog display coming soon)");

            ImGui.End();
        }
    }
}
