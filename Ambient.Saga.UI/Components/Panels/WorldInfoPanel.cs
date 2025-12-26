using Ambient.Saga.Presentation.UI.ViewModels;
using ImGuiNET;
using System.Numerics;
using Ambient.Saga.UI.Components.Utilities;

namespace Ambient.Saga.UI.Components.Panels;

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
            ImGui.Spacing();
            ImGui.Separator();
        }

        // World Catalog (detailed expandable sections like WPF)
        if (viewModel.CurrentWorld != null)
        {
            ImGui.TextColored(new Vector4(0.8f, 0.8f, 1, 1), "World Catalog:");
            ImGui.Spacing();

            // Gameplay Elements
            ImGui.TextColored(new Vector4(0.5f, 0.8f, 1, 1), "Gameplay Elements");

            // Blocks (collapsible with detailed expandable items)
            var blocks = viewModel.CurrentWorld.BlockProvider?.GetAllBlocks().ToList();
            if (blocks != null && blocks.Count > 0 && ImGui.CollapsingHeader($"Blocks ({blocks.Count})"))
            {
                ImGui.Indent(10);
                // Group blocks by substance for better organization
                var blocksBySubstance = blocks
                    .GroupBy(b => b.SubstanceRef ?? "Other")
                    .OrderBy(g => g.Key);

                foreach (var group in blocksBySubstance)
                {
                    if (ImGui.TreeNode($"{group.Key} ({group.Count()})"))
                    {
                        foreach (var block in group)
                        {
                            var treeNodeOpen = ImGui.TreeNode($"{block.DisplayName}##{block.RefName}");
                            if (treeNodeOpen)
                            {
                                if (!string.IsNullOrEmpty(block.Description))
                                {
                                    ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1), block.Description);
                                    ImGui.Spacing();
                                }
                                ImGui.Text($"Substance: {block.SubstanceRef ?? "None"}");
                                ImGui.TextColored(new Vector4(0.5f, 1, 0.5f, 1), $"Price: {block.WholesalePrice}");
                                ImGui.TextColored(new Vector4(1, 0.843f, 0, 1), $"Markup: {block.MerchantMarkupMultiplier}x");
                                if (!string.IsNullOrEmpty(block.TextureRef))
                                {
                                    ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1), $"Texture: {block.TextureRef}");
                                }
                                ImGui.TreePop();
                            }
                        }
                        ImGui.TreePop();
                    }
                }
                ImGui.Unindent(10);
            }

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

            // Status Effects
            var statusEffects = viewModel.CurrentWorld.Gameplay?.StatusEffects;
            if (statusEffects != null && ImGui.CollapsingHeader($"Status Effects ({statusEffects.Length})"))
            {
                ImGui.Indent(10);
                foreach (var effect in statusEffects)
                {
                    var treeNodeOpen = ImGui.TreeNode(effect.DisplayName ?? effect.RefName);
                    if (treeNodeOpen)
                    {
                        if (!string.IsNullOrEmpty(effect.Description))
                        {
                            ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1), effect.Description);
                            ImGui.Spacing();
                        }

                        // Effect type and duration
                        ImGui.Text($"Type: {effect.Type}");
                        ImGui.Text($"Duration: {effect.DurationTurns} turns");

                        // Show modifiers if any are non-zero
                        var hasModifiers = effect.StrengthModifier != 0 || effect.DefenseModifier != 0 ||
                                           effect.SpeedModifier != 0 || effect.MagicModifier != 0 ||
                                           effect.AccuracyModifier != 0 || effect.DamagePerTurn != 0;

                        if (hasModifiers)
                        {
                            ImGui.Spacing();
                            ImGui.TextColored(new Vector4(0.8f, 0.8f, 1, 1), "Modifiers:");
                            ImGui.Indent(10);
                            if (effect.StrengthModifier != 0)
                                ImGui.TextColored(effect.StrengthModifier > 0 ? new Vector4(0.2f, 1, 0.2f, 1) : new Vector4(1, 0.3f, 0.3f, 1),
                                    $"Strength: {effect.StrengthModifier:+0.#;-0.#}");
                            if (effect.DefenseModifier != 0)
                                ImGui.TextColored(effect.DefenseModifier > 0 ? new Vector4(0.2f, 1, 0.2f, 1) : new Vector4(1, 0.3f, 0.3f, 1),
                                    $"Defense: {effect.DefenseModifier:+0.#;-0.#}");
                            if (effect.SpeedModifier != 0)
                                ImGui.TextColored(effect.SpeedModifier > 0 ? new Vector4(0.2f, 1, 0.2f, 1) : new Vector4(1, 0.3f, 0.3f, 1),
                                    $"Speed: {effect.SpeedModifier:+0.#;-0.#}");
                            if (effect.MagicModifier != 0)
                                ImGui.TextColored(effect.MagicModifier > 0 ? new Vector4(0.2f, 1, 0.2f, 1) : new Vector4(1, 0.3f, 0.3f, 1),
                                    $"Magic: {effect.MagicModifier:+0.#;-0.#}");
                            if (effect.AccuracyModifier != 0)
                                ImGui.TextColored(effect.AccuracyModifier > 0 ? new Vector4(0.2f, 1, 0.2f, 1) : new Vector4(1, 0.3f, 0.3f, 1),
                                    $"Accuracy: {effect.AccuracyModifier:+0.#;-0.#}");
                            if (effect.DamagePerTurn != 0)
                                ImGui.TextColored(new Vector4(1, 0.5f, 0.2f, 1), $"Damage/Turn: {effect.DamagePerTurn}");
                            ImGui.Unindent(10);
                        }

                        // Additional info
                        ImGui.Spacing();
                        ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1), $"Max Stacks: {effect.MaxStacks} | Cleansable: {(effect.Cleansable ? "Yes" : "No")}");

                        ImGui.TreePop();
                    }
                }
                ImGui.Unindent(10);
            }

            ImGui.Spacing();

            // Social/Political Systems
            ImGui.TextColored(new Vector4(0.5f, 0.8f, 1, 1), "Social & Political");

            // Factions
            var factions = viewModel.CurrentWorld.Gameplay?.Factions;
            if (factions != null && ImGui.CollapsingHeader($"Factions ({factions.Length})"))
            {
                ImGui.Indent(10);
                foreach (var faction in factions)
                {
                    var treeNodeOpen = ImGui.TreeNode(faction.DisplayName ?? faction.RefName);
                    if (treeNodeOpen)
                    {
                        if (!string.IsNullOrEmpty(faction.Description))
                        {
                            ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1), faction.Description);
                            ImGui.Spacing();
                        }

                        // Category
                        ImGui.Text($"Category: {faction.Category}");

                        // Starting reputation
                        ImGui.Text($"Starting Rep: {faction.StartingReputation}");

                        // Relationships with other factions
                        if (faction.Relationships != null && faction.Relationships.Length > 0)
                        {
                            ImGui.Spacing();
                            ImGui.TextColored(new Vector4(0.8f, 0.8f, 1, 1), "Relationships:");
                            ImGui.Indent(10);
                            foreach (var rel in faction.Relationships)
                            {
                                var relFaction = factions.FirstOrDefault(f => f.RefName == rel.FactionRef);
                                var relName = relFaction?.DisplayName ?? rel.FactionRef;
                                var relColor = rel.RelationshipType.ToString() switch
                                {
                                    "Allied" => new Vector4(0.2f, 1, 0.2f, 1),
                                    "Friendly" => new Vector4(0.5f, 0.9f, 0.5f, 1),
                                    "Rival" => new Vector4(1, 0.7f, 0.2f, 1),
                                    "Enemy" => new Vector4(1, 0.3f, 0.3f, 1),
                                    _ => new Vector4(0.7f, 0.7f, 0.7f, 1)
                                };
                                ImGui.TextColored(relColor, $"{relName}: {rel.RelationshipType} ({rel.SpilloverPercent:P0} spillover)");
                            }
                            ImGui.Unindent(10);
                        }

                        // Reputation rewards
                        if (faction.ReputationRewards != null && faction.ReputationRewards.Length > 0)
                        {
                            ImGui.Spacing();
                            ImGui.TextColored(new Vector4(1, 0.843f, 0, 1), "Reputation Rewards:");
                            ImGui.Indent(10);
                            foreach (var reward in faction.ReputationRewards)
                            {
                                var rewardItems = new List<string>();
                                if (reward.Equipment != null)
                                    foreach (var eq in reward.Equipment)
                                        rewardItems.Add($"Equipment: {eq.EquipmentRef}");
                                if (reward.Consumable != null)
                                    foreach (var c in reward.Consumable)
                                        rewardItems.Add($"Consumable: {c.ConsumableRef} x{c.Quantity}");
                                if (reward.QuestToken != null)
                                    foreach (var qt in reward.QuestToken)
                                        rewardItems.Add($"Token: {qt.QuestTokenRef}");

                                var rewardText = rewardItems.Count > 0 ? string.Join(", ", rewardItems) : "Unlocks rewards";
                                ImGui.Text($"At {reward.RequiredLevel}: {rewardText}");
                            }
                            ImGui.Unindent(10);
                        }

                        ImGui.TreePop();
                    }
                }
                ImGui.Unindent(10);
            }

            ImGui.Spacing();

            // Progression Systems
            ImGui.TextColored(new Vector4(1, 0.843f, 0, 1), "Progression");

            // Quest Tokens (currency/collectibles for quests)
            var questTokens = viewModel.CurrentWorld.Gameplay?.QuestTokens;
            if (questTokens != null && ImGui.CollapsingHeader($"Quest Tokens ({questTokens.Length})"))
            {
                ImGui.Indent(10);
                foreach (var token in questTokens)
                {
                    var treeNodeOpen = ImGui.TreeNode(token.DisplayName ?? token.RefName);
                    if (treeNodeOpen)
                    {
                        if (!string.IsNullOrEmpty(token.Description))
                        {
                            ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1), token.Description);
                        }
                        ImGui.TreePop();
                    }
                }
                ImGui.Unindent(10);
            }

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

        }
    }
}
