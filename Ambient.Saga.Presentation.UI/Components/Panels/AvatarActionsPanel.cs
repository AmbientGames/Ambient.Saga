using Ambient.Saga.Presentation.UI.ViewModels;
using Ambient.Saga.Presentation.UI.Components.Modals;
using Ambient.Saga.Presentation.UI.Components.Utilities;
using ImGuiNET;
using System.Numerics;

namespace Ambient.Saga.Presentation.UI.Components.Panels;

/// <summary>
/// Right panel showing avatar info, quests, and achievements in tabs
/// Matches WPF version functionality with tabs instead of modals
/// </summary>
public class AvatarActionsPanel
{
    private bool _showCompletedQuests = false;
    private bool _showLockedAchievements = false;

    private ModalManager? _modalManager;

    public void Render(MainViewModel viewModel, ModalManager modalManager)
    {
        _modalManager = modalManager;

        ImGui.TextColored(new Vector4(0.5f, 1, 0.5f, 1), "AVATAR");
        ImGui.Separator();

        // Tab bar
        if (ImGui.BeginTabBar("AvatarTabs", ImGuiTabBarFlags.None))
        {
            if (ImGui.BeginTabItem("Info"))
            {
                RenderAvatarInfoTab(viewModel);
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Quests"))
            {
                RenderQuestsTab(viewModel);
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Achievements"))
            {
                RenderAchievementsTab(viewModel);
                ImGui.EndTabItem();
            }

            ImGui.EndTabBar();
        }

        // Action buttons at bottom (outside tabs)
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.TextColored(new Vector4(0.8f, 0.8f, 1, 1), "Actions:");

        if (ImGui.Button("Quest Log", new Vector2(-1, 25)))
        {
            modalManager.ShowQuestLog = true;
        }

        if (ImGui.Button("View Characters", new Vector2(-1, 25)))
        {
            modalManager.ShowCharacters = true;
        }

        if (ImGui.Button("View World Catalog", new Vector2(-1, 25)))
        {
            modalManager.ShowWorldCatalog = true;
        }
    }

    private void RenderAvatarInfoTab(MainViewModel viewModel)
    {
        ImGui.BeginChild("AvatarInfoScroll", new Vector2(0, 0), ImGuiChildFlags.None);

        // Position
        ImGui.TextColored(new Vector4(1, 1, 0, 1), "Position:");
        if (viewModel.HasAvatarPosition)
        {
            ImGui.Text($"Lat: {viewModel.AvatarLatitude:F6}°");
            ImGui.Text($"Long: {viewModel.AvatarLongitude:F6}°");
            ImGui.Text($"Elevation: {viewModel.AvatarElevation}m");
        }
        else
        {
            ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1), "No position set");
            ImGui.TextWrapped("Click on map to move");
        }

        ImGui.Spacing();
        ImGui.Separator();

        // Vitals/Stats
        if (viewModel.PlayerAvatar != null && viewModel.PlayerAvatar.Stats != null)
        {
            var vitals = viewModel.PlayerAvatar.Stats;
            var currencyName = viewModel.CurrentWorld.WorldConfiguration.CurrencyName ?? "Credit";
            var pluralCurrency = vitals.Credits == 1 ? currencyName : currencyName + "s";

            ImGui.TextColored(new Vector4(0.5f, 1, 0.5f, 1), "Stats:");

            RenderStatLine("Health:", vitals.Health.ToString());
            RenderStatLine("Strength:", vitals.Strength.ToString());
            RenderStatLine("Defense:", vitals.Defense.ToString());
            RenderStatLine("Speed:", vitals.Speed.ToString());
            RenderStatLine("Magic:", vitals.Magic.ToString());
            RenderStatLine("Temperature:", $"{vitals.Temperature:F1}°C");
            RenderStatLine("Hunger:", vitals.Hunger.ToString());
            RenderStatLine("Thirst:", vitals.Thirst.ToString());
            RenderStatLine($"{pluralCurrency}:", $"{vitals.Credits:N0}");
        }
        else
        {
            ImGui.TextColored(new Vector4(1, 0.5f, 0, 1), "No avatar created");
            ImGui.TextWrapped("Enter a world to select archetype");
        }

        ImGui.Spacing();
        ImGui.Separator();

        // Inventory
        if (viewModel.PlayerAvatar?.Capabilities != null)
        {
            var caps = viewModel.PlayerAvatar.Capabilities;

            ImGui.TextColored(new Vector4(0.5f, 1, 0.5f, 1), "Inventory:");

            // Gameplay Elements
            ImGui.TextColored(new Vector4(0.5f, 0.8f, 1, 1), "Gameplay Elements");

            // Blocks
            if (ImGui.CollapsingHeader($"Blocks ({caps.Blocks.Length})"))
            {
                if (caps.Blocks != null)
                {
                    foreach (var block in caps.Blocks)
                    {
                        ImGui.Indent();
                        ImGui.BulletText($"{block.BlockRef} x{block.Quantity}");
                        ImGui.Unindent();
                    }
                }
            }

            // Tools
            if (ImGui.CollapsingHeader($"Tools ({caps.Tools.Length})"))
            {
                if (caps.Tools != null)
                {
                    foreach (var tool in caps.Tools)
                    {
                        ImGui.Indent();
                        ImGui.BulletText($"{tool.ToolRef} ({tool.Condition:P0})");
                        ImGui.Unindent();
                    }
                }
            }

            // Materials
            if (ImGui.CollapsingHeader($"Materials ({caps.BuildingMaterials.Length})"))
            {
                if (caps.BuildingMaterials != null)
                {
                    foreach (var material in caps.BuildingMaterials)
                    {
                        var materialItem = viewModel.CurrentWorld?.TryGetBuildingMaterialByRefName(material.BuildingMaterialRef);
                        var name = materialItem?.DisplayName ?? material.BuildingMaterialRef;

                        ImGui.Indent();

                        // Expandable header for each material
                        var treeNodeOpen = ImGui.TreeNode($"{name} x{material.Quantity}");

                        if (treeNodeOpen)
                        {
                            if (materialItem != null)
                            {
                                // Description
                                if (!string.IsNullOrEmpty(materialItem.Description))
                                {
                                    ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1), materialItem.Description);
                                    ImGui.Spacing();
                                }

                                // Pricing information
                                ImGui.TextColored(new Vector4(0.5f, 1, 0.5f, 1), $"Price: {materialItem.WholesalePrice}");
                                ImGui.TextColored(new Vector4(1, 0.843f, 0, 1), $"Markup: {materialItem.MerchantMarkupMultiplier}x");
                            }

                            ImGui.TreePop();
                        }

                        ImGui.Unindent();
                    }
                }
            }

