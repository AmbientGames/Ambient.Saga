using Ambient.Domain;
using Ambient.Domain.Contracts;
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

    // Filtered catalog cache — the world content catalog is immutable per loaded world,
    // so the filtered sections only change when the world or the search filter changes.
    private object? _cachedCatalogWorld;
    private string _cachedCatalogFilter = "";
    private Equipment[] _cachedEquipment = Array.Empty<Equipment>();
    private Consumable[] _cachedConsumables = Array.Empty<Consumable>();
    private Spell[] _cachedSpells = Array.Empty<Spell>();
    private Tool[] _cachedTools = Array.Empty<Tool>();
    private BuildingMaterial[] _cachedMaterials = Array.Empty<BuildingMaterial>();
    private List<(string Key, List<IBlock> Blocks)> _cachedBlockGroups = new();
    private int _cachedBlockCount;
    private Character[] _cachedCharacters = Array.Empty<Character>();
    private AvatarArchetype[] _cachedArchetypes = Array.Empty<AvatarArchetype>();
    private CharacterAffinity[] _cachedAffinities = Array.Empty<CharacterAffinity>();
    private CombatStance[] _cachedStances = Array.Empty<CombatStance>();
    private StatusEffect[] _cachedStatusEffects = Array.Empty<StatusEffect>();
    private LoadoutSlot[] _cachedLoadoutSlots = Array.Empty<LoadoutSlot>();
    private Quest[] _cachedQuests = Array.Empty<Quest>();
    private Faction[] _cachedFactions = Array.Empty<Faction>();
    private DialogueTree[] _cachedDialogueTrees = Array.Empty<DialogueTree>();
    private QuestToken[] _cachedQuestTokens = Array.Empty<QuestToken>();
    private Achievement[] _cachedAchievements = Array.Empty<Achievement>();

    public void Render(SagaMainViewModel viewModel)
    {
        // Header with world name
        var worldName = viewModel.CurrentWorld?.WorldConfiguration?.DisplayName ?? "Unknown World";
        ImGui.TextColored(new Vector4(1, 0.843f, 0, 1), $"WORLD CATALOG - {worldName}");
        ImGui.Separator();

        // Search filter
        ImGui.SetNextItemWidth(300 * UIConstants.DpiScale);
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
            EnsureCatalogCache(viewModel);

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

    /// <summary>
    /// Rebuilds the filtered section arrays when the world or search filter changes.
    /// The catalog is immutable per loaded world, so this is the complete invalidation key.
    /// </summary>
    private void EnsureCatalogCache(SagaMainViewModel viewModel)
    {
        var world = viewModel.CurrentWorld!;
        if (ReferenceEquals(_cachedCatalogWorld, world) && _cachedCatalogFilter == _searchFilter)
            return;

        _cachedCatalogWorld = world;
        _cachedCatalogFilter = _searchFilter;

        var gameplay = world.Gameplay;

        // Column 1: Items & Equipment
        _cachedEquipment = gameplay?.Equipment?.Where(e => MatchesFilter(e.DisplayName) || MatchesFilter(e.RefName)).ToArray() ?? Array.Empty<Equipment>();
        _cachedConsumables = gameplay?.Consumables?.Where(c => MatchesFilter(c.DisplayName) || MatchesFilter(c.RefName)).ToArray() ?? Array.Empty<Consumable>();
        _cachedSpells = gameplay?.Spells?.Where(s => MatchesFilter(s.DisplayName) || MatchesFilter(s.RefName)).ToArray() ?? Array.Empty<Spell>();
        _cachedTools = gameplay?.Tools?.Where(t => MatchesFilter(t.DisplayName) || MatchesFilter(t.RefName)).ToArray() ?? Array.Empty<Tool>();
        _cachedMaterials = gameplay?.BuildingMaterials?.Where(m => MatchesFilter(m.DisplayName) || MatchesFilter(m.RefName)).ToArray() ?? Array.Empty<BuildingMaterial>();

        // Blocks (filtered, then grouped by substance)
        var blocks = world.BlockProvider?.GetAllBlocks().ToList();
        var filteredBlocks = blocks?.Where(b => MatchesFilter(b.DisplayName) || MatchesFilter(b.RefName)).ToList();
        _cachedBlockCount = filteredBlocks?.Count ?? 0;
        _cachedBlockGroups = filteredBlocks?
            .GroupBy(b => b.SubstanceRef ?? "Miscellaneous")
            .OrderBy(g => g.Key)
            .Select(g => (g.Key, g.ToList()))
            .ToList() ?? new List<(string, List<IBlock>)>();

        // Column 2: Characters & Combat
        _cachedCharacters = gameplay?.Characters?.Where(c => MatchesFilter(c.DisplayName) || MatchesFilter(c.RefName)).ToArray() ?? Array.Empty<Character>();
        _cachedArchetypes = gameplay?.AvatarArchetypes?.Where(a => MatchesFilter(a.DisplayName) || MatchesFilter(a.RefName)).ToArray() ?? Array.Empty<AvatarArchetype>();
        _cachedAffinities = gameplay?.CharacterAffinities?.Where(a => MatchesFilter(a.DisplayName) || MatchesFilter(a.RefName)).ToArray() ?? Array.Empty<CharacterAffinity>();
        _cachedStances = gameplay?.CombatStances?.Where(s => MatchesFilter(s.DisplayName) || MatchesFilter(s.RefName)).ToArray() ?? Array.Empty<CombatStance>();
        _cachedStatusEffects = gameplay?.StatusEffects?.Where(e => MatchesFilter(e.DisplayName) || MatchesFilter(e.RefName)).ToArray() ?? Array.Empty<StatusEffect>();
        _cachedLoadoutSlots = gameplay?.LoadoutSlots?.Where(s => MatchesFilter(s.DisplayName) || MatchesFilter(s.RefName)).ToArray() ?? Array.Empty<LoadoutSlot>();

        // Column 3: World Systems
        _cachedQuests = gameplay?.Quests?.Where(q => MatchesFilter(q.DisplayName) || MatchesFilter(q.RefName)).ToArray() ?? Array.Empty<Quest>();
        _cachedFactions = gameplay?.Factions?.Where(f => MatchesFilter(f.DisplayName) || MatchesFilter(f.RefName)).ToArray() ?? Array.Empty<Faction>();
        _cachedDialogueTrees = gameplay?.DialogueTrees?.Where(d => MatchesFilter(d.DisplayName) || MatchesFilter(d.RefName)).ToArray() ?? Array.Empty<DialogueTree>();
        _cachedQuestTokens = gameplay?.QuestTokens?.Where(t => MatchesFilter(t.DisplayName) || MatchesFilter(t.RefName)).ToArray() ?? Array.Empty<QuestToken>();
        _cachedAchievements = gameplay?.Achievements?.Where(a => MatchesFilter(a.DisplayName) || MatchesFilter(a.RefName)).ToArray() ?? Array.Empty<Achievement>();
    }

    #region Column 1: Items & Equipment

    private void RenderItemsColumn(SagaMainViewModel viewModel)
    {
        ImGui.TextColored(new Vector4(0.5f, 1, 0.5f, 1), "ITEMS & EQUIPMENT");
        ImGui.Separator();
        ImGui.Spacing();

        // Equipment
        {
            var filtered = _cachedEquipment;
            if (filtered.Length > 0 && ImGui.CollapsingHeader($"Equipment ({filtered.Length})###Equipment"))
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
                            ImGuiHelpers.RenderAttributes(item.Effects, warmingLabel: "Insulation:", coolingLabel: "Insulation:");
                        if (item.StatusEffectRef != null)
                            ImGui.TextColored(new Vector4(1, 0.5f, 0.5f, 1), $"Applies: {item.StatusEffectRef} ({item.StatusEffectChance:P0})");
                        ImGui.TreePop();
                    }
                }
            }
        }

        // Consumables
        {
            var filtered = _cachedConsumables;
            if (filtered.Length > 0 && ImGui.CollapsingHeader($"Consumables ({filtered.Length})###Consumables"))
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
        {
            var filtered = _cachedSpells;
            if (filtered.Length > 0 && ImGui.CollapsingHeader($"Spells ({filtered.Length})###Spells"))
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
        {
            var filtered = _cachedTools;
            if (filtered.Length > 0 && ImGui.CollapsingHeader($"Tools ({filtered.Length})###Tools"))
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
        {
            var filtered = _cachedMaterials;
            if (filtered.Length > 0 && ImGui.CollapsingHeader($"Materials ({filtered.Length})###Materials"))
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
        {
            if (_cachedBlockCount > 0 && ImGui.CollapsingHeader($"Blocks ({_cachedBlockCount})###Blocks"))
            {
                foreach (var group in _cachedBlockGroups)
                {
                    if (ImGui.TreeNode($"{group.Key} ({group.Blocks.Count})###blkgrp_{group.Key}"))
                    {
                        foreach (var block in group.Blocks)
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

        // Characters
        {
            var filtered = _cachedCharacters;
            if (filtered.Length > 0 && ImGui.CollapsingHeader($"Characters ({filtered.Length})###Characters"))
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
        {
            var filtered = _cachedArchetypes;
            if (filtered.Length > 0 && ImGui.CollapsingHeader($"Archetypes ({filtered.Length})###Archetypes"))
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
        {
            var filtered = _cachedAffinities;
            if (filtered.Length > 0 && ImGui.CollapsingHeader($"Affinities ({filtered.Length})###Affinities"))
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
        {
            var filtered = _cachedStances;
            if (filtered.Length > 0 && ImGui.CollapsingHeader($"Combat Stances ({filtered.Length})###CombatStances"))
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
        {
            var filtered = _cachedStatusEffects;
            if (filtered.Length > 0 && ImGui.CollapsingHeader($"Status Effects ({filtered.Length})###StatusEffects"))
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
        {
            var filtered = _cachedLoadoutSlots;
            if (filtered.Length > 0 && ImGui.CollapsingHeader($"Loadout Slots ({filtered.Length})###LoadoutSlots"))
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
        {
            var filtered = _cachedQuests;
            if (filtered.Length > 0 && ImGui.CollapsingHeader($"Quests ({filtered.Length})###Quests"))
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
            var filtered = _cachedFactions;
            if (filtered.Length > 0 && ImGui.CollapsingHeader($"Factions ({filtered.Length})###Factions"))
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
        {
            var filtered = _cachedDialogueTrees;
            if (filtered.Length > 0 && ImGui.CollapsingHeader($"Dialogue Trees ({filtered.Length})###DialogueTrees"))
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
        {
            var filtered = _cachedQuestTokens;
            if (filtered.Length > 0 && ImGui.CollapsingHeader($"Quest Tokens ({filtered.Length})###QuestTokens"))
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
        {
            var filtered = _cachedAchievements;
            if (filtered.Length > 0 && ImGui.CollapsingHeader($"Achievements ({filtered.Length})###Achievements"))
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
