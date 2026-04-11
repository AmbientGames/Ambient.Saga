using Ambient.Domain;
using Ambient.Saga.Engine.Domain.Rpg.Sagas.TransactionLog;
using Ambient.Saga.Presentation.UI.ViewModels;
using Ambient.Presentation.WindowsUI.RpgControls.ViewModels;
using Ambient.Saga.UI.Components.Modals;
using Ambient.Saga.UI.Components.Utilities;
using Ambient.Saga.UI.Configuration;
using ImGuiNET;
using System.Numerics;
using Ambient.Saga.Engine.Domain;

namespace Ambient.Saga.UI.Components.Panels;

/// <summary>
/// Classic RPG Journal panel accessible via J key.
/// Consolidates player-facing information (quests, bestiary, atlas, history, achievements)
/// with developer extensions shown when GameConfiguration.ShowDeveloperInfo is true.
///
/// Content:
/// - Quests: Active/completed quest tracking
/// - Bestiary: Encountered NPCs, merchants, enemies
/// - Atlas: Discovered locations with GPS coordinates
/// - History: Transaction log of avatar actions
/// - Achievements: Unlocked/locked achievements with progress
/// </summary>
public class JournalPanel
{
    private bool _showCompletedQuests = false;
    private bool _showLockedAchievements = true;
    private string _bestiaryFilter = "";
    private string _historyFilter = "";
    private string _achievementFilter = "";

    public void Render(SagaMainViewModel viewModel, ModalManager modalManager)
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

        // Three-column layout
        var availableWidth = ImGui.GetContentRegionAvail().X;
        var availableHeight = ImGui.GetContentRegionAvail().Y;
        var columnSpacing = 10f * UIConstants.DpiScale;
        var columnWidth = (availableWidth - (columnSpacing * 2)) / 3f;

        // Column 1: Quests
        ImGui.BeginChild("JournalCol1", new Vector2(columnWidth, availableHeight), ImGuiChildFlags.None);
        RenderQuestsColumn(viewModel, modalManager);
        ImGui.EndChild();

        ImGui.SameLine(0, columnSpacing);

        // Column 2: Bestiary + Atlas
        ImGui.BeginChild("JournalCol2", new Vector2(columnWidth, availableHeight), ImGuiChildFlags.None);
        RenderDiscoveryColumn(viewModel, modalManager);
        ImGui.EndChild();

        ImGui.SameLine(0, columnSpacing);

