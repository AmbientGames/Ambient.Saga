using Ambient.Domain;
using Ambient.Presentation.WindowsUI.RpgControls.ViewModels;
using Ambient.Saga.Presentation.UI.ViewModels;
using ImGuiNET;
using System.Numerics;

namespace Ambient.Saga.UI.Components.Modals;

/// <summary>
/// Modal showing achievement progress with full criteria type support
/// </summary>
public class AchievementsModal
{
    private bool _showLocked = true;
    private string _filterText = "";

    public void Render(MainViewModel viewModel, ref bool isOpen)
    {
        if (!isOpen) return;

        ImGui.SetNextWindowSize(new Vector2(700, 600), ImGuiCond.FirstUseEver);
        if (ImGui.Begin("Achievements", ref isOpen))
        {
            if (viewModel.Achievements == null)
            {
                ImGui.TextColored(new Vector4(1, 0.5f, 0, 1), "No achievements available");
                ImGui.Text("Load a world to view achievements.");
                ImGui.End();
                return;
            }

            // Header with completion stats
            ImGui.TextColored(new Vector4(1, 0.843f, 0, 1), "ACHIEVEMENTS");
            ImGui.SameLine();
            ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1), $"({viewModel.Achievements.CompletionText})");

            // Progress bar for overall completion
            var totalCount = viewModel.Achievements.TotalAchievements;
            var unlockedCount = viewModel.Achievements.UnlockedCount;
            if (totalCount > 0)
            {
                var overallProgress = (float)unlockedCount / totalCount;
                ImGui.PushStyleColor(ImGuiCol.PlotHistogram, new Vector4(1, 0.843f, 0, 1));
                ImGui.ProgressBar(overallProgress, new Vector2(-1, 25), $"{unlockedCount}/{totalCount} Unlocked");
                ImGui.PopStyleColor();
            }

            ImGui.Separator();

            // Controls
            ImGui.Checkbox("Show Locked", ref _showLocked);
            ImGui.SameLine();
            ImGui.SetNextItemWidth(200);
            ImGui.InputTextWithHint("##Filter", "Filter achievements...", ref _filterText, 100);

            ImGui.Spacing();

            // Scrolling region for achievements
            ImGui.BeginChild("AchievementsList", new Vector2(0, -40), ImGuiChildFlags.Borders);

            // Unlocked Achievements
            if (viewModel.Achievements.UnlockedAchievements?.Count > 0)
            {
                ImGui.TextColored(new Vector4(0.5f, 1, 0.5f, 1), "Unlocked Achievements");
                ImGui.Spacing();

                foreach (var achievement in viewModel.Achievements.UnlockedAchievements)
                {
                    if (!string.IsNullOrEmpty(_filterText) &&
                        !achievement.DisplayName.Contains(_filterText, StringComparison.OrdinalIgnoreCase) &&
                        !achievement.Description.Contains(_filterText, StringComparison.OrdinalIgnoreCase))
                        continue;

                    RenderAchievementCard(achievement, true);
                }
            }

