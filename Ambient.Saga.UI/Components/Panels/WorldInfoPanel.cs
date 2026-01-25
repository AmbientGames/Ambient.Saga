using Ambient.Saga.Presentation.UI.ViewModels;
using ImGuiNET;
using System.Numerics;
using Ambient.Saga.UI.Components.Utilities;

namespace Ambient.Saga.UI.Components.Panels;

/// <summary>
/// Full-screen panel showing world catalog with all gameplay elements.
/// Organized into three columns for better navigation.
/// Accessible via F1 key (debug mode only).
/// </summary>
public class WorldInfoPanel
{
    private string _searchFilter = "";

    public void Render(SagaMainViewModel viewModel)
    {
        // Header with world name
        var worldName = viewModel.CurrentWorld?.WorldConfiguration?.DisplayName ?? "Unknown World";
        ImGui.TextColored(new Vector4(1, 0.843f, 0, 1), $"WORLD CATALOG - {worldName}");
        ImGui.Separator();

        // Search filter
        ImGui.SetNextItemWidth(300);
        ImGui.InputTextWithHint("##WorldSearch", "Search catalog...", ref _searchFilter, 100);
        ImGui.SameLine();
        if (ImGui.SmallButton("Clear"))
        {
            _searchFilter = "";
        }

        // Height map info (compact)
        if (!string.IsNullOrEmpty(viewModel.HeightMapInfo))
        {
            ImGui.SameLine();
            ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1), $"| {viewModel.HeightMapInfo}");
        }

        ImGui.Spacing();
        ImGui.Separator();

        // World Catalog in columns
        if (viewModel.CurrentWorld != null)
        {
            // Calculate column widths for 3-column layout
            var availableWidth = ImGui.GetContentRegionAvail().X;
            var availableHeight = ImGui.GetContentRegionAvail().Y;
            var spacing = ImGui.GetStyle().ItemSpacing.X;
            var columnWidth = (availableWidth - spacing * 2) / 3;

            // Column 1: Items (Equipment, Consumables, Spells, Tools, Materials, Blocks)
            ImGui.BeginChild("WorldCol1", new Vector2(columnWidth, availableHeight), ImGuiChildFlags.None);
            RenderItemsColumn(viewModel);
            ImGui.EndChild();

            ImGui.SameLine();

            // Column 2: Characters & Combat
            ImGui.BeginChild("WorldCol2", new Vector2(columnWidth, availableHeight), ImGuiChildFlags.None);
            RenderCombatColumn(viewModel);
            ImGui.EndChild();

            ImGui.SameLine();

            // Column 3: World Systems
            ImGui.BeginChild("WorldCol3", new Vector2(columnWidth, availableHeight), ImGuiChildFlags.None);
            RenderSystemsColumn(viewModel);
            ImGui.EndChild();
        }
        else
        {
            ImGui.TextColored(new Vector4(1, 0.5f, 0, 1), "No world loaded");
            ImGui.Text("Load a world to browse its catalog.");
        }
    }

    private bool MatchesFilter(string? text)
    {
        if (string.IsNullOrEmpty(_searchFilter)) return true;
        return text?.Contains(_searchFilter, StringComparison.OrdinalIgnoreCase) ?? false;
    }

    #region Column 1: Items & Equipment

    private void RenderItemsColumn(SagaMainViewModel viewModel)
    {
        ImGui.TextColored(new Vector4(0.5f, 1, 0.5f, 1), "ITEMS & EQUIPMENT");
        ImGui.Separator();
        ImGui.Spacing();

        var gameplay = viewModel.CurrentWorld!.Gameplay;

        // Equipment
        var equipment = gameplay?.Equipment;
        if (equipment != null)
        {
            var filtered = equipment.Where(e => MatchesFilter(e.DisplayName) || MatchesFilter(e.RefName)).ToArray();
            if (filtered.Length > 0 && ImGui.CollapsingHeader($"Equipment ({filtered.Length})"))
            {
                foreach (var item in filtered)
                {
                    if (ImGui.TreeNode($"{item.DisplayName} [{item.SlotRef}]##{item.RefName}"))
                    {
                        if (!string.IsNullOrEmpty(item.Description))
                            ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1), item.Description);
                        ImGui.Text($"Category: {item.Category} | Rarity: {item.Rarity}");
                        ImGui.Text($"Price: {item.WholesalePrice} (x{item.MerchantMarkupMultiplier} markup)");
                        if (item.Effects != null)
                            ImGuiHelpers.RenderAttributes(item.Effects);
                        if (item.StatusEffectRef != null)
                            ImGui.TextColored(new Vector4(1, 0.5f, 0.5f, 1), $"Applies: {item.StatusEffectRef} ({item.StatusEffectChance:P0})");
                        ImGui.TreePop();
                    }
                }
            }
        }

        // Consumables
        var consumables = gameplay?.Consumables;
        if (consumables != null)
        {
            var filtered = consumables.Where(c => MatchesFilter(c.DisplayName) || MatchesFilter(c.RefName)).ToArray();
            if (filtered.Length > 0 && ImGui.CollapsingHeader($"Consumables ({filtered.Length})"))
            {
                foreach (var item in filtered)
                {
                    if (ImGui.TreeNode($"{item.DisplayName}##{item.RefName}"))
                    {
                        if (!string.IsNullOrEmpty(item.Description))
                            ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1), item.Description);
                        ImGui.Text($"Rarity: {item.Rarity} | Price: {item.WholesalePrice}");
                        if (item.Effects != null)
                            ImGuiHelpers.RenderAttributes(item.Effects);
                        if (item.CleansesStatusEffects)
                            ImGui.TextColored(new Vector4(0.5f, 1, 0.5f, 1), "Cleanses status effects");
                        ImGui.TreePop();
                    }
                }
            }
        }

        // Spells
        var spells = gameplay?.Spells;
        if (spells != null)
        {
            var filtered = spells.Where(s => MatchesFilter(s.DisplayName) || MatchesFilter(s.RefName)).ToArray();
            if (filtered.Length > 0 && ImGui.CollapsingHeader($"Spells ({filtered.Length})"))
            {
                foreach (var item in filtered)
                {
                    if (ImGui.TreeNode($"{item.DisplayName} [{item.Category}]##{item.RefName}"))
                    {
                        if (!string.IsNullOrEmpty(item.Description))
                            ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1), item.Description);
                        ImGui.Text($"Rarity: {item.Rarity} | Price: {item.WholesalePrice}");
                        if (item.RequiresEquipped != null)
                            ImGui.TextColored(new Vector4(1, 0.8f, 0.5f, 1), $"Requires: {item.RequiresEquipped}");
                        if (item.Effects != null)
                            ImGuiHelpers.RenderAttributes(item.Effects);
                        ImGui.TreePop();
                    }
                }
            }
        }

        // Tools
        var tools = gameplay?.Tools;
        if (tools != null)
        {
            var filtered = tools.Where(t => MatchesFilter(t.DisplayName) || MatchesFilter(t.RefName)).ToArray();
            if (filtered.Length > 0 && ImGui.CollapsingHeader($"Tools ({filtered.Length})"))
            {
                foreach (var item in filtered)
                {
                    if (ImGui.TreeNode($"{item.DisplayName}##{item.RefName}"))
                    {
                        if (!string.IsNullOrEmpty(item.Description))
                            ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1), item.Description);
                        ImGui.Text($"Rarity: {item.Rarity} | Price: {item.WholesalePrice}");
                        ImGui.Text($"Durability Loss: {item.DurabilityLoss:P2} per use");
                        if (item.EffectiveSubstances?.Length > 0)
                        {
                            ImGui.TextColored(new Vector4(0.5f, 0.8f, 1, 1), "Effective against:");
                            foreach (var eff in item.EffectiveSubstances)
                                ImGui.BulletText($"{eff.SubstanceRef} ({eff.EffectivenessMultiplier:P0})");
                        }
                        ImGui.TreePop();
                    }
                }
            }
        }

        // Building Materials
        var materials = gameplay?.BuildingMaterials;
        if (materials != null)
        {
            var filtered = materials.Where(m => MatchesFilter(m.DisplayName) || MatchesFilter(m.RefName)).ToArray();
            if (filtered.Length > 0 && ImGui.CollapsingHeader($"Materials ({filtered.Length})"))
            {
                foreach (var item in filtered)
                {
                    if (ImGui.TreeNode($"{item.DisplayName}##{item.RefName}"))
                    {
                        if (!string.IsNullOrEmpty(item.Description))
                            ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1), item.Description);
                        ImGui.Text($"Rarity: {item.Rarity} | Price: {item.WholesalePrice}");
                        ImGui.TreePop();
                    }
                }
            }
        }

        // Blocks
        var blocks = viewModel.CurrentWorld!.BlockProvider?.GetAllBlocks().ToList();
        if (blocks != null && blocks.Count > 0)
        {
            var filtered = blocks.Where(b => MatchesFilter(b.DisplayName) || MatchesFilter(b.RefName)).ToList();
            if (filtered.Count > 0 && ImGui.CollapsingHeader($"Blocks ({filtered.Count})"))
            {
                var grouped = filtered.GroupBy(b => b.SubstanceRef ?? "Other").OrderBy(g => g.Key);
                foreach (var group in grouped)
                {
                    if (ImGui.TreeNode($"{group.Key} ({group.Count()})"))
                    {
                        foreach (var block in group)
                        {
                            if (ImGui.TreeNode($"{block.DisplayName}##{block.RefName}"))
                            {
                                if (!string.IsNullOrEmpty(block.Description))
                                    ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1), block.Description);
                                ImGui.Text($"Price: {block.WholesalePrice} (x{block.MerchantMarkupMultiplier} markup)");
                                ImGui.TreePop();
                            }
                        }
                        ImGui.TreePop();
                    }
                }
            }
        }
    }

    #endregion

    #region Column 2: Characters & Combat

    private void RenderCombatColumn(SagaMainViewModel viewModel)
    {
        ImGui.TextColored(new Vector4(1, 0.7f, 0.7f, 1), "CHARACTERS & COMBAT");
        ImGui.Separator();
        ImGui.Spacing();

        var gameplay = viewModel.CurrentWorld!.Gameplay;

        // Characters
        var characters = gameplay?.Characters;
        if (characters != null)
        {
            var filtered = characters.Where(c => MatchesFilter(c.DisplayName) || MatchesFilter(c.RefName)).ToArray();
            if (filtered.Length > 0 && ImGui.CollapsingHeader($"Characters ({filtered.Length})"))
            {
                foreach (var character in filtered)
                {
                    if (ImGui.TreeNode($"{character.DisplayName}##{character.RefName}"))
                    {
                        if (!string.IsNullOrEmpty(character.Description))
                            ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1), character.Description);
                        if (character.Stats != null)
                        {
                            ImGui.Text($"HP: {character.Stats.Health:F0} | STR: {character.Stats.Strength:F0} | DEF: {character.Stats.Defense:F0}");
                            ImGui.Text($"SPD: {character.Stats.Speed:F0} | MAG: {character.Stats.Magic:F0}");
                        }
                        if (character.Traits?.Length > 0)
                        {
                            ImGui.TextColored(new Vector4(1, 0.8f, 0.5f, 1), "Traits:");
                            foreach (var trait in character.Traits)
                                ImGui.BulletText($"{trait.Name}: {trait.Value}");
                        }
                        if (character.Interactable?.DialogueTreeRef != null)
                            ImGui.TextColored(new Vector4(0.5f, 0.8f, 1, 1), $"Dialogue: {character.Interactable.DialogueTreeRef}");
                        ImGui.TreePop();
                    }
                }
            }
        }

        // Archetypes
        var archetypes = gameplay?.AvatarArchetypes;
        if (archetypes != null)
        {
            var filtered = archetypes.Where(a => MatchesFilter(a.DisplayName) || MatchesFilter(a.RefName)).ToArray();
            if (filtered.Length > 0 && ImGui.CollapsingHeader($"Archetypes ({filtered.Length})"))
            {
                foreach (var archetype in filtered)
                {
                    if (ImGui.TreeNode($"{archetype.DisplayName}##{archetype.RefName}"))
                    {
                        if (!string.IsNullOrEmpty(archetype.Description))
                            ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1), archetype.Description);
                        var affinityDef = viewModel.CurrentWorld?.TryGetCharacterAffinityByRefName(archetype.AffinityRef ?? "");
                        ImGui.Text($"Affinity: {affinityDef?.DisplayName ?? archetype.AffinityRef ?? "None"}");
                        ImGui.TreePop();
                    }
                }
            }
        }

        // Affinities
        var affinities = gameplay?.CharacterAffinities;
        if (affinities != null)
        {
            var filtered = affinities.Where(a => MatchesFilter(a.DisplayName) || MatchesFilter(a.RefName)).ToArray();
            if (filtered.Length > 0 && ImGui.CollapsingHeader($"Affinities ({filtered.Length})"))
            {
                foreach (var affinity in filtered)
                {
                    if (ImGui.TreeNode($"{affinity.DisplayName}##{affinity.RefName}"))
                    {
                        if (!string.IsNullOrEmpty(affinity.Description))
                            ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1), affinity.Description);
                        ImGui.Text($"Neutral: {affinity.NeutralMultiplier}x");
                        if (affinity.Matchup?.Length > 0)
                        {
                            ImGui.TextColored(new Vector4(0.5f, 0.8f, 1, 1), "Matchups:");
                            foreach (var m in affinity.Matchup)
                            {
                                var color = m.Multiplier > 1 ? new Vector4(0.5f, 1, 0.5f, 1) : new Vector4(1, 0.5f, 0.5f, 1);
                                ImGui.TextColored(color, $"  vs {m.TargetAffinityRef}: {m.Multiplier}x");
                            }
                        }
                        ImGui.TreePop();
                    }
                }
            }
        }

        // Combat Stances
        var stances = gameplay?.CombatStances;
        if (stances != null)
        {
            var filtered = stances.Where(s => MatchesFilter(s.DisplayName) || MatchesFilter(s.RefName)).ToArray();
            if (filtered.Length > 0 && ImGui.CollapsingHeader($"Combat Stances ({filtered.Length})"))
            {
                foreach (var stance in filtered)
                {
                    if (ImGui.TreeNode($"{stance.DisplayName}##{stance.RefName}"))
                    {
                        if (!string.IsNullOrEmpty(stance.Description))
                            ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1), stance.Description);
                        if (stance.Effects != null)
                            ImGuiHelpers.RenderAttributes(stance.Effects);
                        ImGui.TreePop();
                    }
                }
            }
        }

        // Status Effects
        var effects = gameplay?.StatusEffects;
        if (effects != null)
        {
            var filtered = effects.Where(e => MatchesFilter(e.DisplayName) || MatchesFilter(e.RefName)).ToArray();
            if (filtered.Length > 0 && ImGui.CollapsingHeader($"Status Effects ({filtered.Length})"))
            {
                foreach (var effect in filtered)
                {
                    var catColor = effect.Category switch
                    {
                        Ambient.Domain.StatusEffectCategory.Buff => new Vector4(0.5f, 1, 0.5f, 1),
                        Ambient.Domain.StatusEffectCategory.Debuff => new Vector4(1, 0.5f, 0.5f, 1),
                        _ => new Vector4(0.7f, 0.7f, 0.7f, 1)
                    };
                    if (ImGui.TreeNode($"{effect.DisplayName} [{effect.Category}]##{effect.RefName}"))
                    {
                        if (!string.IsNullOrEmpty(effect.Description))
                            ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1), effect.Description);
                        ImGui.Text($"Type: {effect.Type} | Duration: {effect.DurationTurns} turns");
                        ImGui.Text($"Max Stacks: {effect.MaxStacks} | Cleansable: {(effect.Cleansable ? "Yes" : "No")}");
                        if (effect.DamagePerTurn != 0)
                            ImGui.TextColored(new Vector4(1, 0.5f, 0.5f, 1), $"Damage/Turn: {effect.DamagePerTurn}");
                        if (effect.StrengthModifier != 0) ImGui.Text($"STR: {effect.StrengthModifier:+0;-0}");
                        if (effect.DefenseModifier != 0) ImGui.Text($"DEF: {effect.DefenseModifier:+0;-0}");
                        if (effect.SpeedModifier != 0) ImGui.Text($"SPD: {effect.SpeedModifier:+0;-0}");
                        if (effect.MagicModifier != 0) ImGui.Text($"MAG: {effect.MagicModifier:+0;-0}");
                        ImGui.TreePop();
                    }
                }
            }
        }

        // Loadout Slots
        var slots = gameplay?.LoadoutSlots;
        if (slots != null)
        {
            var filtered = slots.Where(s => MatchesFilter(s.DisplayName) || MatchesFilter(s.RefName)).ToArray();
            if (filtered.Length > 0 && ImGui.CollapsingHeader($"Loadout Slots ({filtered.Length})"))
            {
                foreach (var slot in filtered)
                {
                    if (ImGui.TreeNode($"{slot.DisplayName}##{slot.RefName}"))
                    {
                        if (!string.IsNullOrEmpty(slot.Description))
                            ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1), slot.Description);
                        ImGui.TreePop();
                    }
                }
            }
        }
    }

    #endregion

    #region Column 3: World Systems

    private void RenderSystemsColumn(SagaMainViewModel viewModel)
    {
        ImGui.TextColored(new Vector4(0.5f, 0.8f, 1, 1), "WORLD SYSTEMS");
        ImGui.Separator();
        ImGui.Spacing();

        var gameplay = viewModel.CurrentWorld!.Gameplay;

        // Quests
        var quests = gameplay?.Quests;
        if (quests != null)
        {
            var filtered = quests.Where(q => MatchesFilter(q.DisplayName) || MatchesFilter(q.RefName)).ToArray();
            if (filtered.Length > 0 && ImGui.CollapsingHeader($"Quests ({filtered.Length})"))
            {
                foreach (var quest in filtered)
                {
                    if (ImGui.TreeNode($"{quest.DisplayName}##{quest.RefName}"))
                    {
                        if (!string.IsNullOrEmpty(quest.Description))
                            ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1), quest.Description);
                        var stageCount = quest.Stages?.Stage?.Length ?? 0;
                        ImGui.Text($"Stages: {stageCount}");
                        if (quest.Prerequisites?.Length > 0)
                        {
                            ImGui.TextColored(new Vector4(1, 0.8f, 0.5f, 1), "Prerequisites:");
                            foreach (var prereq in quest.Prerequisites)
                            {
                                if (prereq.QuestRef != null)
                                {
                                    var pq = viewModel.CurrentWorld?.TryGetQuestByRefName(prereq.QuestRef);
                                    ImGui.BulletText($"Quest: {pq?.DisplayName ?? prereq.QuestRef}");
                                }
                                if (prereq.MinimumLevel > 0)
                                    ImGui.BulletText($"Level: {prereq.MinimumLevel}");
                            }
                        }
                        if (quest.Rewards?.Length > 0)
                            ImGui.TextColored(new Vector4(0.5f, 1, 0.5f, 1), $"Rewards: {quest.Rewards.Length} entries");
                        ImGui.TreePop();
                    }
                }
            }
        }

        // Factions
        var factions = gameplay?.Factions;
        if (factions != null)
        {
            var filtered = factions.Where(f => MatchesFilter(f.DisplayName) || MatchesFilter(f.RefName)).ToArray();
            if (filtered.Length > 0 && ImGui.CollapsingHeader($"Factions ({filtered.Length})"))
            {
                foreach (var faction in filtered)
                {
                    if (ImGui.TreeNode($"{faction.DisplayName} [{faction.Category}]##{faction.RefName}"))
                    {
                        if (!string.IsNullOrEmpty(faction.Description))
                            ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1), faction.Description);
                        ImGui.Text($"Starting Rep: {faction.StartingReputation}");
                        if (faction.Relationships?.Length > 0)
                        {
                            ImGui.TextColored(new Vector4(0.5f, 0.8f, 1, 1), "Relationships:");
                            foreach (var rel in faction.Relationships)
                            {
                                var rf = factions.FirstOrDefault(f => f.RefName == rel.FactionRef);
                                var color = rel.RelationshipType == Ambient.Domain.FactionRelationshipRelationshipType.Allied
                                    ? new Vector4(0.5f, 1, 0.5f, 1) : new Vector4(1, 0.5f, 0.5f, 1);
                                ImGui.TextColored(color, $"  {rf?.DisplayName ?? rel.FactionRef}: {rel.RelationshipType}");
                            }
                        }
                        if (faction.ReputationRewards?.Length > 0)
                            ImGui.TextColored(new Vector4(1, 0.843f, 0, 1), $"Rewards: {faction.ReputationRewards.Length} tiers");
                        ImGui.TreePop();
                    }
                }
            }
        }

        // Dialogue Trees
        var dialogues = gameplay?.DialogueTrees;
        if (dialogues != null)
        {
            var filtered = dialogues.Where(d => MatchesFilter(d.DisplayName) || MatchesFilter(d.RefName)).ToArray();
            if (filtered.Length > 0 && ImGui.CollapsingHeader($"Dialogue Trees ({filtered.Length})"))
            {
                foreach (var tree in filtered)
                {
                    var nodeCount = tree.Node?.Length ?? 0;
                    if (ImGui.TreeNode($"{tree.DisplayName} ({nodeCount} nodes)##{tree.RefName}"))
                    {
                        if (!string.IsNullOrEmpty(tree.Description))
                            ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1), tree.Description);
                        ImGui.Text($"Start: {tree.StartNodeId}");
                        ImGui.TreePop();
                    }
                }
            }
        }

        // Quest Tokens
        var tokens = gameplay?.QuestTokens;
        if (tokens != null)
        {
            var filtered = tokens.Where(t => MatchesFilter(t.DisplayName) || MatchesFilter(t.RefName)).ToArray();
            if (filtered.Length > 0 && ImGui.CollapsingHeader($"Quest Tokens ({filtered.Length})"))
            {
                foreach (var token in filtered)
                {
                    if (ImGui.TreeNode($"{token.DisplayName}##{token.RefName}"))
                    {
                        if (!string.IsNullOrEmpty(token.Description))
                            ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1), token.Description);
                        ImGui.TreePop();
                    }
                }
            }
        }

        // Achievements
        var achievements = gameplay?.Achievements;
        if (achievements != null)
        {
            var filtered = achievements.Where(a => MatchesFilter(a.DisplayName) || MatchesFilter(a.RefName)).ToArray();
            if (filtered.Length > 0 && ImGui.CollapsingHeader($"Achievements ({filtered.Length})"))
            {
                foreach (var ach in filtered)
                {
                    if (ImGui.TreeNode($"{ach.DisplayName}##{ach.RefName}"))
                    {
                        if (!string.IsNullOrEmpty(ach.Description))
                            ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1), ach.Description);
                        if (ach.Criteria != null)
                        {
                            ImGui.TextColored(new Vector4(0.6f, 0.8f, 1, 1), $"Criteria: {ach.Criteria.Type}");
                            ImGui.Text($"Threshold: {ach.Criteria.Threshold:F0}");
                        }
                        ImGui.TreePop();
                    }
                }
            }
        }
    }

    #endregion
}