        // Column 3: History + Achievements
        ImGui.BeginChild("JournalCol3", new Vector2(columnWidth, availableHeight), ImGuiChildFlags.None);
        RenderProgressColumn(viewModel);
        ImGui.EndChild();
    }

    private void RenderQuestsColumn(SagaMainViewModel viewModel, ModalManager modalManager)
    {
        ImGui.TextColored(new Vector4(1f, 0.9f, 0.4f, 1f), "QUESTS");
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.BeginChild("QuestsScroll", new Vector2(ImGuiSizes.Fill, ImGuiSizes.Fill - ImGui.GetFrameHeightWithSpacing()), ImGuiChildFlags.None);
        RenderQuestsContent(viewModel, modalManager);
        ImGui.EndChild();

        RenderQuestsFooter();
    }

    private void RenderDiscoveryColumn(SagaMainViewModel viewModel, ModalManager modalManager)
    {
        var halfHeight = (ImGui.GetContentRegionAvail().Y - 10f * UIConstants.DpiScale) / 2f;

        // Bestiary section
        ImGui.BeginChild("BestiarySection", new Vector2(ImGuiSizes.Fill, halfHeight), ImGuiChildFlags.None);
        ImGui.TextColored(new Vector4(0.8f, 0.6f, 0.9f, 1f), "BESTIARY");
        ImGui.Separator();
        ImGui.Spacing();
        RenderBestiaryContent(viewModel, modalManager);
        ImGui.EndChild();

        ImGui.Spacing();

        // Atlas section
        ImGui.BeginChild("AtlasSection", new Vector2(ImGuiSizes.Fill, halfHeight), ImGuiChildFlags.None);
        ImGui.TextColored(new Vector4(0.5f, 0.8f, 1f, 1f), "ATLAS");
        ImGui.Separator();
        ImGui.Spacing();
        RenderAtlasContent(viewModel);
        ImGui.EndChild();
    }

    private void RenderProgressColumn(SagaMainViewModel viewModel)
    {
        var halfHeight = (ImGui.GetContentRegionAvail().Y - 10f * UIConstants.DpiScale) / 2f;

        // History section
        ImGui.BeginChild("HistorySection", new Vector2(ImGuiSizes.Fill, halfHeight), ImGuiChildFlags.None);
        ImGui.TextColored(new Vector4(0.6f, 0.8f, 0.9f, 1f), "HISTORY");
        ImGui.Separator();
        ImGui.Spacing();
        RenderHistoryContent(viewModel);
        ImGui.EndChild();

        ImGui.Spacing();

        // Achievements section
        ImGui.BeginChild("AchievementsSection", new Vector2(ImGuiSizes.Fill, halfHeight), ImGuiChildFlags.None);
        ImGui.TextColored(new Vector4(1f, 0.84f, 0f, 1f), "ACHIEVEMENTS");
        ImGui.Separator();
        ImGui.Spacing();
        RenderAchievementsContent(viewModel);
        ImGui.EndChild();
    }

    #region Quests

    private void RenderQuestsContent(SagaMainViewModel viewModel, ModalManager modalManager)
    {
        if (viewModel.QuestLog == null)
        {
            ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1f), "No quest log available.");
            return;
        }

        // Active Quests
        var activeCount = viewModel.QuestLog.ActiveQuests?.Count ?? 0;
        ImGui.TextColored(new Vector4(1f, 0.9f, 0.4f, 1f), $"Active ({activeCount})");
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
            ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1f), "No active quests.");
            ImGui.TextWrapped("Explore the world to discover quests.");
        }

        // Completed Quests (collapsible)
        if (_showCompletedQuests)
        {
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            var completedCount = viewModel.QuestLog.CompletedQuests?.Count ?? 0;
            ImGui.TextColored(new Vector4(0.5f, 0.8f, 0.5f, 1f), $"Completed ({completedCount})");
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
                ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1f), "No completed quests yet.");
            }
        }
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

    #region Bestiary

    private void RenderBestiaryContent(SagaMainViewModel viewModel, ModalManager modalManager)
    {
        // Filter input
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - 50);
        ImGui.InputTextWithHint("##BestiaryFilter", "Filter...", ref _bestiaryFilter, 100);
        ImGui.SameLine();
        if (ImGui.SmallButton("X##ClearBestiary"))
        {
            _bestiaryFilter = "";
        }

        ImGui.Spacing();

        ImGui.BeginChild("BestiaryScroll", new Vector2(ImGuiSizes.Fill, ImGuiSizes.Fill), ImGuiChildFlags.None);

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
            ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1f), "No matches.");
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

    private void RenderBestiaryEntry(CharacterViewModel character, SagaMainViewModel viewModel, ModalManager modalManager)
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

    #region Atlas

    private void RenderAtlasContent(SagaMainViewModel viewModel)
    {
        ImGui.BeginChild("AtlasScroll", new Vector2(ImGuiSizes.Fill, ImGuiSizes.Fill), ImGuiChildFlags.None);

        var world = viewModel.CurrentWorld;

        if (world == null)
        {
            ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1f), "No world loaded.");
            ImGui.EndChild();
            return;
        }

        var locations = world.SagaArcLookup?.Values?.ToList();
        if (locations == null || locations.Count == 0)
        {
            ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1f), "No locations discovered yet.");
            ImGui.TextWrapped("Explore to discover points of interest.");
            ImGui.EndChild();
            return;
        }

        // Header
        ImGui.TextColored(new Vector4(0.5f, 0.8f, 1f, 1f), $"Locations ({locations.Count})");
        ImGui.Spacing();

        // Group by category
        var grouped = locations
            .GroupBy(l => l.Category)
            .OrderBy(g => g.Key.ToString());

        foreach (var group in grouped)
        {
            var categoryName = GetCategoryDisplayName(group.Key);
            if (ImGui.CollapsingHeader($"{categoryName} ({group.Count()})", ImGuiTreeNodeFlags.DefaultOpen))
            {
                foreach (var location in group.OrderBy(l => l.DisplayName ?? l.RefName))
                {
                    RenderAtlasEntry(location);
                }
            }
        }

        ImGui.EndChild();
    }

    private void RenderAtlasEntry(SagaArc location)
    {
        var bgColor = new Vector4(0.1f, 0.1f, 0.15f, 0.3f);
        ImGui.PushStyleColor(ImGuiCol.ChildBg, bgColor);

        var cardHeight = GameConfiguration.ShowDeveloperInfo
            ? ImGui.GetFrameHeightWithSpacing() * 4f
            : ImGui.GetFrameHeightWithSpacing() * 3f;

        ImGui.BeginChild($"atlas_{location.RefName}", new Vector2(ImGuiSizes.Fill, cardHeight), ImGuiChildFlags.Borders);

        // Category icon and name
        var categoryColor = GetCategoryColor(location.Category);
        ImGui.TextColored(categoryColor, $"[{GetCategoryDisplayName(location.Category)}]");
        ImGui.SameLine();
        ImGui.TextColored(new Vector4(1f, 0.95f, 0.7f, 1f), location.DisplayName ?? location.RefName);

        // Developer: RefName
        if (GameConfiguration.ShowDeveloperInfo)
        {
            ImGui.SameLine();
            ImGui.TextColored(GameConfiguration.DevInfoColor, $"[{location.RefName}]");
        }

        // Description
        if (!string.IsNullOrEmpty(location.Description))
        {
            ImGui.TextColored(new Vector4(0.8f, 0.8f, 0.8f, 1f), location.Description);
        }

        // GPS coordinates
        ImGui.TextColored(new Vector4(0.6f, 0.7f, 0.8f, 1f), $"GPS: ({location.Latitude:F4}, {location.Longitude:F4})");

        // Developer: Additional metadata
        if (GameConfiguration.ShowDeveloperInfo)
        {
            ImGui.TextColored(GameConfiguration.DevInfoColor, $"DiscoverRadius: {location.DiscoverRadius:F0}m | Kind: {location.Kind} | InitialState: {location.InitialState}");
            var triggerCount = location.SagaTrigger?.Length ?? 0;
            if (triggerCount > 0)
            {
                ImGui.TextColored(GameConfiguration.DevInfoColor, $"Triggers: {triggerCount}");
            }
        }

        ImGui.EndChild();
        ImGui.PopStyleColor();
        ImGui.Spacing();
    }

    private static string GetCategoryDisplayName(SagaArcCategory category)
    {
        return category switch
        {
            SagaArcCategory.Facility => "Facility",
            SagaArcCategory.Stronghold => "Stronghold",
            SagaArcCategory.Religious => "Religious Site",
            SagaArcCategory.Ruin => "Ruin",
            SagaArcCategory.Landmark => "Landmark",
            SagaArcCategory.Infrastructure => "Infrastructure",
            SagaArcCategory.Passage => "Passage",
            SagaArcCategory.Waypoint => "Waypoint",
            SagaArcCategory.Camp => "Camp",
            SagaArcCategory.QuestHub => "Quest Hub",
            SagaArcCategory.Service => "Service",
            SagaArcCategory.Default => "Location",
            _ => category.ToString()
        };
    }

    private static Vector4 GetCategoryColor(SagaArcCategory category)
    {
        return category switch
        {
            SagaArcCategory.Stronghold => new Vector4(1f, 0.3f, 0.3f, 1f),      // Red - dangerous
            SagaArcCategory.Religious => new Vector4(0.9f, 0.8f, 0.3f, 1f),     // Gold - sacred
            SagaArcCategory.Ruin => new Vector4(0.6f, 0.5f, 0.4f, 1f),          // Brown - ancient
            SagaArcCategory.Landmark => new Vector4(0.4f, 0.8f, 0.4f, 1f),      // Green - natural
            SagaArcCategory.QuestHub => new Vector4(0.4f, 0.6f, 1f, 1f),        // Blue - important
            SagaArcCategory.Service => new Vector4(1f, 0.84f, 0f, 1f),          // Yellow - merchant
            SagaArcCategory.Camp => new Vector4(0.8f, 0.6f, 0.4f, 1f),          // Orange - rest
            SagaArcCategory.Facility => new Vector4(0.7f, 0.7f, 0.8f, 1f),      // Gray-blue - industrial
            SagaArcCategory.Infrastructure => new Vector4(0.5f, 0.5f, 0.6f, 1f), // Gray - utility
            SagaArcCategory.Passage => new Vector4(0.6f, 0.7f, 0.6f, 1f),       // Muted green - path
            SagaArcCategory.Waypoint => new Vector4(0.5f, 0.7f, 0.9f, 1f),      // Light blue - navigation
            _ => new Vector4(0.8f, 0.8f, 0.8f, 1f)                              // Default gray
        };
    }

    #endregion

    #region History

    private void RenderHistoryContent(SagaMainViewModel viewModel)
    {
        // Filter input
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - 50);
        ImGui.InputTextWithHint("##HistoryFilter", "Filter...", ref _historyFilter, 100);
        ImGui.SameLine();
        if (ImGui.SmallButton("X##ClearHistory"))
        {
            _historyFilter = "";
        }

        ImGui.Spacing();

        ImGui.BeginChild("HistoryScroll", new Vector2(ImGuiSizes.Fill, ImGuiSizes.Fill), ImGuiChildFlags.None);

        var transactions = viewModel.RecentTransactions;
        if (transactions == null || transactions.Count == 0)
        {
            ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1f), "No events recorded yet.");
            ImGui.TextWrapped("Actions will be logged here.");
            ImGui.EndChild();
            return;
        }

        // Filter transactions
        IEnumerable<SagaTransaction> filteredTransactions = string.IsNullOrWhiteSpace(_historyFilter)
            ? transactions
            : transactions.Where(t =>
                GetTransactionDisplayText(t).Contains(_historyFilter, StringComparison.OrdinalIgnoreCase) ||
                t.Type.ToString().Contains(_historyFilter, StringComparison.OrdinalIgnoreCase));

        var transactionList = filteredTransactions.ToList();

        // Display transactions in reverse chronological order (most recent first)
        foreach (var transaction in transactionList.OrderByDescending(t => t.GetCanonicalTimestamp()).Take(50))
        {
            RenderHistoryEntry(transaction);
        }

        ImGui.EndChild();
    }

    private void RenderHistoryEntry(SagaTransaction transaction)
    {
        var bgColor = GetTransactionBackgroundColor(transaction.Type);
        ImGui.PushStyleColor(ImGuiCol.ChildBg, bgColor);

        var cardHeight = GameConfiguration.ShowDeveloperInfo
            ? ImGui.GetFrameHeightWithSpacing() * 2.5f
            : ImGui.GetFrameHeightWithSpacing() * 1.5f;

        ImGui.BeginChild($"history_{transaction.TransactionId}", new Vector2(ImGuiSizes.Fill, cardHeight), ImGuiChildFlags.Borders);

        // Timestamp
        var timestamp = transaction.GetCanonicalTimestamp();
        var relativeTime = GetRelativeTimeString(timestamp);
        ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1f), $"[{relativeTime}]");
        ImGui.SameLine();

        // Event text
        var displayText = GetTransactionDisplayText(transaction);
        var typeColor = GetTransactionTypeColor(transaction.Type);
        ImGui.TextColored(typeColor, displayText);

        // Developer: Additional metadata
        if (GameConfiguration.ShowDeveloperInfo)
        {
            ImGui.TextColored(GameConfiguration.DevInfoColor, $"Type: {transaction.Type} | Seq: {transaction.SequenceNumber} | ID: {transaction.TransactionId:N}");
            if (transaction.Data.Count > 0)
            {
                var dataPreview = string.Join(", ", transaction.Data.Take(3).Select(kv => $"{kv.Key}={kv.Value}"));
                if (transaction.Data.Count > 3)
                    dataPreview += "...";
                ImGui.TextColored(GameConfiguration.DevInfoColor, $"Data: {dataPreview}");
            }
        }

        ImGui.EndChild();
        ImGui.PopStyleColor();
        ImGui.Spacing();
    }

    private static string GetTransactionDisplayText(SagaTransaction transaction)
    {
        // Extract common data fields
        transaction.Data.TryGetValue(TransactionDataKeys.CharacterRef, out var characterRef);
        transaction.Data.TryGetValue(TransactionDataKeys.QuestRef, out var questRef);
        transaction.Data.TryGetValue(TransactionDataKeys.EquipmentRef, out var equipmentRef);
        transaction.Data.TryGetValue(TransactionDataKeys.ConsumableRef, out var consumableRef);
        transaction.Data.TryGetValue(TransactionDataKeys.FeatureRef, out var featureRef);
        transaction.Data.TryGetValue(TransactionDataKeys.TriggerRef, out var triggerRef);
        transaction.Data.TryGetValue(TransactionDataKeys.ItemRef, out var itemRef);

        return transaction.Type switch
        {
            SagaTransactionType.CharacterDefeated => $"Defeated {characterRef ?? "enemy"}",
            SagaTransactionType.CharacterDamaged => $"Attacked {characterRef ?? "enemy"}",
            SagaTransactionType.CharacterSpawned => $"Encountered {characterRef ?? "character"}",
            SagaTransactionType.QuestAccepted => $"Accepted quest: {questRef ?? "unknown"}",
            SagaTransactionType.QuestCompleted => $"Completed quest: {questRef ?? "unknown"}",
            SagaTransactionType.QuestFailed => $"Failed quest: {questRef ?? "unknown"}",
            SagaTransactionType.QuestAbandoned => $"Abandoned quest: {questRef ?? "unknown"}",
            SagaTransactionType.QuestObjectiveCompleted => $"Objective completed: {questRef ?? "unknown"}",
            SagaTransactionType.QuestStageAdvanced => $"Quest advanced: {questRef ?? "unknown"}",
            SagaTransactionType.DialogueStarted => $"Spoke with {characterRef ?? "character"}",
            SagaTransactionType.DialogueCompleted => $"Finished talking with {characterRef ?? "character"}",
            SagaTransactionType.ItemTraded => $"Traded with {characterRef ?? "merchant"}",
            SagaTransactionType.LootAwarded => "Collected loot",
            SagaTransactionType.EquipmentChanged => $"Equipped {equipmentRef ?? "item"}",
            SagaTransactionType.ConsumableUsed => $"Used {consumableRef ?? "item"}",
            SagaTransactionType.LandmarkDiscovered => $"Discovered {featureRef ?? triggerRef ?? "location"}",
            SagaTransactionType.SagaDiscovered => $"Found new area: {triggerRef ?? "unknown"}",
            SagaTransactionType.SagaCompleted => $"Area completed: {triggerRef ?? "unknown"}",
            SagaTransactionType.BattleStarted => $"Battle started with {characterRef ?? "enemy"}",
            SagaTransactionType.BattleEnded => "Battle ended",
            SagaTransactionType.BattleTurnExecuted => "Attack executed",
            SagaTransactionType.TriggerActivated => $"Entered: {triggerRef ?? "area"}",
            SagaTransactionType.TriggerCompleted => $"Left: {triggerRef ?? "area"}",
            SagaTransactionType.PlayerEntered => "Arrived at location",
            SagaTransactionType.PlayerExited => "Departed location",
            SagaTransactionType.AffinityGranted => $"Learned about {characterRef ?? "character"}",
            SagaTransactionType.CurrencyChanged => "Currency changed",
            SagaTransactionType.ItemCrafted => $"Crafted {itemRef ?? "item"}",
            SagaTransactionType.PartyMemberJoined => $"{characterRef ?? "Companion"} joined party",
            SagaTransactionType.PartyMemberLeft => $"{characterRef ?? "Companion"} left party",
            SagaTransactionType.StatusEffectApplied => "Status effect applied",
            SagaTransactionType.StatusEffectRemoved => "Status effect removed",
            SagaTransactionType.ReputationChanged => "Reputation changed",
            _ => transaction.Type.ToString()
        };
    }

    private static string GetRelativeTimeString(DateTime timestamp)
    {
        var now = DateTime.UtcNow;
        var diff = now - timestamp;

        if (diff.TotalSeconds < 60)
            return "Just now";
        if (diff.TotalMinutes < 60)
            return $"{(int)diff.TotalMinutes} min ago";
        if (diff.TotalHours < 24)
            return $"{(int)diff.TotalHours} hr ago";
        if (diff.TotalDays < 7)
            return $"{(int)diff.TotalDays} days ago";

        return timestamp.ToString("MMM dd");
    }

    private static Vector4 GetTransactionTypeColor(SagaTransactionType type)
    {
        return type switch
        {
            // Combat - red tones
            SagaTransactionType.CharacterDefeated => new Vector4(1f, 0.4f, 0.4f, 1f),
            SagaTransactionType.CharacterDamaged => new Vector4(1f, 0.5f, 0.3f, 1f),
            SagaTransactionType.BattleStarted => new Vector4(1f, 0.3f, 0.3f, 1f),
            SagaTransactionType.BattleEnded => new Vector4(0.8f, 0.4f, 0.4f, 1f),
            SagaTransactionType.BattleTurnExecuted => new Vector4(1f, 0.6f, 0.4f, 1f),

            // Quests - blue tones
            SagaTransactionType.QuestAccepted => new Vector4(0.4f, 0.6f, 1f, 1f),
            SagaTransactionType.QuestCompleted => new Vector4(0.4f, 0.9f, 0.4f, 1f),
            SagaTransactionType.QuestFailed => new Vector4(0.8f, 0.3f, 0.3f, 1f),
            SagaTransactionType.QuestObjectiveCompleted => new Vector4(0.5f, 0.7f, 1f, 1f),
            SagaTransactionType.QuestStageAdvanced => new Vector4(0.5f, 0.8f, 1f, 1f),

            // Dialogue/Social - yellow tones
            SagaTransactionType.DialogueStarted => new Vector4(1f, 0.9f, 0.4f, 1f),
            SagaTransactionType.DialogueCompleted => new Vector4(0.9f, 0.85f, 0.5f, 1f),
            SagaTransactionType.ItemTraded => new Vector4(1f, 0.84f, 0f, 1f),

            // Discovery - green tones
            SagaTransactionType.LandmarkDiscovered => new Vector4(0.4f, 0.9f, 0.5f, 1f),
            SagaTransactionType.SagaDiscovered => new Vector4(0.5f, 0.9f, 0.6f, 1f),

            // Loot/Items - gold/orange
            SagaTransactionType.LootAwarded => new Vector4(1f, 0.7f, 0.2f, 1f),
            SagaTransactionType.EquipmentChanged => new Vector4(0.9f, 0.7f, 0.3f, 1f),
            SagaTransactionType.ConsumableUsed => new Vector4(0.8f, 0.6f, 0.9f, 1f),

            // Default - white
            _ => new Vector4(0.9f, 0.9f, 0.9f, 1f)
        };
    }

    private static Vector4 GetTransactionBackgroundColor(SagaTransactionType type)
    {
        return type switch
        {
            // Combat events - slight red tint
            SagaTransactionType.CharacterDefeated or
            SagaTransactionType.CharacterDamaged or
            SagaTransactionType.BattleStarted or
            SagaTransactionType.BattleEnded => new Vector4(0.15f, 0.05f, 0.05f, 0.3f),

            // Quest events - slight blue tint
            SagaTransactionType.QuestAccepted or
            SagaTransactionType.QuestCompleted or
            SagaTransactionType.QuestObjectiveCompleted => new Vector4(0.05f, 0.05f, 0.15f, 0.3f),

            // Discovery events - slight green tint
            SagaTransactionType.LandmarkDiscovered or
            SagaTransactionType.SagaDiscovered => new Vector4(0.05f, 0.15f, 0.05f, 0.3f),

            // Default - neutral
            _ => new Vector4(0.1f, 0.1f, 0.1f, 0.3f)
        };
    }

    #endregion

    #region Achievements

    private void RenderAchievementsContent(SagaMainViewModel viewModel)
    {
        if (viewModel.Achievements == null)
        {
            ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1f), "No achievements available.");
            return;
        }

        // Overall progress bar
        var totalCount = viewModel.Achievements.TotalAchievements;
        var unlockedCount = viewModel.Achievements.UnlockedCount;
        if (totalCount > 0)
        {
            var overallProgress = (float)unlockedCount / totalCount;
            ImGui.PushStyleColor(ImGuiCol.PlotHistogram, new Vector4(1, 0.843f, 0, 1));
            ImGui.ProgressBar(overallProgress, new Vector2(ImGuiSizes.Fill, ImGui.GetFrameHeight()), $"{unlockedCount}/{totalCount}");
            ImGui.PopStyleColor();
        }

        // Filter and toggle row
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - 100);
        ImGui.InputTextWithHint("##AchievementFilter", "Filter...", ref _achievementFilter, 100);
        ImGui.SameLine();
        ImGui.Checkbox("Locked", ref _showLockedAchievements);
        ImGui.Spacing();

        ImGui.BeginChild("AchievementsScroll", new Vector2(ImGuiSizes.Fill, ImGuiSizes.Fill), ImGuiChildFlags.None);

        // Filter achievements
        var filterActive = !string.IsNullOrWhiteSpace(_achievementFilter);

        // Unlocked Achievements
        var unlockedAchievements = viewModel.Achievements.UnlockedAchievements?
            .Where(a => !filterActive ||
                (a.DisplayName?.Contains(_achievementFilter, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (a.Description?.Contains(_achievementFilter, StringComparison.OrdinalIgnoreCase) ?? false))
            .ToList();

        var filteredUnlockedCount = unlockedAchievements?.Count ?? 0;
        ImGui.TextColored(new Vector4(0.5f, 0.8f, 0.5f, 1f), $"Unlocked ({filteredUnlockedCount})");
        ImGui.Spacing();

        if (filteredUnlockedCount > 0)
        {
            foreach (var achievement in unlockedAchievements!)
            {
                RenderAchievementEntry(achievement, isUnlocked: true);
            }
        }
        else if (!filterActive)
        {
            ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1f), "No achievements unlocked yet.");
        }

        // Locked Achievements (collapsible)
        if (_showLockedAchievements)
        {
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            var lockedAchievements = viewModel.Achievements.LockedAchievements?
                .Where(a => !filterActive ||
                    (a.DisplayName?.Contains(_achievementFilter, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    (a.Description?.Contains(_achievementFilter, StringComparison.OrdinalIgnoreCase) ?? false))
                .ToList();

            var filteredLockedCount = lockedAchievements?.Count ?? 0;
            ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1f), $"Locked ({filteredLockedCount})");
            ImGui.Spacing();

            if (filteredLockedCount > 0)
            {
                foreach (var achievement in lockedAchievements!)
                {
                    RenderAchievementEntry(achievement, isUnlocked: false);
                }
            }
            else if (!filterActive)
            {
                ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1f), "All achievements unlocked!");
            }
        }

        ImGui.EndChild();
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
            ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1f), "-");
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
            ImGui.TextColored(new Vector4(0.5f, 0.8f, 0.5f, 1f), $"Unlocked: {achievement.UnlockedDate ?? "Unknown"}");
        }
        else
        {
            // Criteria text with full type support
            var criteriaText = GetAchievementCriteriaText(achievement.CriteriaType, achievement.Threshold);
            ImGui.TextColored(new Vector4(0.6f, 0.8f, 1f, 1f), criteriaText);

            // Progress bar
            var progress = achievement.ProgressPercentage / 100f;
            ImGui.PushStyleColor(ImGuiCol.PlotHistogram, new Vector4(0.3f, 0.6f, 1, 1));
            ImGui.ProgressBar(progress, new Vector2(ImGuiSizes.Fill, ImGui.GetFrameHeight() * 0.7f), $"{achievement.CurrentValue:F0} / {achievement.Threshold:F0}");
            ImGui.PopStyleColor();
        }

        // Developer: Additional metadata
        if (GameConfiguration.ShowDeveloperInfo)
        {
            ImGui.TextColored(GameConfiguration.DevInfoColor, $"Type: {achievement.CriteriaType} | Progress: {achievement.CurrentValue:F0}/{achievement.Threshold:F0}");
        }

        ImGui.EndChild();
        ImGui.PopStyleColor();
        ImGui.Spacing();
    }

    /// <summary>
    /// Gets human-readable criteria text for all 28 achievement criteria types
    /// </summary>
    private static string GetAchievementCriteriaText(AchievementCriteriaType criteriaType, float threshold)
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
            AchievementCriteriaType.CharactersDefeatedByType => $"Defeat {threshold:F0} of specific type",
            AchievementCriteriaType.CharactersDefeatedByTag => $"Defeat {threshold:F0} with tag",
            AchievementCriteriaType.CharactersDefeatedByRef => $"Defeat specific character",
            AchievementCriteriaType.CriticalHitsDealt => $"Deal {threshold:F0} critical hits",
            AchievementCriteriaType.CombosExecuted => $"Execute {threshold:F0} combos",

            // Exploration
            AchievementCriteriaType.SagaArcsDiscovered => $"Discover {threshold:F0} saga arcs",
            AchievementCriteriaType.SagaArcsCompleted => $"Complete {threshold:F0} saga arcs",
            AchievementCriteriaType.LandmarksDiscovered => $"Discover {threshold:F0} landmarks",
            AchievementCriteriaType.SagaTriggersActivated => $"Activate {threshold:F0} triggers",

            // Social
            AchievementCriteriaType.DialogueTreesCompleted => $"Complete {threshold:F0} dialogues",
            AchievementCriteriaType.DialogueNodesVisited => $"Visit {threshold:F0} dialogue nodes",
            AchievementCriteriaType.UniqueCharactersMet => $"Meet {threshold:F0} characters",

            // Traits
            AchievementCriteriaType.TraitsAssigned => $"Assign {threshold:F0} traits",
            AchievementCriteriaType.TraitsAssignedByType => $"Assign {threshold:F0} traits of type",
            AchievementCriteriaType.TraitsAssignedToCharacterType => $"Assign to {threshold:F0} character types",

            // Economy
            AchievementCriteriaType.ItemsTraded => $"Trade {threshold:F0} items",
            AchievementCriteriaType.LootAwarded => $"Collect {threshold:F0} loot items",
            AchievementCriteriaType.QuestTokensEarned => $"Earn {threshold:F0} quest tokens",

            // Quests
            AchievementCriteriaType.QuestsCompleted => $"Complete {threshold:F0} quests",
            AchievementCriteriaType.QuestsCompletedByRef => $"Complete specific quest",

            // Reputation
            AchievementCriteriaType.ReputationReached => $"Reach reputation {threshold:F0}",
            AchievementCriteriaType.FactionsAtReputationLevel => $"Rep with {threshold:F0} factions",

            // Status Effects
            AchievementCriteriaType.StatusEffectsApplied => $"Apply {threshold:F0} effects",

            _ => $"Reach {threshold:F0}"
        };
    }

    #endregion
}