            // Locked Achievements
            if (_showLocked && viewModel.Achievements.LockedAchievements?.Count > 0)
            {
                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();
                ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1), "Locked Achievements");
                ImGui.Spacing();

                foreach (var achievement in viewModel.Achievements.LockedAchievements)
                {
                    if (!string.IsNullOrEmpty(_filterText) &&
                        !achievement.DisplayName.Contains(_filterText, StringComparison.OrdinalIgnoreCase) &&
                        !achievement.Description.Contains(_filterText, StringComparison.OrdinalIgnoreCase))
                        continue;

                    RenderAchievementCard(achievement, false);
                }
            }

            if (viewModel.Achievements.HasNoAchievements)
            {
                ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1), "No achievements defined for this world.");
            }

            ImGui.EndChild();

            // Footer with refresh button
            if (ImGui.Button("Refresh", new Vector2(100, 30)))
            {
                viewModel.Achievements.RefreshAchievements();
            }
            ImGui.SameLine();
            ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1), "Progress syncs automatically with Steam when available");

            ImGui.End();
        }
    }

    private void RenderAchievementCard(AchievementDisplayItem achievement, bool isUnlocked)
    {
        var bgColor = isUnlocked
            ? new Vector4(0.1f, 0.3f, 0.1f, 0.4f)
            : new Vector4(0.15f, 0.15f, 0.15f, 0.4f);

        ImGui.PushStyleColor(ImGuiCol.ChildBg, bgColor);
        ImGui.BeginChild($"achievement_{achievement.RefName}", new Vector2(0, 100), ImGuiChildFlags.Borders);

        // Icon and title
        var icon = isUnlocked ? "★" : "☆";
        var titleColor = isUnlocked ? new Vector4(1, 0.843f, 0, 1) : new Vector4(0.7f, 0.7f, 0.7f, 1);
        ImGui.TextColored(titleColor, $"{icon} {achievement.DisplayName}");

        // Description
        if (!string.IsNullOrEmpty(achievement.Description))
        {
            ImGui.TextWrapped(achievement.Description);
        }

        // Criteria text with full type support
        var criteriaText = GetCriteriaText(achievement.CriteriaType, achievement.Threshold);
        ImGui.TextColored(new Vector4(0.6f, 0.8f, 1f, 1), criteriaText);

        // Progress bar
        if (!isUnlocked)
        {
            var progress = achievement.ProgressPercentage / 100f;
            ImGui.PushStyleColor(ImGuiCol.PlotHistogram, new Vector4(0.3f, 0.6f, 1, 1));
            ImGui.ProgressBar(progress, new Vector2(-1, 18), $"{achievement.CurrentValue:F0} / {achievement.Threshold:F0}");
            ImGui.PopStyleColor();
        }
        else
        {
            ImGui.TextColored(new Vector4(0.5f, 0.8f, 0.5f, 1), $"Unlocked: {achievement.UnlockedDate ?? "Unknown"}");
        }

        ImGui.EndChild();
        ImGui.PopStyleColor();
        ImGui.Spacing();
    }

    /// <summary>
    /// Gets human-readable criteria text for all 28 achievement criteria types
    /// </summary>
    private string GetCriteriaText(AchievementCriteriaType criteriaType, float threshold)
    {
        return criteriaType switch
        {
            // Progression
            AchievementCriteriaType.PlayTimeHours => $"Play for {threshold:F0} hours",
            AchievementCriteriaType.BlocksPlaced => $"Place {threshold:F0} blocks",
            AchievementCriteriaType.BlocksDestroyed => $"Destroy {threshold:F0} blocks",
            AchievementCriteriaType.DistanceTraveled => $"Travel {threshold:F0} meters",

            // Combat
            AchievementCriteriaType.CharactersDefeated => $"Defeat {threshold:F0} characters",
            AchievementCriteriaType.CharactersDefeatedByType => $"Defeat {threshold:F0} characters of specific type",
            AchievementCriteriaType.CharactersDefeatedByTag => $"Defeat {threshold:F0} characters with specific tag",
            AchievementCriteriaType.CharactersDefeatedByRef => $"Defeat specific character {threshold:F0} times",
            AchievementCriteriaType.CriticalHitsDealt => $"Deal {threshold:F0} critical hits",
            AchievementCriteriaType.CombosExecuted => $"Execute {threshold:F0} combos",

            // Exploration
            AchievementCriteriaType.SagaArcsDiscovered => $"Discover {threshold:F0} saga arcs",
            AchievementCriteriaType.SagaArcsCompleted => $"Complete {threshold:F0} saga arcs",
            AchievementCriteriaType.LandmarksDiscovered => $"Discover {threshold:F0} landmarks",
            AchievementCriteriaType.SagaTriggersActivated => $"Activate {threshold:F0} saga triggers",

            // Social
            AchievementCriteriaType.DialogueTreesCompleted => $"Complete {threshold:F0} dialogue trees",
            AchievementCriteriaType.DialogueNodesVisited => $"Visit {threshold:F0} dialogue nodes",
            AchievementCriteriaType.UniqueCharactersMet => $"Meet {threshold:F0} unique characters",

            // Traits
            AchievementCriteriaType.TraitsAssigned => $"Assign {threshold:F0} traits",
            AchievementCriteriaType.TraitsAssignedByType => $"Assign {threshold:F0} traits of specific type",
            AchievementCriteriaType.TraitsAssignedToCharacterType => $"Assign traits to {threshold:F0} character types",

            // Economy
            AchievementCriteriaType.ItemsTraded => $"Trade {threshold:F0} items",
            AchievementCriteriaType.LootAwarded => $"Collect {threshold:F0} loot items",
            AchievementCriteriaType.QuestTokensEarned => $"Earn {threshold:F0} quest tokens",

            // Quests
            AchievementCriteriaType.QuestsCompleted => $"Complete {threshold:F0} quests",
            AchievementCriteriaType.QuestsCompletedByRef => $"Complete specific quest {threshold:F0} times",

            // Reputation
            AchievementCriteriaType.ReputationReached => $"Reach reputation level {threshold:F0}",
            AchievementCriteriaType.FactionsAtReputationLevel => $"Reach reputation with {threshold:F0} factions",

            // Status Effects
            AchievementCriteriaType.StatusEffectsApplied => $"Apply {threshold:F0} status effects",

            _ => $"Reach {threshold:F0}"
        };
    }
}
