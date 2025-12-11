using Ambient.Saga.Presentation.UI.ViewModels;
using Ambient.Presentation.WindowsUI.RpgControls.ViewModels;
using ImGuiNET;
using System.Numerics;

namespace Ambient.Saga.UI.Components.Modals;

/// <summary>
/// Quest log modal showing all active and completed quests.
/// Click on a quest to open detailed view.
/// </summary>
public class QuestLogModal
{
    private bool _showCompleted = false;
    private string? _selectedQuestRef;

    public void Render(MainViewModel viewModel, ModalManager modalManager, ref bool isOpen)
    {
        if (!isOpen) return;

        ImGui.SetNextWindowSize(new Vector2(800, 600), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowPos(ImGui.GetMainViewport().GetCenter(), ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));

        if (ImGui.Begin("Quest Log", ref isOpen, ImGuiWindowFlags.None))
        {
            if (viewModel.QuestLog == null)
            {
                ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1), "No quest log available");
                ImGui.End();
                return;
            }

            // Header with toggle
            ImGui.Checkbox("Show Completed Quests", ref _showCompleted);
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            ImGui.BeginChild("QuestLogScroll", new Vector2(0, -50), ImGuiChildFlags.None);

            // Active Quests
            ImGui.TextColored(new Vector4(0.5f, 1, 0.5f, 1), $"Active Quests ({viewModel.QuestLog.ActiveQuests?.Count ?? 0})");
            ImGui.Spacing();

            if (viewModel.QuestLog.ActiveQuests?.Count > 0)
            {
                foreach (var quest in viewModel.QuestLog.ActiveQuests)
                {
                    RenderQuestCard(quest, false, modalManager);
                }
            }
            else
            {
                ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1), "No active quests");
                ImGui.TextWrapped("Explore the world to find quest signposts and NPCs offering quests!");
            }

            // Completed Quests
            if (_showCompleted)
            {
                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();

                ImGui.TextColored(new Vector4(0.5f, 1, 0.5f, 1), $"Completed Quests ({viewModel.QuestLog.CompletedQuests?.Count ?? 0})");
                ImGui.Spacing();

                if (viewModel.QuestLog.CompletedQuests?.Count > 0)
                {
                    foreach (var quest in viewModel.QuestLog.CompletedQuests)
                    {
                        RenderQuestCard(quest, true, modalManager);
                    }
                }
                else
                {
                    ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1), "No completed quests yet");
                }
            }

            ImGui.EndChild();

            // Bottom info
            ImGui.Separator();
            ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1), "Click on a quest to view detailed information");

            ImGui.End();
        }
    }

    private void RenderQuestCard(QuestDisplayItem quest, bool isCompleted, ModalManager modalManager)
    {
        var bgColor = isCompleted ?
            new Vector4(0.0f, 0.2f, 0.0f, 0.3f) :  // Green tint for completed
            new Vector4(0.1f, 0.1f, 0.2f, 0.3f);   // Blue tint for active

        ImGui.PushStyleColor(ImGuiCol.ChildBg, bgColor);
        ImGui.PushStyleColor(ImGuiCol.Border, isCompleted ?
            new Vector4(0, 0.5f, 0, 1) :
            new Vector4(0.3f, 0.3f, 1, 1));

        var cardHeight = isCompleted ? 70f : 100f;
        ImGui.BeginChild($"quest_card_{quest.RefName}", new Vector2(0, cardHeight), ImGuiChildFlags.Borders);

        // Quest title
        var titleColor = isCompleted ?
            new Vector4(0.5f, 1, 0.5f, 1) :
            new Vector4(1, 1, 0.7f, 1);

        if (isCompleted)
        {
            ImGui.TextColored(new Vector4(0, 1, 0, 1), "✓");
            ImGui.SameLine();
        }

        ImGui.TextColored(titleColor, quest.DisplayName ?? quest.RefName);

        // Description
        if (!string.IsNullOrEmpty(quest.Description))
        {
            ImGui.TextColored(new Vector4(0.8f, 0.8f, 0.8f, 1), quest.Description);
        }

        // Progress bar (only for active quests)
        if (!isCompleted)
        {
            var progress = (float)quest.ProgressPercentage / 100f;
            ImGui.ProgressBar(progress, new Vector2(-1, 20), quest.ProgressText);
        }

        // Make the whole card clickable
        ImGui.SetCursorPos(new Vector2(0, 0));
        ImGui.InvisibleButton($"quest_click_{quest.RefName}", new Vector2(ImGui.GetContentRegionAvail().X, cardHeight));

        if (ImGui.IsItemHovered())
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        }

        if (ImGui.IsItemClicked())
        {
            // Open quest detail modal
            _selectedQuestRef = quest.RefName;
            modalManager.OpenQuestDetail(quest.RefName);
        }

        ImGui.EndChild();
        ImGui.PopStyleColor(2);

        ImGui.Spacing();
    }
}
