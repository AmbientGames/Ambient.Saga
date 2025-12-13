using Ambient.Domain;
using Ambient.Domain.Contracts;
using Ambient.Saga.Presentation.UI.ViewModels;
using ImGuiNET;
using System.Numerics;
using Ambient.Saga.UI.Components.Utilities;

namespace Ambient.Saga.UI.Components.Modals;

/// <summary>
/// Modal showing complete world catalog with all gameplay elements
/// </summary>
public class WorldCatalogModal
{
    private string _searchFilter = "";
    private int _selectedCategory = 0;
    private readonly string[] _categories = new[]
    {
        "Equipment", "Consumables", "Spells", "Tools", "Building Materials", "Blocks",
        "Characters", "Character Archetypes", "Quests", "Factions",
        "Dialogue Trees", "Status Effects", "Combat Stances", "Affinities"
    };

    public void Render(MainViewModel viewModel, ref bool isOpen)
    {
        if (!isOpen) return;

        ImGui.SetNextWindowSize(new Vector2(900, 700), ImGuiCond.FirstUseEver);
        if (ImGui.Begin("World Catalog", ref isOpen))
        {
            if (viewModel.CurrentWorld?.Gameplay == null)
            {
                ImGui.TextColored(new Vector4(1, 0.5f, 0, 1), "No world loaded");
                ImGui.Text("Load a world to browse its catalog.");
                ImGui.End();
                return;
            }

            var gameplay = viewModel.CurrentWorld.Gameplay;

            // Header
            ImGui.TextColored(new Vector4(1, 0.843f, 0, 1), "WORLD CATALOG");
            ImGui.SameLine();
            ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1), $"- {viewModel.CurrentWorld.WorldConfiguration?.DisplayName ?? "Unknown World"}");
            ImGui.Separator();

            // Search and category selection
            ImGui.SetNextItemWidth(300);
            ImGui.InputTextWithHint("##Search", "Search items...", ref _searchFilter, 100);
            ImGui.SameLine();
            ImGui.SetNextItemWidth(200);
            ImGui.Combo("Category", ref _selectedCategory, _categories, _categories.Length);

            ImGui.Spacing();
            ImGui.Separator();

            // Content area
            ImGui.BeginChild("CatalogContent", new Vector2(0, 0), ImGuiChildFlags.Borders);

            switch (_selectedCategory)
            {
                case 0: RenderEquipmentCatalog(gameplay); break;
                case 1: RenderConsumablesCatalog(gameplay); break;
                case 2: RenderSpellsCatalog(gameplay); break;
                case 3: RenderToolsCatalog(gameplay); break;
                case 4: RenderBuildingMaterialsCatalog(gameplay); break;
                case 5: RenderBlocksCatalog(viewModel.CurrentWorld); break;
                case 6: RenderCharactersCatalog(gameplay); break;
                case 7: RenderCharacterArchetypesCatalog(gameplay); break;
                case 8: RenderQuestsCatalog(gameplay); break;
                case 9: RenderFactionsCatalog(gameplay); break;
                case 10: RenderDialogueTreesCatalog(gameplay); break;
                case 11: RenderStatusEffectsCatalog(gameplay); break;
                case 12: RenderCombatStancesCatalog(gameplay); break;
                case 13: RenderAffinitiesCatalog(gameplay); break;
            }

