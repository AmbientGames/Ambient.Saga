using Ambient.Saga.Presentation.UI.ViewModels;
using ImGuiNET;
using System.Numerics;
using Ambient.Saga.Presentation.UI.Components.Utilities;

namespace Ambient.Saga.Presentation.UI.Components.Panels;

/// <summary>
/// Left panel showing map controls, feature legend, and world catalog.
/// GAME-REUSABLE: This panel is designed to be dropped into the actual game.
/// World selection has been moved to WorldSelectionScreen (Sandbox-specific).
/// </summary>
public class WorldInfoPanel
{
    public void Render(MainViewModel viewModel)
    {
        ImGui.TextColored(new Vector4(1, 1, 0.5f, 1), "WORLD");
        ImGui.Separator();

        // Height map info
        if (!string.IsNullOrEmpty(viewModel.HeightMapInfo))
        {
            ImGui.Spacing();
            ImGui.TextWrapped(viewModel.HeightMapInfo);
        }

        ImGui.Spacing();
        ImGui.Separator();

        // Map controls
        ImGui.TextColored(new Vector4(0.8f, 0.8f, 1, 1), "Map Controls:");
        ImGui.Text($"Zoom: {viewModel.ZoomFactor:F2}x");

        // Mouse position (live)
        if (viewModel.HasMousePosition)
        {
            ImGui.Spacing();
            ImGui.TextColored(new Vector4(1, 1, 0.5f, 1), "Mouse:");
            ImGui.Text($"Lat: {viewModel.MouseLatitude:F6}°");
            ImGui.Text($"Long: {viewModel.MouseLongitude:F6}°");
            ImGui.Text($"Elevation: {viewModel.MouseElevation}m");
        }

        ImGui.Spacing();
        ImGui.Separator();

        // Feature legend (matches WPF three-color-dot style)
        ImGui.TextColored(new Vector4(0.8f, 0.8f, 1, 1), "Features:");
        ImGui.Spacing();

        // Structure (Blue shades)
        RenderFeatureLegend("Structure",
            new Vector4(0.118f, 0.565f, 1.0f, 1.0f),    // #1E90FF - Available
            new Vector4(0.275f, 0.510f, 0.706f, 1.0f),  // #4682B4 - Locked
            new Vector4(0.502f, 0.502f, 0.502f, 1.0f)); // #808080 - Complete

        // Landmark (Red shades)
        RenderFeatureLegend("Landmark",
            new Vector4(1.0f, 0.271f, 0.0f, 1.0f),      // #FF4500 - Available
            new Vector4(0.698f, 0.133f, 0.133f, 1.0f),  // #B22222 - Locked
            new Vector4(0.502f, 0.502f, 0.502f, 1.0f)); // #808080 - Complete

        // Quest Signpost (Green shades)
        RenderFeatureLegend("Quest Signpost",
            new Vector4(0.196f, 0.804f, 0.196f, 1.0f),  // #32CD32 - Available
            new Vector4(0.420f, 0.557f, 0.137f, 1.0f),  // #6B8E23 - Locked
            new Vector4(0.502f, 0.502f, 0.502f, 1.0f)); // #808080 - Complete

        // Hint text about hovering
        ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1), "• Hover to see character triggers");

        ImGui.Spacing();
        ImGui.Separator();

        // Trigger Rings legend (matching WPF)
        ImGui.TextColored(new Vector4(0.8f, 0.8f, 1, 1), "Trigger Rings:");
        ImGui.Spacing();
        ImGui.Indent(10);
        RenderLegendCircle(new Vector4(0.196f, 0.804f, 0.196f, 1.0f), "Available", filled: false); // #32CD32
        RenderLegendCircle(new Vector4(1, 0, 0, 1), "Locked", filled: false);
        RenderLegendCircle(new Vector4(0.502f, 0.502f, 0.502f, 1.0f), "Complete", filled: false); // #808080
        ImGui.Unindent(10);

        ImGui.Spacing();
        ImGui.Separator();

        // Spawned Characters legend (matching WPF)
        ImGui.TextColored(new Vector4(0.8f, 0.8f, 1, 1), "Spawned Characters:");
        ImGui.Spacing();
        ImGui.Indent(10);
        RenderLegendCircle(new Vector4(1, 0, 0, 1), "Boss (NPC)", filled: true); // Red
        RenderLegendCircle(new Vector4(1, 0.843f, 0, 1), "Merchant (NPC)", filled: true); // Gold
        RenderLegendCircle(new Vector4(0, 0, 1, 1), "NPC", filled: true); // Blue
        RenderLegendCircle(new Vector4(0, 1, 1, 1), "Player (multiplayer)", filled: true); // Cyan
        RenderLegendCircle(new Vector4(0, 1, 0, 1), "Avatar (you)", filled: true); // Lime
        ImGui.Unindent(10);

        ImGui.Spacing();
        ImGui.Separator();

        // World Catalog (detailed expandable sections like WPF)
        if (viewModel.CurrentWorld != null)
        {
            ImGui.TextColored(new Vector4(0.8f, 0.8f, 1, 1), "World Catalog:");
            ImGui.Spacing();

            // Gameplay Elements
            ImGui.TextColored(new Vector4(0.5f, 0.8f, 1, 1), "Gameplay Elements");

            //// Blocks (collapsible with basic list)
            //var blocks = viewModel.CurrentWorld.Simulation?.Blocks?.BlockList;
            //if (blocks != null && ImGui.CollapsingHeader($"Blocks ({blocks.Length})"))
            //{
            //    ImGui.Indent(10);
            //    foreach (var block in blocks.Take(10)) // Show first 10
            //    {
            //        ImGui.BulletText(block.DisplayName ?? block.RefName);
            //    }
            //    if (blocks.Length > 10)
            //    {
            //        ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1), $"... and {blocks.Length - 10} more");
            //    }
            //    ImGui.Unindent(10);
            //}

            // Tools (collapsible with basic list)
            var tools = viewModel.CurrentWorld.Gameplay?.Tools;
            if (tools != null && ImGui.CollapsingHeader($"Tools ({tools.Length})"))
            {
                ImGui.Indent(10);
                foreach (var tool in tools)
                {
                    ImGui.BulletText(tool.DisplayName ?? tool.RefName);
                }
                ImGui.Unindent(10);
            }

            // Materials (collapsible with detailed expandable items)
            var materials = viewModel.CurrentWorld.Gameplay?.BuildingMaterials;
            if (materials != null && ImGui.CollapsingHeader($"Materials ({materials.Length})"))
            {
                ImGui.Indent(10);
                foreach (var material in materials)
                {
                    var treeNodeOpen = ImGui.TreeNode(material.DisplayName ?? material.RefName);
                    if (treeNodeOpen)
                    {
                        if (!string.IsNullOrEmpty(material.Description))
                        {
                            ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1), material.Description);
                        }
                        ImGui.TextColored(new Vector4(0.5f, 1, 0.5f, 1), $"Price: {material.WholesalePrice}");
                        ImGui.TextColored(new Vector4(1, 0.843f, 0, 1), $"Markup: {material.MerchantMarkupMultiplier}x");
                        ImGui.TreePop();
                    }
                }
                ImGui.Unindent(10);
            }

            ImGui.Spacing();

            // RPG Elements
            ImGui.TextColored(new Vector4(1, 0.7f, 0.7f, 1), "RPG Elements");

            // Equipment (collapsible with detailed expandable items)
            var equipment = viewModel.CurrentWorld.Gameplay?.Equipment;
            if (equipment != null && ImGui.CollapsingHeader($"Equipment ({equipment.Length})"))
            {
                ImGui.Indent(10);
                foreach (var equip in equipment)
                {
                    var treeNodeOpen = ImGui.TreeNode(equip.DisplayName ?? equip.RefName);
                    if (treeNodeOpen)
                    {
                        if (!string.IsNullOrEmpty(equip.Description))
                        {
                            ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1), equip.Description);
                            ImGui.Spacing();
                        }
                        if (equip.Effects != null)
                        {
                            ImGuiHelpers.RenderCharacterEffects(equip.Effects);
                        }
                        ImGui.TreePop();
                    }
                }
                ImGui.Unindent(10);
            }

            // Consumables (collapsible with detailed expandable items)
            var consumables = viewModel.CurrentWorld.Gameplay?.Consumables;
            if (consumables != null && ImGui.CollapsingHeader($"Consumables ({consumables.Length})"))
            {
                ImGui.Indent(10);
                foreach (var consumable in consumables)
                {
                    var treeNodeOpen = ImGui.TreeNode(consumable.DisplayName ?? consumable.RefName);
                    if (treeNodeOpen)
                    {
                        if (!string.IsNullOrEmpty(consumable.Description))
                        {
                            ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1), consumable.Description);
                            ImGui.Spacing();
                        }
                        if (consumable.Effects != null)
                        {
                            ImGuiHelpers.RenderCharacterEffects(consumable.Effects);
                        }
                        ImGui.TreePop();
                    }
                }
                ImGui.Unindent(10);
            }

            // Spells (collapsible with detailed expandable items)
            var spells = viewModel.CurrentWorld.Gameplay?.Spells;
            if (spells != null && ImGui.CollapsingHeader($"Spells ({spells.Length})"))
            {
                ImGui.Indent(10);
                foreach (var spell in spells)
                {
                    var treeNodeOpen = ImGui.TreeNode(spell.DisplayName ?? spell.RefName);
                    if (treeNodeOpen)
                    {
                        if (!string.IsNullOrEmpty(spell.Description))
                        {
                            ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1), spell.Description);
                            ImGui.Spacing();
                        }
                        if (spell.Effects != null)
                        {
                            ImGuiHelpers.RenderCharacterEffects(spell.Effects);
                        }
                        ImGui.TreePop();
                    }
                }
                ImGui.Unindent(10);
            }

            // Character Archetypes (collapsible with detailed expandable items)
            var archetypes = viewModel.CurrentWorld.Gameplay?.AvatarArchetypes;
            if (archetypes != null && ImGui.CollapsingHeader($"Character Archetypes ({archetypes.Length})"))
            {
                ImGui.Indent(10);
                foreach (var archetype in archetypes)
                {
                    var treeNodeOpen = ImGui.TreeNode(archetype.DisplayName ?? archetype.RefName);
                    if (treeNodeOpen)
                    {
                        if (!string.IsNullOrEmpty(archetype.Description))
                        {
                            ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1), archetype.Description);
                            ImGui.Spacing();
                        }
                        ImGui.Text($"Affinity: {archetype.AffinityRef}");
                        ImGui.TreePop();
                    }
                }
                ImGui.Unindent(10);
            }

            // Characters (NPC/boss templates)
            var characters = viewModel.CurrentWorld.Gameplay?.Characters;
            if (characters != null && ImGui.CollapsingHeader($"Characters ({characters.Length})"))
            {
                ImGui.Indent(10);
                foreach (var character in characters)
                {
                    var treeNodeOpen = ImGui.TreeNode(character.DisplayName ?? character.RefName);
                    if (treeNodeOpen)
                    {
                        if (!string.IsNullOrEmpty(character.Description))
                        {
                            ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1), character.Description);
                            ImGui.Spacing();
                        }
                        if (character.Interactable != null && !string.IsNullOrEmpty(character.Interactable.DialogueTreeRef))
                        {
                            ImGui.Text($"Dialogue: {character.Interactable.DialogueTreeRef}");
                        }
                        ImGui.TreePop();
                    }
                }
                ImGui.Unindent(10);
            }

            // Character Affinities (combat types)
            var affinities = viewModel.CurrentWorld.Gameplay?.CharacterAffinities;
            if (affinities != null && ImGui.CollapsingHeader($"Character Affinities ({affinities.Length})"))
            {
                ImGui.Indent(10);
                foreach (var affinity in affinities)
                {
                    var treeNodeOpen = ImGui.TreeNode(affinity.DisplayName ?? affinity.RefName);
                    if (treeNodeOpen)
                    {
                        if (!string.IsNullOrEmpty(affinity.Description))
                        {
                            ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1), affinity.Description);
                            ImGui.Spacing();
                        }
                        if (affinity.Matchup != null && affinity.Matchup.Length > 0)
                        {
                            ImGui.TextColored(new Vector4(0.8f, 0.8f, 1, 1), "Matchups:");
                            ImGui.Indent(10);
                            foreach (var matchup in affinity.Matchup)
                            {
                                var color = matchup.Multiplier > 1.0
                                    ? new Vector4(0.2f, 1, 0.2f, 1)  // Green for strong
                                    : new Vector4(1, 0.5f, 0.2f, 1); // Orange for weak
                                ImGui.TextColored(color, $"vs {matchup.TargetAffinityRef}: {matchup.Multiplier}x");
                            }
                            ImGui.Unindent(10);
                        }
                        ImGui.TreePop();
                    }
                }
                ImGui.Unindent(10);
            }

            // Quests (quest templates)
            var quests = viewModel.CurrentWorld.Gameplay?.Quests;
            if (quests != null && ImGui.CollapsingHeader($"Quests ({quests.Length})"))
            {
                ImGui.Indent(10);
                foreach (var quest in quests)
                {
                    var treeNodeOpen = ImGui.TreeNode(quest.DisplayName ?? quest.RefName);
                    if (treeNodeOpen)
                    {
                        if (!string.IsNullOrEmpty(quest.Description))
                        {
                            ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1), quest.Description);
                            ImGui.Spacing();
                        }
                        if (quest.Stages?.Stage != null && quest.Stages.Stage.Length > 0)
                        {
                            ImGui.Text($"Stages: {quest.Stages.Stage.Length}");
                        }
                        ImGui.TreePop();
                    }
                }
                ImGui.Unindent(10);
            }

            // Dialogue Trees
            var dialogueTrees = viewModel.CurrentWorld.Gameplay?.DialogueTrees;
            if (dialogueTrees != null && ImGui.CollapsingHeader($"Dialogue Trees ({dialogueTrees.Length})"))
            {
                ImGui.Indent(10);
                foreach (var tree in dialogueTrees)
                {
                    var treeNodeOpen = ImGui.TreeNode(tree.RefName);
                    if (treeNodeOpen)
                    {
                        ImGui.Text($"Start Node: {tree.StartNodeId}");
                        if (tree.Node != null && tree.Node.Length > 0)
                        {
                            ImGui.Text($"Nodes: {tree.Node.Length}");
                        }
                        ImGui.TreePop();
                    }
                }
                ImGui.Unindent(10);
            }

            // Combat Stances
            var stances = viewModel.CurrentWorld.Gameplay?.CombatStances;
            if (stances != null && ImGui.CollapsingHeader($"Combat Stances ({stances.Length})"))
            {
                ImGui.Indent(10);
                foreach (var stance in stances)
                {
                    var treeNodeOpen = ImGui.TreeNode(stance.DisplayName ?? stance.RefName);
                    if (treeNodeOpen)
                    {
                        if (!string.IsNullOrEmpty(stance.Description))
                        {
                            ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1), stance.Description);
                        }
                        ImGui.TreePop();
                    }
                }
                ImGui.Unindent(10);
            }

            // Loadout Slots
            var loadoutSlots = viewModel.CurrentWorld.Gameplay?.LoadoutSlots;
            if (loadoutSlots != null && ImGui.CollapsingHeader($"Loadout Slots ({loadoutSlots.Length})"))
            {
                ImGui.Indent(10);
                foreach (var slot in loadoutSlots)
                {
                    var treeNodeOpen = ImGui.TreeNode(slot.DisplayName ?? slot.RefName);
                    if (treeNodeOpen)
                    {
                        if (!string.IsNullOrEmpty(slot.Description))
                        {
                            ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1), slot.Description);
                        }
                        ImGui.TreePop();
                    }
                }
                ImGui.Unindent(10);
            }

            ImGui.Spacing();

            // Progression Systems
            ImGui.TextColored(new Vector4(1, 0.843f, 0, 1), "Progression");

            // Achievements (collapsible with detailed expandable items)
            var achievements = viewModel.CurrentWorld.Gameplay?.Achievements;
            if (achievements != null && ImGui.CollapsingHeader($"Achievements ({achievements.Length})"))
            {
                ImGui.Indent(10);
                foreach (var achievement in achievements)
                {
                    var treeNodeOpen = ImGui.TreeNode(achievement.DisplayName ?? achievement.RefName);
                    if (treeNodeOpen)
                    {
                        if (!string.IsNullOrEmpty(achievement.Description))
                        {
                            ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1), achievement.Description);
                            ImGui.Spacing();
                        }
                        ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1), $"Steam ID: {achievement.RefName}");

                        if (achievement.Criteria != null)
                        {
                            ImGui.Spacing();
                            ImGui.TextColored(new Vector4(0.8f, 0.8f, 1, 1), "Unlock Criteria:");
                            ImGui.Indent(10);
                            ImGui.Text($"Type: {achievement.Criteria.Type}");
                            ImGui.Text($"Threshold: {achievement.Criteria.Threshold:F0}");
                            ImGui.Unindent(10);
                        }
                        ImGui.TreePop();
                    }
                }
                ImGui.Unindent(10);
            }

            ImGui.Spacing();
            ImGui.Separator();

            // Steam Testing Section (matching WPF)
            ImGui.TextColored(new Vector4(1, 0.647f, 0, 1), "Steam Testing");
            ImGui.Spacing();

            if (ImGui.Button("Test Steam Achievement (ACH_HEAVY_FIRE)", new Vector2(-1, 25)))
            {
                if (viewModel.TestSteamAchievementCommand?.CanExecute(null) == true)
                {
                    viewModel.TestSteamAchievementCommand.Execute(null);
                }
            }

            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Directly set ACH_HEAVY_FIRE achievement to Steam and query status");
            }

            ImGui.Spacing();
            ImGui.Separator();
        }

        // Spawn Character button (matching WPF at bottom)
        if (ImGui.Button("Spawn Character", new Vector2(-1, 25)))
        {
            if (viewModel.ViewCharactersCommand.CanExecute(null))
            {
                viewModel.ViewCharactersCommand.Execute(null);
            }
        }
    }

    private void RenderLegendItem(string label, Vector4 color, string state1, string state2)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, color);
        ImGui.Text($"● {label}");
        ImGui.PopStyleColor();

        ImGui.Indent(20);
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.7f, 0.7f, 0.7f, 1));
        ImGui.Text($"• {state1}");
        if (!string.IsNullOrEmpty(state2))
        {
            ImGui.Text($"• {state2}");
        }
        ImGui.PopStyleColor();
        ImGui.Unindent(20);
    }

    private void RenderFeatureLegend(string label, Vector4 availableColor, Vector4 lockedColor, Vector4 completeColor)
    {
        // Feature type name
        ImGui.Text(label);
        ImGui.Indent(10);

        // Three-dot status legend (matching WPF) - draw circles instead of Unicode
        RenderLegendCircle(availableColor, "Available", filled: true);
        RenderLegendCircle(lockedColor, "Locked", filled: true);
        RenderLegendCircle(completeColor, "Complete", filled: true);

        ImGui.Unindent(10);
        ImGui.Spacing();
    }

    private void RenderLegendCircle(Vector4 color, string label, bool filled)
    {
        var drawList = ImGui.GetWindowDrawList();
        var cursorPos = ImGui.GetCursorScreenPos();

        // Draw circle at current position
        var circleCenter = new Vector2(cursorPos.X + 5, cursorPos.Y + 7); // Offset to align with text
        var radius = 4f;
        var circleColor = ImGui.ColorConvertFloat4ToU32(color);

        if (filled)
        {
            drawList.AddCircleFilled(circleCenter, radius, circleColor, 12);
        }
        else
        {
            drawList.AddCircle(circleCenter, radius, circleColor, 12, 2.0f);
        }

        // Move cursor past the circle and render text
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 15);
        ImGui.Text(label);
    }
}
