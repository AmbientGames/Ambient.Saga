using Ambient.Saga.Presentation.UI.ViewModels;
using Ambient.Presentation.WindowsUI.RpgControls.ViewModels;
using Ambient.Saga.UI.Components.Modals;
using Ambient.Saga.UI.Components.Utilities;
using Ambient.Saga.UI.Configuration;
using ImGuiNET;
using System.Numerics;

namespace Ambient.Saga.UI.Components.Panels;

/// <summary>
/// Classic RPG Journal panel accessible via J key.
/// Consolidates player-facing information (quests, bestiary, world info, achievements)
/// with developer extensions shown when GameConfiguration.ShowDeveloperInfo is true.
///
/// Content:
/// - Quests: Active/completed quest tracking (replaces former QuestLogModal)
/// - Bestiary: Encountered NPCs, merchants, enemies (replaces former CharactersModal)
/// - World: Summary statistics and world information
/// - Achievements: Unlocked/locked achievements with progress
/// </summary>
public class JournalPanel
{
    private bool _showCompletedQuests = false;
    private bool _showLockedAchievements = false;
    private string _bestiaryFilter = "";

    public void Render(MainViewModel viewModel, ModalManager modalManager)
    {
        // Header
        ImGui.TextColored(new Vector4(0.9f, 0.85f, 0.6f, 1f), "JOURNAL");

        // Developer mode indicator
        if (GameConfiguration.ShowDeveloperInfo)
        {
            ImGui.SameLine();
            ImGui.TextColored(GameConfiguration.DevInfoColor, "[DEV MODE]");
        }

        ImGui.Separator();
        ImGui.Spacing();

        // Tab bar
        if (ImGui.BeginTabBar("JournalTabs"))
        {
            if (ImGui.BeginTabItem("Quests"))
            {
                RenderQuestsTab(viewModel, modalManager);
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Bestiary"))
            {
                RenderBestiaryTab(viewModel, modalManager);
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("World"))
            {
                RenderWorldTab(viewModel);
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Achievements"))
            {
                RenderAchievementsTab(viewModel);
                ImGui.EndTabItem();
            }

            ImGui.EndTabBar();
        }
    }

    #region Quests Tab

    private void RenderQuestsTab(MainViewModel viewModel, ModalManager modalManager)
    {
        ImGui.BeginChild("QuestsScroll", new Vector2(0, -ImGui.GetFrameHeightWithSpacing()), ImGuiChildFlags.None);

        if (viewModel.QuestLog == null)
        {
            ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1f), "No quest log available.");
            ImGui.EndChild();
            RenderQuestsFooter();
            return;
        }

        // Active Quests
        var activeCount = viewModel.QuestLog.ActiveQuests?.Count ?? 0;
        ImGui.TextColored(new Vector4(1f, 0.9f, 0.4f, 1f), $"Active Quests ({activeCount})");
        ImGui.Spacing();

        if (activeCount > 0)
        {
            foreach (var quest in viewModel.QuestLog.ActiveQuests!)
            {
                RenderQuestEntry(quest, isCompleted: false, modalManager);
            }
        }
        else
        {
            ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1f), "  No active quests.");
            ImGui.TextWrapped("  Explore the world to discover quests from NPCs and signposts.");
        }

        // Completed Quests (collapsible)
        if (_showCompletedQuests)
        {
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            var completedCount = viewModel.QuestLog.CompletedQuests?.Count ?? 0;
            ImGui.TextColored(new Vector4(0.5f, 0.8f, 0.5f, 1f), $"Completed Quests ({completedCount})");
            ImGui.Spacing();

            if (completedCount > 0)
            {
                foreach (var quest in viewModel.QuestLog.CompletedQuests!)
                {
                    RenderQuestEntry(quest, isCompleted: true, modalManager);
                }
            }
            else
            {
                ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1f), "  No completed quests yet.");
            }
        }

        ImGui.EndChild();
        RenderQuestsFooter();
    }

    private void RenderQuestEntry(QuestDisplayItem quest, bool isCompleted, ModalManager modalManager)
    {
        var bgColor = isCompleted
            ? new Vector4(0.0f, 0.15f, 0.0f, 0.3f)
            : new Vector4(0.1f, 0.1f, 0.15f, 0.3f);

        ImGui.PushStyleColor(ImGuiCol.ChildBg, bgColor);

        var cardHeight = isCompleted
            ? ImGui.GetFrameHeightWithSpacing() * 2.5f
            : ImGui.GetFrameHeightWithSpacing() * 3.5f;

        ImGui.BeginChild($"quest_{quest.RefName}", new Vector2(ImGuiSizes.Fill, cardHeight), ImGuiChildFlags.Borders);

        // Quest title
        var titleColor = isCompleted
            ? new Vector4(0.6f, 0.9f, 0.6f, 1f)
            : new Vector4(1f, 0.95f, 0.7f, 1f);

        if (isCompleted)
        {
            ImGui.TextColored(new Vector4(0.4f, 0.8f, 0.4f, 1f), "[DONE]");
            ImGui.SameLine();
        }

        ImGui.TextColored(titleColor, quest.DisplayName ?? quest.RefName);

        // Developer: RefName
        if (GameConfiguration.ShowDeveloperInfo)
        {
            ImGui.SameLine();
            ImGui.TextColored(GameConfiguration.DevInfoColor, $"[{quest.RefName}]");
        }

        // Description
        if (!string.IsNullOrEmpty(quest.Description))
        {
            ImGui.TextColored(new Vector4(0.8f, 0.8f, 0.8f, 1f), quest.Description);
        }

        // Progress (active quests only)
        if (!isCompleted)
        {
            var progress = (float)quest.ProgressPercentage / 100f;
            ImGui.ProgressBar(progress, new Vector2(ImGuiSizes.Fill, ImGui.GetFrameHeight()), quest.ProgressText);
        }

        // Developer: Additional metadata (progress details)
        if (GameConfiguration.ShowDeveloperInfo)
        {
            ImGui.TextColored(GameConfiguration.DevInfoColor, $"Progress: {quest.CurrentValue}/{quest.TargetValue}");
        }

        // Make clickable to open details
        ImGui.SetCursorPos(new Vector2(0, 0));
        if (ImGui.InvisibleButton($"quest_click_{quest.RefName}", new Vector2(ImGui.GetContentRegionAvail().X, cardHeight - ImGui.GetStyle().WindowPadding.Y * 2)))
        {
            modalManager.OpenQuestDetail(quest.RefName);
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            ImGui.SetTooltip("Click to view quest details");
        }

        ImGui.EndChild();
        ImGui.PopStyleColor();
        ImGui.Spacing();
    }

    private void RenderQuestsFooter()
    {
        ImGui.Separator();
        ImGui.Checkbox("Show Completed Quests", ref _showCompletedQuests);
    }

    #endregion

    #region Bestiary Tab

    private void RenderBestiaryTab(MainViewModel viewModel, ModalManager modalManager)
    {
        // Filter input
        ImGui.SetNextItemWidth(200);
        ImGui.InputTextWithHint("##BestiaryFilter", "Filter by name...", ref _bestiaryFilter, 100);
        ImGui.SameLine();
        if (ImGui.Button("Clear"))
        {
            _bestiaryFilter = "";
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.BeginChild("BestiaryScroll", new Vector2(0, 0), ImGuiChildFlags.None);

        if (viewModel.Characters == null || viewModel.Characters.Count == 0)
        {
            ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1f), "No characters encountered yet.");
            ImGui.EndChild();
            return;
        }

        // Filter characters
        IEnumerable<CharacterViewModel> filteredCharacters = string.IsNullOrWhiteSpace(_bestiaryFilter)
            ? viewModel.Characters
            : viewModel.Characters.Where(c =>
                (c.DisplayName?.Contains(_bestiaryFilter, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (c.CharacterType?.Contains(_bestiaryFilter, StringComparison.OrdinalIgnoreCase) ?? false));

        if (!filteredCharacters.Any())
        {
            ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1f), "No characters match the filter.");
            ImGui.EndChild();
            return;
        }

        // Group by type
        var grouped = filteredCharacters
            .GroupBy(c => c.CharacterType ?? "Unknown")
            .OrderBy(g => g.Key);

        foreach (var group in grouped)
        {
            if (ImGui.CollapsingHeader($"{group.Key} ({group.Count()})", ImGuiTreeNodeFlags.DefaultOpen))
            {
                foreach (var character in group)
                {
                    RenderBestiaryEntry(character, viewModel, modalManager);
                }
            }
        }

        ImGui.EndChild();
    }

    private void RenderBestiaryEntry(CharacterViewModel character, MainViewModel viewModel, ModalManager modalManager)
    {
        var bgColor = character.IsAlive
            ? new Vector4(0.1f, 0.1f, 0.15f, 0.3f)
            : new Vector4(0.15f, 0.05f, 0.05f, 0.3f);

        ImGui.PushStyleColor(ImGuiCol.ChildBg, bgColor);

        var cardHeight = GameConfiguration.ShowDeveloperInfo
            ? ImGui.GetFrameHeightWithSpacing() * 3f
            : ImGui.GetFrameHeightWithSpacing() * 2.5f;

        ImGui.BeginChild($"bestiary_{character.DisplayName}_{character.PixelX}", new Vector2(ImGuiSizes.Fill, cardHeight), ImGuiChildFlags.Borders);

        // Character name and status
        var typeColor = GetCharacterTypeColor(character.CharacterType);
        ImGui.TextColored(typeColor, character.DisplayName ?? "Unknown");

        ImGui.SameLine();
        if (character.IsAlive)
        {
            ImGui.TextColored(new Vector4(0.4f, 0.8f, 0.4f, 1f), "[Alive]");
        }
        else
        {
            ImGui.TextColored(new Vector4(0.8f, 0.4f, 0.4f, 1f), "[Defeated]");
        }

        // Developer: CharacterRef
        if (GameConfiguration.ShowDeveloperInfo && !string.IsNullOrEmpty(character.CharacterRef))
        {
            ImGui.SameLine();
            ImGui.TextColored(GameConfiguration.DevInfoColor, $"[{character.CharacterRef}]");
        }

        // Character type and interaction hints
        var interactions = new List<string>();
        if (character.CanDialogue) interactions.Add("Talk");
        if (character.CanTrade) interactions.Add("Trade");
        if (character.CanAttack) interactions.Add("Combat");
        if (character.CanLoot) interactions.Add("Loot");

        if (interactions.Count > 0)
        {
            ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1f), $"  {string.Join(" | ", interactions)}");
        }

        // Developer: Coordinates
        if (GameConfiguration.ShowDeveloperInfo)
        {
            ImGui.TextColored(GameConfiguration.DevInfoColor, $"  Pixel: ({character.PixelX:F0}, {character.PixelY:F0})");
        }

        // Make clickable to interact
        ImGui.SetCursorPos(new Vector2(0, 0));
        if (ImGui.InvisibleButton($"bestiary_click_{character.DisplayName}_{character.PixelX}", new Vector2(ImGui.GetContentRegionAvail().X, cardHeight - ImGui.GetStyle().WindowPadding.Y * 2)))
        {
            modalManager.OpenCharacterInteraction(character, viewModel);
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            ImGui.SetTooltip("Click to interact");
        }

        ImGui.EndChild();
        ImGui.PopStyleColor();
        ImGui.Spacing();
    }

    private static Vector4 GetCharacterTypeColor(string? characterType)
    {
        return characterType switch
        {
            "Boss" => new Vector4(1f, 0.3f, 0.3f, 1f),
            "Merchant" => new Vector4(1f, 0.84f, 0f, 1f),
            "Quest" => new Vector4(0.4f, 0.6f, 1f, 1f),
            "Encounter" => new Vector4(0.4f, 0.9f, 0.4f, 1f),
            _ => new Vector4(0.9f, 0.9f, 0.9f, 1f)
        };
    }

    #endregion

    #region World Tab

    private void RenderWorldTab(MainViewModel viewModel)
    {
        ImGui.BeginChild("WorldScroll", new Vector2(0, 0), ImGuiChildFlags.None);

        var world = viewModel.CurrentWorld;
        var avatar = viewModel.PlayerAvatar;

        if (world == null)
        {
            ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1f), "No world loaded.");
            ImGui.EndChild();
            return;
        }

        // World Name
        ImGui.TextColored(new Vector4(0.9f, 0.85f, 0.6f, 1f), world.WorldConfiguration?.DisplayName ?? "Unknown World");

        // Developer: RefName
        if (GameConfiguration.ShowDeveloperInfo)
        {
            ImGui.TextColored(GameConfiguration.DevInfoColor, $"[{world.WorldConfiguration?.RefName}]");
        }

        // World description
        if (!string.IsNullOrEmpty(world.WorldConfiguration?.Description))
        {
            ImGui.Spacing();
            ImGui.TextWrapped(world.WorldConfiguration.Description);
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // Play Statistics
        ImGui.TextColored(new Vector4(0.5f, 0.8f, 1f, 1f), "Statistics");
        ImGui.Spacing();

        if (avatar != null)
        {
            ImGui.Text($"Play Time: {avatar.PlayTimeHours:F1} hours");
            ImGui.Text($"Distance Traveled: {avatar.DistanceTraveled:N0} meters");
            ImGui.Text($"Blocks Placed: {avatar.BlocksPlaced:N0}");
            ImGui.Text($"Blocks Destroyed: {avatar.BlocksDestroyed:N0}");

            var completedQuests = avatar.Quests?.Count(q => q.IsCompleted) ?? 0;
            var totalQuests = avatar.Quests?.Length ?? 0;
            ImGui.Text($"Quests Completed: {completedQuests} / {totalQuests}");

            var achievements = avatar.Achievements?.Length ?? 0;
            ImGui.Text($"Achievements: {achievements}");
        }
        else
        {
            ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1f), "No avatar statistics available.");
        }

        // Developer: World Configuration Details
        if (GameConfiguration.ShowDeveloperInfo)
        {
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            ImGui.TextColored(GameConfiguration.DevInfoLabelColor, "Developer Info");
            ImGui.Spacing();

            var config = world.WorldConfiguration;
            if (config != null)
            {
                ImGui.TextColored(GameConfiguration.DevInfoColor, $"Theme: {config.ContentPackTheme ?? "default"}");
                ImGui.TextColored(GameConfiguration.DevInfoColor, $"Currency: {config.CurrencyName ?? "Credits"}");

                if (config.SpawnLatitude != 0 || config.SpawnLongitude != 0)
                {
                    ImGui.TextColored(GameConfiguration.DevInfoColor, $"Spawn GPS: ({config.SpawnLatitude:F4}, {config.SpawnLongitude:F4})");
                }

                // Content counts
                var sagaCount = world.SagaArcLookup?.Count ?? 0;
                var questCount = world.QuestsLookup?.Count ?? 0;
                var characterCount = world.CharactersLookup?.Count ?? 0;
                var equipmentCount = world.EquipmentLookup?.Count ?? 0;

                ImGui.Spacing();
                ImGui.TextColored(GameConfiguration.DevInfoColor, $"Content: {sagaCount} Sagas, {questCount} Quests, {characterCount} Characters, {equipmentCount} Equipment");
            }
        }

        ImGui.EndChild();
    }

    #endregion

    #region Achievements Tab

    private void RenderAchievementsTab(MainViewModel viewModel)
    {
        ImGui.BeginChild("AchievementsScroll", new Vector2(0, -ImGui.GetFrameHeightWithSpacing()), ImGuiChildFlags.None);

        if (viewModel.Achievements == null)
        {
            ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1f), "No achievements available.");
            ImGui.EndChild();
            RenderAchievementsFooter();
            return;
        }

        // Completion stats header
        ImGui.TextColored(new Vector4(1f, 0.9f, 0.4f, 1f), $"Progress: {viewModel.Achievements.CompletionText}");
        ImGui.Spacing();

        // Unlocked Achievements
        var unlockedCount = viewModel.Achievements.UnlockedAchievements?.Count ?? 0;
        ImGui.TextColored(new Vector4(0.5f, 0.8f, 0.5f, 1f), $"Unlocked ({unlockedCount})");
        ImGui.Spacing();

        if (unlockedCount > 0)
        {
            foreach (var achievement in viewModel.Achievements.UnlockedAchievements!)
            {
                RenderAchievementEntry(achievement, isUnlocked: true);
            }
        }
        else
        {
            ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1f), "  No achievements unlocked yet.");
            ImGui.TextWrapped("  Complete objectives to earn achievements.");
        }

        // Locked Achievements (collapsible)
        if (_showLockedAchievements)
        {
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            var lockedCount = viewModel.Achievements.LockedAchievements?.Count ?? 0;
            ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1f), $"Locked ({lockedCount})");
            ImGui.Spacing();

            if (lockedCount > 0)
            {
                foreach (var achievement in viewModel.Achievements.LockedAchievements!)
                {
                    RenderAchievementEntry(achievement, isUnlocked: false);
                }
            }
            else
            {
                ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1f), "  All achievements unlocked!");
            }
        }

        ImGui.EndChild();
        RenderAchievementsFooter();
    }

    private void RenderAchievementEntry(Ambient.Presentation.WindowsUI.RpgControls.ViewModels.AchievementDisplayItem achievement, bool isUnlocked)
    {
        var bgColor = isUnlocked
            ? new Vector4(0.0f, 0.15f, 0.0f, 0.3f)
            : new Vector4(0.1f, 0.1f, 0.1f, 0.2f);

        ImGui.PushStyleColor(ImGuiCol.ChildBg, bgColor);

        // Card height varies: unlocked shows status, locked shows criteria + progress
        var cardHeight = isUnlocked
            ? ImGui.GetFrameHeightWithSpacing() * 3f
            : ImGui.GetFrameHeightWithSpacing() * 4f;

        if (GameConfiguration.ShowDeveloperInfo)
        {
            cardHeight += ImGui.GetFrameHeightWithSpacing() * 0.5f;
        }

        ImGui.BeginChild($"achievement_{achievement.RefName}", new Vector2(ImGuiSizes.Fill, cardHeight), ImGuiChildFlags.Borders);

        // Achievement title with trophy icon for unlocked
        if (isUnlocked)
        {
            ImGui.TextColored(new Vector4(1f, 0.84f, 0f, 1f), "* ");
            ImGui.SameLine();
            ImGui.TextColored(new Vector4(0.6f, 0.9f, 0.6f, 1f), achievement.DisplayName ?? achievement.RefName);
        }
        else
        {
            ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1f), "[Locked]");
            ImGui.SameLine();
            ImGui.TextColored(new Vector4(0.8f, 0.8f, 0.8f, 1f), achievement.DisplayName ?? achievement.RefName);
        }

        // Developer: RefName
        if (GameConfiguration.ShowDeveloperInfo)
        {
            ImGui.SameLine();
            ImGui.TextColored(GameConfiguration.DevInfoColor, $"[{achievement.RefName}]");
        }

        // Description
        if (!string.IsNullOrEmpty(achievement.Description))
        {
            ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1f), achievement.Description);
        }

        if (isUnlocked)
        {
            // Status text (unlocked date)
            ImGui.TextColored(new Vector4(0.5f, 0.8f, 0.5f, 1f), achievement.StatusText);
        }
        else
        {
            // Criteria text
            if (!string.IsNullOrEmpty(achievement.CriteriaText))
            {
                ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1f), achievement.CriteriaText);
            }

            // Progress bar
            var progress = achievement.ProgressPercentage / 100f;
            ImGui.ProgressBar(progress, new Vector2(ImGuiSizes.Fill, ImGui.GetFrameHeight() * 0.7f), achievement.ProgressText);
        }

        // Developer: Additional metadata
        if (GameConfiguration.ShowDeveloperInfo)
        {
            ImGui.TextColored(GameConfiguration.DevInfoColor, $"Progress: {achievement.CurrentValue:F0}/{achievement.Threshold:F0}");
        }

        ImGui.EndChild();
        ImGui.PopStyleColor();
        ImGui.Spacing();
    }

    private void RenderAchievementsFooter()
    {
        ImGui.Separator();
        ImGui.Checkbox("Show Locked Achievements", ref _showLockedAchievements);
    }

    #endregion
}
