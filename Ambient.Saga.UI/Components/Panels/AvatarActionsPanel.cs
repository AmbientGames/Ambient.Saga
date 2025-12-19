using Ambient.Saga.Presentation.UI.ViewModels;
using ImGuiNET;
using System.Numerics;
using Ambient.Saga.UI.Components.Utilities;
using Ambient.Saga.UI.Components.Modals;

namespace Ambient.Saga.UI.Components.Panels;

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

        // Calculate available height for content AFTER the header is rendered
        // Reserve space for action buttons at the bottom:
        // spacing (4) + separator (1) + spacing (4) + label (~18) + 4 buttons (25 each) + spacing between (4*3) = ~155
        var actionsHeight = 155f;
        var availableHeight = ImGui.GetContentRegionAvail().Y - actionsHeight;

        // Tab bar with scrollable content area
        ImGui.BeginChild("AvatarTabsContainer", new Vector2(0, availableHeight), ImGuiChildFlags.None);

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

        ImGui.EndChild();

        // Action buttons at bottom (always visible, outside scroll area)
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.TextColored(new Vector4(0.8f, 0.8f, 1, 1), "Actions:");

        if (ImGui.Button("Quest Log", new Vector2(-1, 25)))
        {
            modalManager.OpenQuestLog();
        }

        if (ImGui.Button("View Characters", new Vector2(-1, 25)))
        {
            modalManager.OpenCharacters();
        }

        if (ImGui.Button("View World Catalog", new Vector2(-1, 25)))
        {
            modalManager.OpenWorldCatalog();
        }

        if (ImGui.Button("Faction Reputation", new Vector2(-1, 25)))
        {
            modalManager.OpenFactionReputation();
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

            // Archetype info with bias
            if (!string.IsNullOrEmpty(viewModel.PlayerAvatar.ArchetypeRef))
            {
                var archetype = viewModel.CurrentWorld?.Gameplay?.AvatarArchetypes?
                    .FirstOrDefault(a => a.RefName == viewModel.PlayerAvatar.ArchetypeRef);

                if (archetype != null)
                {
                    ImGui.Spacing();
                    ImGui.Separator();
                    ImGui.Spacing();

                    // Archetype name and affinity
                    ImGui.TextColored(new Vector4(0.8f, 0.5f, 1, 1), "Archetype:");
                    ImGui.SameLine();
                    ImGui.Text(archetype.DisplayName ?? archetype.RefName);

                    if (!string.IsNullOrEmpty(archetype.AffinityRef))
                    {
                        var affinity = viewModel.CurrentWorld?.Gameplay?.CharacterAffinities?
                            .FirstOrDefault(a => a.RefName == archetype.AffinityRef);
                        var affinityName = affinity?.DisplayName ?? archetype.AffinityRef;
                        ImGui.TextColored(new Vector4(0.6f, 0.8f, 1, 1), $"Affinity: {affinityName}");
                    }

                    // Archetype bias (permanent stat modifiers)
                    if (archetype.ArchetypeBias != null)
                    {
                        var bias = archetype.ArchetypeBias;
                        var hasBias = bias.Strength != 0 || bias.Defense != 0 || bias.Speed != 0 || bias.Magic != 0 ||
                                      bias.Health != 1 || bias.Stamina != 1 || bias.Mana != 1 || bias.Insulation != 0;

                        if (hasBias)
                        {
                            ImGui.Spacing();
                            ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.9f, 1), "Archetype Bonuses:");

                            if (bias.Strength != 0) RenderBiasLine("Strength", bias.Strength);
                            if (bias.Defense != 0) RenderBiasLine("Defense", bias.Defense);
                            if (bias.Speed != 0) RenderBiasLine("Speed", bias.Speed);
                            if (bias.Magic != 0) RenderBiasLine("Magic", bias.Magic);
                            if (bias.Health != 1) RenderBiasLine("Health", bias.Health - 1);
                            if (bias.Stamina != 1) RenderBiasLine("Stamina", bias.Stamina - 1);
                            if (bias.Mana != 1) RenderBiasLine("Mana", bias.Mana - 1);
                            if (bias.Insulation != 0) RenderBiasLine("Insulation", bias.Insulation);
                        }
                    }
                }
            }
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

            // Quest Tokens
            if (caps.QuestTokens != null && caps.QuestTokens.Length > 0)
            {
                if (ImGui.CollapsingHeader($"Quest Tokens ({caps.QuestTokens.Length})"))
                {
                    foreach (var token in caps.QuestTokens)
                    {
                        var tokenDef = viewModel.CurrentWorld?.Gameplay?.QuestTokens?.FirstOrDefault(t => t.RefName == token.QuestTokenRef);
                        var name = tokenDef?.DisplayName ?? token.QuestTokenRef;

                        ImGui.Indent();
                        ImGui.BulletText(name);
                        if (tokenDef != null && !string.IsNullOrEmpty(tokenDef.Description))
                        {
                            ImGui.SameLine();
                            ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1), $"- {tokenDef.Description}");
                        }
                        ImGui.Unindent();
                    }
                }
            }
        }

        ImGui.Spacing();
        ImGui.Separator();

        // Collected Affinities (captured from characters)
        if (viewModel.PlayerAvatar?.Affinities != null && viewModel.PlayerAvatar.Affinities.Length > 0)
        {
            ImGui.TextColored(new Vector4(0.8f, 0.5f, 1, 1), "Collected Affinities:");
            ImGui.Spacing();

            // Active affinity indicator
            var activeAffinity = viewModel.PlayerAvatar.ActiveAffinityRef;
            if (!string.IsNullOrEmpty(activeAffinity))
            {
                var activeAffinityDef = viewModel.CurrentWorld?.Gameplay?.CharacterAffinities?.FirstOrDefault(a => a.RefName == activeAffinity);
                var activeName = activeAffinityDef?.DisplayName ?? activeAffinity;
                ImGui.TextColored(new Vector4(0.5f, 1, 0.5f, 1), $"Active: {activeName}");
                ImGui.Spacing();
            }

            foreach (var affinity in viewModel.PlayerAvatar.Affinities)
            {
                var affinityDef = viewModel.CurrentWorld?.Gameplay?.CharacterAffinities?.FirstOrDefault(a => a.RefName == affinity.AffinityRef);
                var name = affinityDef?.DisplayName ?? affinity.AffinityRef;
                var isActive = affinity.AffinityRef == activeAffinity;

                ImGui.Indent();

                var treeNodeOpen = ImGui.TreeNode($"{(isActive ? "★ " : "")}{name}##aff_{affinity.AffinityRef}");

                if (treeNodeOpen)
                {
                    // Source character
                    if (!string.IsNullOrEmpty(affinity.CapturedFromCharacterRef))
                    {
                        var sourceChar = viewModel.CurrentWorld?.Gameplay?.Characters?.FirstOrDefault(c => c.RefName == affinity.CapturedFromCharacterRef);
                        var sourceName = sourceChar?.DisplayName ?? affinity.CapturedFromCharacterRef;
                        ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1), $"From: {sourceName}");
                    }

                    // Acquired date
                    if (!string.IsNullOrEmpty(affinity.AcquiredDate))
                    {
                        ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1), $"Acquired: {affinity.AcquiredDate}");
                    }

                    // Affinity description and matchups
                    if (affinityDef != null)
                    {
                        if (!string.IsNullOrEmpty(affinityDef.Description))
                        {
                            ImGui.TextWrapped(affinityDef.Description);
                        }

                        if (affinityDef.Matchup != null && affinityDef.Matchup.Length > 0)
                        {
                            ImGui.Spacing();
                            ImGui.TextColored(new Vector4(0.8f, 0.8f, 1, 1), "Matchups:");
                            ImGui.Indent(10);
                            foreach (var matchup in affinityDef.Matchup)
                            {
                                var targetAffinityDef = viewModel.CurrentWorld?.Gameplay?.CharacterAffinities?.FirstOrDefault(a => a.RefName == matchup.TargetAffinityRef);
                                var targetName = targetAffinityDef?.DisplayName ?? matchup.TargetAffinityRef;
                                var color = matchup.Multiplier > 1.0
                                    ? new Vector4(0.2f, 1, 0.2f, 1)  // Green for strong
                                    : new Vector4(1, 0.5f, 0.2f, 1); // Orange for weak
                                ImGui.TextColored(color, $"vs {targetName}: {matchup.Multiplier}x");
                            }
                            ImGui.Unindent(10);
                        }
                    }

                    ImGui.TreePop();
                }

                ImGui.Unindent();
            }

            ImGui.Spacing();
            ImGui.Separator();
        }

        // Party/Companions
        if (viewModel.PlayerAvatar?.Party?.Member != null && viewModel.PlayerAvatar.Party.Member.Length > 0)
        {
            ImGui.TextColored(new Vector4(1, 0.8f, 0.5f, 1), "Party Members:");
            ImGui.Spacing();

            foreach (var member in viewModel.PlayerAvatar.Party.Member)
            {
                var memberChar = viewModel.CurrentWorld?.Gameplay?.Characters?.FirstOrDefault(c => c.RefName == member.CharacterRef);
                var memberName = memberChar?.DisplayName ?? member.CharacterRef;

                ImGui.Indent();

                var treeNodeOpen = ImGui.TreeNode($"{memberName}##party_{member.CharacterRef}");

                if (treeNodeOpen)
                {
                    if (memberChar != null && !string.IsNullOrEmpty(memberChar.Description))
                    {
                        ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1), memberChar.Description);
                    }

                    // Show member's affinity if available
                    if (memberChar?.AffinityRef != null)
                    {
                        var memberAffinity = viewModel.CurrentWorld?.Gameplay?.CharacterAffinities?.FirstOrDefault(a => a.RefName == memberChar.AffinityRef);
                        var affinityName = memberAffinity?.DisplayName ?? memberChar.AffinityRef;
                        ImGui.TextColored(new Vector4(0.8f, 0.5f, 1, 1), $"Affinity: {affinityName}");
                    }

                    ImGui.TreePop();
                }

                ImGui.Unindent();
            }

            ImGui.Spacing();
            ImGui.Separator();
        }

        // Lifetime Statistics
        if (viewModel.PlayerAvatar != null)
        {
            ImGui.TextColored(new Vector4(0.7f, 0.9f, 1, 1), "Lifetime Statistics:");
            ImGui.Spacing();

            var avatar = viewModel.PlayerAvatar;

            // Play time
            var playTimeHours = avatar.PlayTimeHours;
            if (playTimeHours >= 1)
            {
                RenderStatLine("Play Time:", $"{playTimeHours:F1} hours");
            }
            else
            {
                RenderStatLine("Play Time:", $"{playTimeHours * 60:F0} minutes");
            }

            // Distance traveled
            var distance = avatar.DistanceTraveled;
            if (distance >= 1000)
            {
                RenderStatLine("Distance:", $"{distance / 1000:F2} km");
            }
            else
            {
                RenderStatLine("Distance:", $"{distance:F0} m");
            }

            // Blocks
            RenderStatLine("Blocks Placed:", $"{avatar.BlocksPlaced:N0}");
            RenderStatLine("Blocks Destroyed:", $"{avatar.BlocksDestroyed:N0}");
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

    private void RenderBiasLine(string statName, float modifier)
    {
        var color = modifier > 0
            ? new Vector4(0.2f, 1, 0.2f, 1)   // Green for positive
            : new Vector4(1, 0.4f, 0.4f, 1);  // Red for negative
        var sign = modifier > 0 ? "+" : "";
        ImGui.TextColored(color, $"  {statName}: {sign}{modifier:P0}");
    }
}