            ImGui.Spacing();
            ImGui.TextColored(new Vector4(1, 0.7f, 0.7f, 1), "RPG Elements");

            // Equipment
            if (ImGui.CollapsingHeader($"Equipment ({caps.Equipment.Length})"))
            {
                if (caps.Equipment != null)
                {
                    foreach (var equip in caps.Equipment)
                    {
                        var equipItem = viewModel.CurrentWorld?.Gameplay?.Equipment?.FirstOrDefault(e => e.RefName == equip.EquipmentRef);
                        var name = equipItem?.DisplayName ?? equip.EquipmentRef;

                        ImGui.Indent();

                        // Expandable header for each equipment item
                        var treeNodeOpen = ImGui.TreeNode($"{name} ({equip.Condition:P0})");

                        if (treeNodeOpen)
                        {
                            if (equipItem != null)
                            {
                                // Description
                                if (!string.IsNullOrEmpty(equipItem.Description))
                                {
                                    ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1), equipItem.Description);
                                    ImGui.Spacing();
                                }

                                // Effects
                                if (equipItem.Effects != null)
                                {
                                    ImGuiHelpers.RenderCharacterEffects(equipItem.Effects);
                                }
                            }

                            ImGui.TreePop();
                        }

                        ImGui.Unindent();
                    }
                }
            }

            // Consumables
            if (ImGui.CollapsingHeader($"Consumables ({caps.Consumables.Length})"))
            {
                if (caps.Consumables != null)
                {
                    foreach (var consumable in caps.Consumables)
                    {
                        var consumableItem = viewModel.CurrentWorld?.Gameplay?.Consumables?.FirstOrDefault(c => c.RefName == consumable.ConsumableRef);
                        var name = consumableItem?.DisplayName ?? consumable.ConsumableRef;

                        ImGui.Indent();

                        // Expandable header for each consumable item
                        var treeNodeOpen = ImGui.TreeNode($"{name} x{consumable.Quantity}");

                        if (treeNodeOpen)
                        {
                            if (consumableItem != null)
                            {
                                // Description
                                if (!string.IsNullOrEmpty(consumableItem.Description))
                                {
                                    ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1), consumableItem.Description);
                                    ImGui.Spacing();
                                }

                                // Effects
                                if (consumableItem.Effects != null)
                                {
                                    ImGuiHelpers.RenderCharacterEffects(consumableItem.Effects);
                                }
                            }

                            ImGui.TreePop();
                        }

                        ImGui.Unindent();
                    }
                }
            }

            // Spells
            if (ImGui.CollapsingHeader($"Spells ({caps.Spells.Length})"))
            {
                if (caps.Spells != null)
                {
                    foreach (var spell in caps.Spells)
                    {
                        var spellItem = viewModel.CurrentWorld?.Gameplay?.Spells?.FirstOrDefault(s => s.RefName == spell.SpellRef);
                        var name = spellItem?.DisplayName ?? spell.SpellRef;

                        ImGui.Indent();

                        // Expandable header for each spell
                        var treeNodeOpen = ImGui.TreeNode($"{name} ({spell.Condition:P0})");

                        if (treeNodeOpen)
                        {
                            if (spellItem != null)
                            {
                                // Description
                                if (!string.IsNullOrEmpty(spellItem.Description))
                                {
                                    ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1), spellItem.Description);
                                    ImGui.Spacing();
                                }

                                // Effects
                                if (spellItem.Effects != null)
                                {
                                    ImGuiHelpers.RenderCharacterEffects(spellItem.Effects);
                                }
                            }

                            ImGui.TreePop();
                        }

                        ImGui.Unindent();
                    }
                }
            }
        }

        ImGui.EndChild();
    }

    private void RenderQuestsTab(MainViewModel viewModel)
    {
        ImGui.BeginChild("QuestsScroll", new Vector2(0, 0), ImGuiChildFlags.None);

        // Show Completed toggle
        ImGui.Checkbox("Show Completed", ref _showCompletedQuests);
        ImGui.Spacing();

        if (viewModel.QuestLog == null)
        {
            ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1), "No quest log available");
            ImGui.EndChild();
            return;
        }

        // Active Quests
        if (viewModel.QuestLog.ActiveQuests?.Count > 0)
        {
            ImGui.TextColored(new Vector4(0.5f, 1, 0.5f, 1), "Active Quests:");
            ImGui.Spacing();

            foreach (var quest in viewModel.QuestLog.ActiveQuests)
            {
                ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.1f, 0.1f, 0.1f, 0.3f));
                ImGui.BeginChild($"quest_{quest.RefName}", new Vector2(0, 80), ImGuiChildFlags.Borders);

                ImGui.TextColored(new Vector4(1, 1, 0, 1), quest.DisplayName ?? quest.RefName);
                if (!string.IsNullOrEmpty(quest.Description))
                {
                    ImGui.TextWrapped(quest.Description);
                }

                // Progress bar
                var progress = (float)quest.ProgressPercentage / 100f;
                ImGui.ProgressBar(progress, new Vector2(-1, 20), quest.ProgressText);

                // Make the card clickable
                ImGui.SetCursorPos(new Vector2(0, 0));
                ImGui.InvisibleButton($"quest_click_{quest.RefName}", new Vector2(ImGui.GetContentRegionAvail().X, 80));

                if (ImGui.IsItemHovered())
                {
                    ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
                }

                if (ImGui.IsItemClicked() && _modalManager != null)
                {
                    // Open quest detail modal
                    _modalManager.OpenQuestDetail(quest.RefName);
                }

                ImGui.EndChild();
                ImGui.PopStyleColor();
                ImGui.Spacing();
            }
        }
        else
        {
            ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1), "No active quests");
        }

        // Completed Quests
        if (_showCompletedQuests && viewModel.QuestLog.CompletedQuests?.Count > 0)
        {
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.TextColored(new Vector4(0.5f, 1, 0.5f, 1), "Completed Quests:");
            ImGui.Spacing();

            foreach (var quest in viewModel.QuestLog.CompletedQuests)
            {
                ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.0f, 0.2f, 0.0f, 0.2f));
                ImGui.BeginChild($"quest_completed_{quest.RefName}", new Vector2(0, 60), ImGuiChildFlags.Borders);

                ImGui.Text($"✓ {quest.DisplayName ?? quest.RefName}");
                if (!string.IsNullOrEmpty(quest.Description))
                {
                    ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1), quest.Description);
                }

                // Make the card clickable
                ImGui.SetCursorPos(new Vector2(0, 0));
                ImGui.InvisibleButton($"quest_completed_click_{quest.RefName}", new Vector2(ImGui.GetContentRegionAvail().X, 60));

                if (ImGui.IsItemHovered())
                {
                    ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
                }

                if (ImGui.IsItemClicked() && _modalManager != null)
                {
                    // Open quest detail modal
                    _modalManager.OpenQuestDetail(quest.RefName);
                }

                ImGui.EndChild();
                ImGui.PopStyleColor();
                ImGui.Spacing();
            }
        }

        ImGui.EndChild();
    }

    private void RenderAchievementsTab(MainViewModel viewModel)
    {
        ImGui.BeginChild("AchievementsScroll", new Vector2(0, 0), ImGuiChildFlags.None);

        if (viewModel.Achievements == null)
        {
            ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1), "No achievements available");
            ImGui.EndChild();
            return;
        }

        // Completion stats
        ImGui.TextColored(new Vector4(0.5f, 1, 0.5f, 1), "Achievements");
        ImGui.SameLine();
        ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1), $"({viewModel.Achievements.CompletionText})");

        ImGui.Spacing();

        // Show Locked toggle
        ImGui.Checkbox("Show Locked", ref _showLockedAchievements);
        ImGui.Spacing();

        // Unlocked Achievements
        if (viewModel.Achievements.UnlockedAchievements?.Count > 0)
        {
            ImGui.TextColored(new Vector4(0.5f, 1, 0.5f, 1), "Unlocked:");
            ImGui.Spacing();

            foreach (var achievement in viewModel.Achievements.UnlockedAchievements)
            {
                ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.0f, 0.2f, 0.0f, 0.2f));
                ImGui.BeginChild($"ach_unlocked_{achievement.RefName}", new Vector2(0, 70), ImGuiChildFlags.Borders);

                ImGui.Text($"🏆 {achievement.DisplayName ?? achievement.RefName}");
                if (!string.IsNullOrEmpty(achievement.Description))
                {
                    ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1), achievement.Description);
                }
                if (!string.IsNullOrEmpty(achievement.StatusText))
                {
                    ImGui.TextColored(new Vector4(0.5f, 0.8f, 0.5f, 1), achievement.StatusText);
                }

                ImGui.EndChild();
                ImGui.PopStyleColor();
                ImGui.Spacing();
            }
        }
        else
        {
            ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1), "No achievements unlocked yet");
        }

        // Locked Achievements
        if (_showLockedAchievements && viewModel.Achievements.LockedAchievements?.Count > 0)
        {
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1), "Locked:");
            ImGui.Spacing();

            foreach (var achievement in viewModel.Achievements.LockedAchievements)
            {
                ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.1f, 0.1f, 0.1f, 0.2f));
                ImGui.BeginChild($"ach_locked_{achievement.RefName}", new Vector2(0, 90), ImGuiChildFlags.Borders);

                ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1), $"🔒 {achievement.DisplayName ?? achievement.RefName}");
                if (!string.IsNullOrEmpty(achievement.Description))
                {
                    ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1), achievement.Description);
                }
                if (!string.IsNullOrEmpty(achievement.CriteriaText))
                {
                    ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1), achievement.CriteriaText);
                }

                // Progress bar
                var progress = (float)achievement.ProgressPercentage / 100f;
                ImGui.ProgressBar(progress, new Vector2(-1, 15), achievement.ProgressText);

                ImGui.EndChild();
                ImGui.PopStyleColor();
                ImGui.Spacing();
            }
        }

        ImGui.EndChild();
    }

    private void RenderStatLine(string label, string value)
    {
        ImGui.Text(label);
        ImGui.SameLine(120);
        ImGui.Text(value);
    }
}
