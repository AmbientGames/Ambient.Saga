using Ambient.Saga.Presentation.UI.ViewModels;
using ImGuiNET;
using System.Numerics;

namespace Ambient.Saga.Presentation.UI.Components.Modals;

/// <summary>
/// Modal showing achievement progress
/// </summary>
public class AchievementsModal
{
    public void Render(MainViewModel viewModel, ref bool isOpen)
    {
        if (!isOpen) return;

        ImGui.SetNextWindowSize(new Vector2(600, 500), ImGuiCond.FirstUseEver);
        if (ImGui.Begin("Achievements", ref isOpen))
        {
            ImGui.TextColored(new Vector4(1, 1, 0, 1), "ACHIEVEMENTS");
            ImGui.Separator();

            ImGui.Text("Achievement system:");
            ImGui.BulletText("Event-sourced from transaction log");
            ImGui.BulletText("Combat victories, discoveries, social interactions");
            ImGui.BulletText("Progress tracking and Steam sync");

            ImGui.Spacing();
            ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1), "(Full achievement display coming soon)");

            ImGui.End();
        }
    }
}