            ImGui.EndChild();
            ImGui.End();
        }
    }

    private bool MatchesFilter(string? text)
    {
        if (string.IsNullOrEmpty(_searchFilter)) return true;
        return text?.Contains(_searchFilter, StringComparison.OrdinalIgnoreCase) ?? false;
    }

    private void RenderEquipmentCatalog(GameplayComponents gameplay)
    {
        if (gameplay.Equipment == null || gameplay.Equipment.Length == 0)
        {
            ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1), "No equipment defined");
            return;
        }

        ImGui.TextColored(new Vector4(0.5f, 1, 0.5f, 1), $"Equipment ({gameplay.Equipment.Length} items)");
        ImGui.Spacing();

        foreach (var item in gameplay.Equipment)
        {
            if (!MatchesFilter(item.DisplayName) && !MatchesFilter(item.RefName)) continue;

            if (ImGui.CollapsingHeader($"{item.DisplayName} [{item.SlotRef}]##{item.RefName}"))
            {
                ImGui.Indent();
                ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1), item.Description ?? "No description");
                ImGui.Text($"Category: {item.Category}");
                ImGui.Text($"Rarity: {item.Rarity}");
                ImGui.Text($"Price: {item.WholesalePrice} (x{item.MerchantMarkupMultiplier} markup)");
                if (item.Effects != null)
                {
                    ImGui.Spacing();
                    ImGuiHelpers.RenderCharacterEffects(item.Effects);
                }
                if (item.StatusEffectRef != null)
                {
                    ImGui.TextColored(new Vector4(1, 0.5f, 0.5f, 1), $"Applies: {item.StatusEffectRef} ({item.StatusEffectChance:P0} chance)");
                }
                ImGui.Unindent();
            }
        }
    }

    private void RenderConsumablesCatalog(GameplayComponents gameplay)
    {
        if (gameplay.Consumables == null || gameplay.Consumables.Length == 0)
        {
            ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1), "No consumables defined");
            return;
        }

        ImGui.TextColored(new Vector4(0.5f, 1, 0.5f, 1), $"Consumables ({gameplay.Consumables.Length} items)");
        ImGui.Spacing();

        foreach (var item in gameplay.Consumables)
        {
            if (!MatchesFilter(item.DisplayName) && !MatchesFilter(item.RefName)) continue;

            if (ImGui.CollapsingHeader($"{item.DisplayName}##{item.RefName}"))
            {
                ImGui.Indent();
                ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1), item.Description ?? "No description");
                ImGui.Text($"Rarity: {item.Rarity}");
                ImGui.Text($"Price: {item.WholesalePrice} (x{item.MerchantMarkupMultiplier} markup)");
                if (item.Effects != null)
                {
                    ImGui.Spacing();
                    ImGuiHelpers.RenderCharacterEffects(item.Effects);
                }
                if (item.CleansesStatusEffects)
                {
                    ImGui.TextColored(new Vector4(0.5f, 1, 0.5f, 1), "Cleanses status effects");
                }
                ImGui.Unindent();
            }
        }
    }

    private void RenderSpellsCatalog(GameplayComponents gameplay)
    {
        if (gameplay.Spells == null || gameplay.Spells.Length == 0)
        {
            ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1), "No spells defined");
            return;
        }

        ImGui.TextColored(new Vector4(0.5f, 1, 0.5f, 1), $"Spells ({gameplay.Spells.Length} items)");
        ImGui.Spacing();

        foreach (var item in gameplay.Spells)
        {
            if (!MatchesFilter(item.DisplayName) && !MatchesFilter(item.RefName)) continue;

            if (ImGui.CollapsingHeader($"{item.DisplayName} [{item.Category}]##{item.RefName}"))
            {
                ImGui.Indent();
                ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1), item.Description ?? "No description");
                ImGui.Text($"Category: {item.Category}");
                ImGui.Text($"Rarity: {item.Rarity}");
                ImGui.Text($"Price: {item.WholesalePrice}");
                if (item.RequiresEquipped != null)
                {
                    ImGui.TextColored(new Vector4(1, 0.8f, 0.5f, 1), $"Requires: {item.RequiresEquipped} equipped");
                }
                if (item.Effects != null)
                {
                    ImGui.Spacing();
                    ImGuiHelpers.RenderCharacterEffects(item.Effects);
                }
                ImGui.Unindent();
            }
        }
    }

    private void RenderToolsCatalog(GameplayComponents gameplay)
    {
        if (gameplay.Tools == null || gameplay.Tools.Length == 0)
        {
            ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1), "No tools defined");
            return;
        }

        ImGui.TextColored(new Vector4(0.5f, 1, 0.5f, 1), $"Tools ({gameplay.Tools.Length} items)");
        ImGui.Spacing();

        foreach (var item in gameplay.Tools)
        {
            if (!MatchesFilter(item.DisplayName) && !MatchesFilter(item.RefName)) continue;

            if (ImGui.CollapsingHeader($"{item.DisplayName}##{item.RefName}"))
            {
                ImGui.Indent();
                ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1), item.Description ?? "No description");
                ImGui.Text($"Rarity: {item.Rarity}");
                ImGui.Text($"Price: {item.WholesalePrice}");
                ImGui.Text($"Durability Loss: {item.DurabilityLoss:P1} per use");
                if (item.EffectiveSubstances?.Length > 0)
                {
                    ImGui.TextColored(new Vector4(0.5f, 0.8f, 1, 1), "Effective against:");
                    foreach (var eff in item.EffectiveSubstances)
                    {
                        ImGui.BulletText($"{eff.SubstanceRef} ({eff.EffectivenessMultiplier:P0})");
                    }
                }
                ImGui.Unindent();
            }
        }
    }

    private void RenderBuildingMaterialsCatalog(GameplayComponents gameplay)
    {
        if (gameplay.BuildingMaterials == null || gameplay.BuildingMaterials.Length == 0)
        {
            ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1), "No building materials defined");
            return;
        }

        ImGui.TextColored(new Vector4(0.5f, 1, 0.5f, 1), $"Building Materials ({gameplay.BuildingMaterials.Length} items)");
        ImGui.Spacing();

        foreach (var item in gameplay.BuildingMaterials)
        {
            if (!MatchesFilter(item.DisplayName) && !MatchesFilter(item.RefName)) continue;

            if (ImGui.CollapsingHeader($"{item.DisplayName}##{item.RefName}"))
            {
                ImGui.Indent();
                ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1), item.Description ?? "No description");
                ImGui.Text($"Rarity: {item.Rarity}");
                ImGui.Text($"Price: {item.WholesalePrice} (x{item.MerchantMarkupMultiplier} markup)");
                ImGui.Unindent();
            }
        }
    }

    private void RenderBlocksCatalog(IWorld world)
    {
        if (world.BlockProvider == null)
        {
            ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1), "No block provider available");
            return;
        }

        var blocks = world.BlockProvider.GetAllBlocks().ToList();
        if (blocks.Count == 0)
        {
            ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1), "No blocks defined");
            return;
        }

        ImGui.TextColored(new Vector4(0.5f, 1, 0.5f, 1), $"Blocks ({blocks.Count} types)");
        ImGui.Spacing();

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
                    if (!MatchesFilter(block.DisplayName) && !MatchesFilter(block.RefName)) continue;

                    if (ImGui.CollapsingHeader($"{block.DisplayName}##{block.RefName}"))
                    {
                        ImGui.Indent();
                        ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1), block.Description ?? "No description");
                        ImGui.Text($"Substance: {block.SubstanceRef ?? "None"}");
                        ImGui.Text($"Price: {block.WholesalePrice} (x{block.MerchantMarkupMultiplier} markup)");
                        if (!string.IsNullOrEmpty(block.TextureRef))
                        {
                            ImGui.Text($"Texture: {block.TextureRef}");
                        }
                        ImGui.Unindent();
                    }
                }
                ImGui.TreePop();
            }
        }
    }

    private void RenderCharactersCatalog(GameplayComponents gameplay)
    {
        if (gameplay.Characters == null || gameplay.Characters.Length == 0)
        {
            ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1), "No characters defined");
            return;
        }

        ImGui.TextColored(new Vector4(0.5f, 1, 0.5f, 1), $"Characters ({gameplay.Characters.Length} NPCs)");
        ImGui.Spacing();

        foreach (var character in gameplay.Characters)
        {
            if (!MatchesFilter(character.DisplayName) && !MatchesFilter(character.RefName)) continue;

            if (ImGui.CollapsingHeader($"{character.DisplayName}##{character.RefName}"))
            {
                ImGui.Indent();
                ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1), character.Description ?? "No description");

                if (character.Stats != null)
                {
                    ImGui.Text($"Health: {character.Stats.Health:F0} | Strength: {character.Stats.Strength:F0} | Defense: {character.Stats.Defense:F0}");
                    ImGui.Text($"Speed: {character.Stats.Speed:F0} | Magic: {character.Stats.Magic:F0}");
                }

                if (character.Traits?.Length > 0)
                {
                    ImGui.TextColored(new Vector4(1, 0.8f, 0.5f, 1), "Traits:");
                    foreach (var trait in character.Traits)
                    {
                        ImGui.BulletText($"{trait.Name}: {trait.Value}");
                    }
                }

                if (character.Interactable?.DialogueTreeRef != null)
                {
                    ImGui.TextColored(new Vector4(0.5f, 0.8f, 1, 1), $"Dialogue: {character.Interactable.DialogueTreeRef}");
                }

                ImGui.Unindent();
            }
        }
    }

    private void RenderCharacterArchetypesCatalog(GameplayComponents gameplay)
    {
        if (gameplay.CharacterArchetypes == null || gameplay.CharacterArchetypes.Length == 0)
        {
            ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1), "No character archetypes defined");
            return;
        }

        ImGui.TextColored(new Vector4(0.5f, 1, 0.5f, 1), $"Character Archetypes ({gameplay.CharacterArchetypes.Length} types)");
        ImGui.Spacing();

        foreach (var archetype in gameplay.CharacterArchetypes)
        {
            if (!MatchesFilter(archetype.DisplayName) && !MatchesFilter(archetype.RefName)) continue;

            if (ImGui.CollapsingHeader($"{archetype.DisplayName}##{archetype.RefName}"))
            {
                ImGui.Indent();
                ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1), archetype.Description ?? "No description");
                if (archetype.CharacterRef?.Length > 0)
                {
                    ImGui.Text($"Contains {archetype.CharacterRef.Length} character variants");
                }
                ImGui.Unindent();
            }
        }
    }

    private void RenderQuestsCatalog(GameplayComponents gameplay)
    {
        if (gameplay.Quests == null || gameplay.Quests.Length == 0)
        {
            ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1), "No quests defined");
            return;
        }

        ImGui.TextColored(new Vector4(0.5f, 1, 0.5f, 1), $"Quests ({gameplay.Quests.Length} quests)");
        ImGui.Spacing();

        foreach (var quest in gameplay.Quests)
        {
            if (!MatchesFilter(quest.DisplayName) && !MatchesFilter(quest.RefName)) continue;

            if (ImGui.CollapsingHeader($"{quest.DisplayName}##{quest.RefName}"))
            {
                ImGui.Indent();
                ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1), quest.Description ?? "No description");

                var stageCount = quest.Stages?.Stage?.Length ?? 0;
                ImGui.Text($"Stages: {stageCount}");

                if (quest.Prerequisites?.Length > 0)
                {
                    ImGui.TextColored(new Vector4(1, 0.8f, 0.5f, 1), "Prerequisites:");
                    foreach (var prereq in quest.Prerequisites)
                    {
                        if (prereq.QuestRef != null)
                            ImGui.BulletText($"Quest: {prereq.QuestRef}");
                        if (prereq.MinimumLevel > 0)
                            ImGui.BulletText($"Level: {prereq.MinimumLevel}");
                    }
                }

                if (quest.Rewards?.Length > 0)
                {
                    ImGui.TextColored(new Vector4(0.5f, 1, 0.5f, 1), $"Rewards: {quest.Rewards.Length} reward entries");
                }

                ImGui.Unindent();
            }
        }
    }

    private void RenderFactionsCatalog(GameplayComponents gameplay)
    {
        if (gameplay.Factions == null || gameplay.Factions.Length == 0)
        {
            ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1), "No factions defined");
            return;
        }

        ImGui.TextColored(new Vector4(0.5f, 1, 0.5f, 1), $"Factions ({gameplay.Factions.Length} factions)");
        ImGui.Spacing();

        foreach (var faction in gameplay.Factions)
        {
            if (!MatchesFilter(faction.DisplayName) && !MatchesFilter(faction.RefName)) continue;

            if (ImGui.CollapsingHeader($"{faction.DisplayName} [{faction.Category}]##{faction.RefName}"))
            {
                ImGui.Indent();
                ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1), faction.Description ?? "No description");
                ImGui.Text($"Category: {faction.Category}");
                ImGui.Text($"Starting Reputation: {faction.StartingReputation}");

                if (faction.Relationships?.Length > 0)
                {
                    ImGui.TextColored(new Vector4(0.5f, 0.8f, 1, 1), "Relationships:");
                    foreach (var rel in faction.Relationships)
                    {
                        var color = rel.RelationshipType == FactionRelationshipRelationshipType.Allied
                            ? new Vector4(0.5f, 1, 0.5f, 1)
                            : new Vector4(1, 0.5f, 0.5f, 1);
                        ImGui.TextColored(color, $"  {rel.FactionRef}: {rel.RelationshipType} ({rel.SpilloverPercent:P0} spillover)");
                    }
                }

                if (faction.ReputationRewards?.Length > 0)
                {
                    ImGui.TextColored(new Vector4(1, 0.843f, 0, 1), $"Rewards: {faction.ReputationRewards.Length} reputation rewards");
                }

                ImGui.Unindent();
            }
        }
    }

    private void RenderDialogueTreesCatalog(GameplayComponents gameplay)
    {
        if (gameplay.DialogueTrees == null || gameplay.DialogueTrees.Length == 0)
        {
            ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1), "No dialogue trees defined");
            return;
        }

        ImGui.TextColored(new Vector4(0.5f, 1, 0.5f, 1), $"Dialogue Trees ({gameplay.DialogueTrees.Length} trees)");
        ImGui.Spacing();

        foreach (var tree in gameplay.DialogueTrees)
        {
            if (!MatchesFilter(tree.DisplayName) && !MatchesFilter(tree.RefName)) continue;

            var nodeCount = tree.Node?.Length ?? 0;
            if (ImGui.CollapsingHeader($"{tree.DisplayName} ({nodeCount} nodes)##{tree.RefName}"))
            {
                ImGui.Indent();
                ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1), tree.Description ?? "No description");
                ImGui.Text($"Start Node: {tree.StartNodeId}");
                ImGui.Text($"Total Nodes: {nodeCount}");
                ImGui.Unindent();
            }
        }
    }

    private void RenderStatusEffectsCatalog(GameplayComponents gameplay)
    {
        if (gameplay.StatusEffects == null || gameplay.StatusEffects.Length == 0)
        {
            ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1), "No status effects defined");
            return;
        }

        ImGui.TextColored(new Vector4(0.5f, 1, 0.5f, 1), $"Status Effects ({gameplay.StatusEffects.Length} effects)");
        ImGui.Spacing();

        foreach (var effect in gameplay.StatusEffects)
        {
            if (!MatchesFilter(effect.DisplayName) && !MatchesFilter(effect.RefName)) continue;

            var categoryColor = effect.Category switch
            {
                StatusEffectCategory.Buff => new Vector4(0.5f, 1, 0.5f, 1),
                StatusEffectCategory.Debuff => new Vector4(1, 0.5f, 0.5f, 1),
                _ => new Vector4(0.7f, 0.7f, 0.7f, 1)
            };

            if (ImGui.CollapsingHeader($"{effect.DisplayName} [{effect.Category}]##{effect.RefName}"))
            {
                ImGui.Indent();
                ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1), effect.Description ?? "No description");
                ImGui.Text($"Type: {effect.Type}");
                ImGui.Text($"Duration: {effect.DurationTurns} turns");
                ImGui.Text($"Max Stacks: {effect.MaxStacks}");
                ImGui.Text($"Application: {effect.ApplicationMethod}");

                if (effect.DamagePerTurn != 0)
                    ImGui.TextColored(new Vector4(1, 0.5f, 0.5f, 1), $"Damage/Turn: {effect.DamagePerTurn}");

                // Show stat modifiers
                if (effect.StrengthModifier != 0)
                    ImGui.Text($"Strength: {effect.StrengthModifier:+0;-0}");
                if (effect.DefenseModifier != 0)
                    ImGui.Text($"Defense: {effect.DefenseModifier:+0;-0}");
                if (effect.SpeedModifier != 0)
                    ImGui.Text($"Speed: {effect.SpeedModifier:+0;-0}");
                if (effect.MagicModifier != 0)
                    ImGui.Text($"Magic: {effect.MagicModifier:+0;-0}");

                ImGui.Text($"Cleansable: {(effect.Cleansable ? "Yes" : "No")}");
                ImGui.Unindent();
            }
        }
    }

    private void RenderCombatStancesCatalog(GameplayComponents gameplay)
    {
        if (gameplay.CombatStances == null || gameplay.CombatStances.Length == 0)
        {
            ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1), "No combat stances defined");
            return;
        }

        ImGui.TextColored(new Vector4(0.5f, 1, 0.5f, 1), $"Combat Stances ({gameplay.CombatStances.Length} stances)");
        ImGui.Spacing();

        foreach (var stance in gameplay.CombatStances)
        {
            if (!MatchesFilter(stance.DisplayName) && !MatchesFilter(stance.RefName)) continue;

            if (ImGui.CollapsingHeader($"{stance.DisplayName}##{stance.RefName}"))
            {
                ImGui.Indent();
                ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1), stance.Description ?? "No description");
                if (stance.Effects != null)
                {
                    ImGui.Spacing();
                    ImGuiHelpers.RenderCharacterEffects(stance.Effects);
                }
                ImGui.Unindent();
            }
        }
    }

    private void RenderAffinitiesCatalog(GameplayComponents gameplay)
    {
        if (gameplay.CharacterAffinities == null || gameplay.CharacterAffinities.Length == 0)
        {
            ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1), "No affinities defined");
            return;
        }

        ImGui.TextColored(new Vector4(0.5f, 1, 0.5f, 1), $"Character Affinities ({gameplay.CharacterAffinities.Length} types)");
        ImGui.Spacing();

        foreach (var affinity in gameplay.CharacterAffinities)
        {
            if (!MatchesFilter(affinity.DisplayName) && !MatchesFilter(affinity.RefName)) continue;

            if (ImGui.CollapsingHeader($"{affinity.DisplayName}##{affinity.RefName}"))
            {
                ImGui.Indent();
                ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1), affinity.Description ?? "No description");
                ImGui.Text($"Neutral Multiplier: {affinity.NeutralMultiplier}x");

                if (affinity.Matchup?.Length > 0)
                {
                    ImGui.TextColored(new Vector4(0.5f, 0.8f, 1, 1), "Matchups:");
                    foreach (var matchup in affinity.Matchup)
                    {
                        var color = matchup.Multiplier > 1
                            ? new Vector4(0.5f, 1, 0.5f, 1)  // Advantage
                            : matchup.Multiplier < 1
                                ? new Vector4(1, 0.5f, 0.5f, 1)  // Disadvantage
                                : new Vector4(0.7f, 0.7f, 0.7f, 1);  // Neutral
                        ImGui.TextColored(color, $"  vs {matchup.TargetAffinityRef}: {matchup.Multiplier}x");
                    }
                }

                ImGui.Unindent();
            }
        }
    }
}
