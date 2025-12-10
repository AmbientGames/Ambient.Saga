using Ambient.Domain;
using Ambient.Domain.DefinitionExtensions;

namespace Ambient.Infrastructure.GameLogic.Services;

public static class WorldValidationService
{
    public static void ValidateReferentialIntegrity(World world)
    {
        var errors = new List<string>();

        ValidateAvatarArchetypeReferences(world, errors);
        ValidateQuestItemReferences(world, errors);
        ValidateCharacterReferences(world, errors);
        ValidateCharacterAffinityReferences(world, errors);
        ValidateCombatStanceReferences(world, errors);
        ValidateDialogueReferences(world, errors);
        ValidateDialogueInventoryReferences(world, errors);
        ValidateAchievementReferences(world, errors);
        ValidateQuestReferences(world, errors);
        ValidateAvatarQuestReferences(world, errors);
        ValidateFactionReferences(world, errors);
        ValidateGameplayHeuristics(world, errors);
        ValidateDataQuality(world, errors);

        if (errors.Count > 0)
        {
            var errorMessage = "Referential integrity validation failed:\n" + string.Join("\n", errors);
            throw new InvalidOperationException(errorMessage);
        }
    }

    private static void ValidateAvatarArchetypeReferences(World world, List<string> errors)
    {
        if (world.Gameplay.AvatarArchetypes == null) return;

        foreach (var archetype in world.Gameplay.AvatarArchetypes)
        {
            var archetypeContext = $"AvatarArchetype '{archetype.RefName}'";

            // Validate AffinityRef
            if (!string.IsNullOrEmpty(archetype.AffinityRef))
            {
                ValidateReference(world.CharacterAffinitiesLookup, archetype.AffinityRef, archetypeContext, "AffinityRef", "CharacterAffinities", errors);
            }

            // Validate all inventory items in SpawnCapabilities
            ValidateItemCollection(world, errors, archetypeContext, archetype.SpawnCapabilities, "SpawnCapabilities");

            // Validate all inventory items in RespawnCapabilities
            ValidateItemCollection(world, errors, archetypeContext, archetype.RespawnCapabilities, "RespawnCapabilities");
        }
    }

    /// <summary>
    /// Validates all item references in an ItemCollection (inventory).
    /// Checks Equipment, Consumables, Tools, Spells, Blocks, BuildingMaterials, and QuestTokens.
    /// </summary>
    private static void ValidateItemCollection(World world, List<string> errors, string context, ItemCollection itemCollection, string collectionName)
    {
        if (itemCollection == null) return;

        var fullContext = $"{context} {collectionName}";

        // Validate Equipment references
        if (itemCollection.Equipment != null)
        {
            foreach (var entry in itemCollection.Equipment)
            {
                if (!string.IsNullOrEmpty(entry.EquipmentRef))
                {
                    ValidateReference(world.EquipmentLookup, entry.EquipmentRef, fullContext, "Equipment.EquipmentRef", "Equipment", errors);
                }
            }
        }

        // Validate Consumable references
        if (itemCollection.Consumables != null)
        {
            foreach (var entry in itemCollection.Consumables)
            {
                if (!string.IsNullOrEmpty(entry.ConsumableRef))
                {
                    ValidateReference(world.ConsumablesLookup, entry.ConsumableRef, fullContext, "Consumables.ConsumableRef", "Consumables", errors);
                }
            }
        }

        // Validate Tool references
        if (itemCollection.Tools != null)
        {
            foreach (var entry in itemCollection.Tools)
            {
                if (!string.IsNullOrEmpty(entry.ToolRef))
                {
                    ValidateReference(world.ToolsLookup, entry.ToolRef, fullContext, "Tools.ToolRef", "Tools", errors);
                }
            }
        }

        // Validate Spell references
        if (itemCollection.Spells != null)
        {
            foreach (var entry in itemCollection.Spells)
            {
                if (!string.IsNullOrEmpty(entry.SpellRef))
                {
                    ValidateReference(world.SpellsLookup, entry.SpellRef, fullContext, "Spells.SpellRef", "Spells", errors);
                }
            }
        }

        // Validate BuildingMaterial references
        if (itemCollection.BuildingMaterials != null)
        {
            foreach (var entry in itemCollection.BuildingMaterials)
            {
                if (!string.IsNullOrEmpty(entry.BuildingMaterialRef))
                {
                    ValidateReference(world.BuildingMaterialsLookup, entry.BuildingMaterialRef, fullContext, "BuildingMaterials.BuildingMaterialRef", "BuildingMaterials", errors);
                }
            }
        }

        // Validate QuestToken references
        if (itemCollection.QuestTokens != null)
        {
            foreach (var entry in itemCollection.QuestTokens)
            {
                if (!string.IsNullOrEmpty(entry.QuestTokenRef))
                {
                    ValidateReference(world.QuestTokensLookup, entry.QuestTokenRef, fullContext, "QuestTokens.QuestTokenRef", "QuestTokens", errors);
                }
            }
        }
    }

    private static void ValidateReference<T>(Dictionary<string, T> lookup, string refValue, string context, string propertyName, string lookupName, List<string> errors)
    {
        // Skip validation for special reference keywords
        if (IsSpecialReference(refValue))
            return;

        if (!string.IsNullOrEmpty(refValue) && !lookup.ContainsKey(refValue))
        {
            errors.Add($"{context}: {propertyName} '{refValue}' not found in {lookupName}");
        }
    }

    private static bool IsSpecialReference(string refValue)
    {
        // Special reference keywords that don't need lookup validation
        // Case-insensitive comparison to handle @self, @Self, @SELF, etc.
        return string.Equals(refValue, "@self", StringComparison.OrdinalIgnoreCase);
    }

    private static void ValidateQuestItemReferences(World world, List<string> errors)
    {
        // Build a map of quest keys to entities that provide them
        // Quest tokens are on: Characters and SagaFeatures
        // (Sagas are just spatial organizers and don't provide quest tokens)
        var QuestTokenProviders = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        // Scan all characters to see what quest keys they have or give
        if (world.Gameplay.Characters != null)
        {
            foreach (var character in world.Gameplay.Characters)
            {
                // Quest tokens in character inventory
                if (character.Capabilities?.QuestTokens != null)
                {
                    foreach (var QuestTokenStack in character.Capabilities.QuestTokens)
                    {
                        if (!string.IsNullOrEmpty(QuestTokenStack.QuestTokenRef))
                        {
                            // Check if this is a valid quest key
                            if (world.QuestTokensLookup.TryGetValue(QuestTokenStack.QuestTokenRef, out var QuestToken))
                            {
                                if (!QuestTokenProviders.ContainsKey(QuestTokenStack.QuestTokenRef))
                                {
                                    QuestTokenProviders[QuestTokenStack.QuestTokenRef] = new List<string>();
                                }
                                QuestTokenProviders[QuestTokenStack.QuestTokenRef].Add($"Character '{character.RefName}'");
                            }
                        }
                    }
                }

                // Quest tokens given when character defeated/traded (now in Interactable)
                if (character.Interactable?.GivesQuestTokenRef != null)
                {
                    foreach (var questTokenRef in character.Interactable.GivesQuestTokenRef)
                    {
                        if (!string.IsNullOrEmpty(questTokenRef))
                        {
                            if (world.QuestTokensLookup.ContainsKey(questTokenRef))
                            {
                                if (!QuestTokenProviders.ContainsKey(questTokenRef))
                                {
                                    QuestTokenProviders[questTokenRef] = new List<string>();
                                }
                                QuestTokenProviders[questTokenRef].Add($"Character '{character.RefName}'");
                            }
                            else
                            {
                                errors.Add($"Character '{character.RefName}': GivesQuestTokenRef '{questTokenRef}' not found in QuestTokens catalog");
                            }
                        }
                    }
                }
            }
        }

        // Scan all saga features to see what keys they provide
        if (world.Gameplay.SagaFeatures != null)
        {
            foreach (var feature in world.Gameplay.SagaFeatures)
            {
                var featureContext = $"SagaFeature '{feature.RefName}' (Type: {feature.Type})";

                // Validate RequiresQuestTokenRef (now in Interactable)
                if (feature.Interactable?.RequiresQuestTokenRef != null)
                {
                    foreach (var questTokenRef in feature.Interactable.RequiresQuestTokenRef)
                    {
                        if (!string.IsNullOrEmpty(questTokenRef))
                        {
                            if (!world.QuestTokensLookup.ContainsKey(questTokenRef))
                            {
                                errors.Add($"{featureContext}: RequiresQuestTokenRef '{questTokenRef}' not found in QuestTokens catalog");
                            }
                        }
                    }
                }

                // Validate GivesQuestTokenRef (now in Interactable)
                if (feature.Interactable?.GivesQuestTokenRef != null)
                {
                    foreach (var questTokenRef in feature.Interactable.GivesQuestTokenRef)
                    {
                        if (!string.IsNullOrEmpty(questTokenRef))
                        {
                            if (world.QuestTokensLookup.ContainsKey(questTokenRef))
                            {
                                if (!QuestTokenProviders.ContainsKey(questTokenRef))
                                {
                                    QuestTokenProviders[questTokenRef] = new List<string>();
                                }
                                QuestTokenProviders[questTokenRef].Add(featureContext);
                            }
                            else
                            {
                                errors.Add($"{featureContext}: GivesQuestTokenRef '{questTokenRef}' not found in QuestTokens catalog");
                            }
                        }
                    }
                }
            }
        }

        // Validate Saga character associations and structure
        if (world.Gameplay.SagaArcs != null)
        {
            foreach (var saga in world.Gameplay.SagaArcs)
            {
                var sagaContext = $"Saga '{saga.RefName}'";

                if (saga.Items != null)
                {
                    foreach (var item in saga.Items)
                    {
                        switch (item)
                        {
                            case SagaTrigger sagaTrigger:
                                // Inline trigger - validate directly
                                var inlineSagaTriggerContext = $"{sagaContext} Trigger '{sagaTrigger.RefName}'";

                                ValidateSagaTriggerQuestTokens(world, errors, inlineSagaTriggerContext, sagaTrigger);

                                if (sagaTrigger.Spawn != null)
                                {
                                    foreach (var spawn in sagaTrigger.Spawn)
                                    {
                                        ValidateCharacterSpawn(world, errors, inlineSagaTriggerContext, spawn);
                                    }
                                }
                                break;

                            case string patternRef:
                                // TriggerPatternRef - validate pattern triggers
                                var pattern = world.Gameplay?.SagaTriggerPatterns?
                                    .FirstOrDefault(tp => tp.RefName == patternRef);

                                if (pattern == null)
                                {
                                    errors.Add($"{sagaContext}: TriggerPattern '{patternRef}' not found");
                                }
                                else if (pattern.SagaTrigger != null)
                                {
                                    foreach (var patternTrigger in pattern.SagaTrigger)
                                    {
                                        var patternTriggerContext = $"{sagaContext} TriggerPattern '{patternRef}' Trigger '{patternTrigger.RefName}'";

                                        ValidateSagaTriggerQuestTokens(world, errors, patternTriggerContext, patternTrigger);

                                        if (patternTrigger.Spawn != null)
                                        {
                                            foreach (var spawn in patternTrigger.Spawn)
                                            {
                                                ValidateCharacterSpawn(world, errors, patternTriggerContext, spawn);
                                            }
                                        }
                                    }
                                }
                                break;
                        }
                    }
                }

                // Validate SagaFeatureRef (now a simple property, not discriminated union)
                if (!string.IsNullOrEmpty(saga.SagaFeatureRef))
                {
                    ValidateReference(world.SagaFeaturesLookup, saga.SagaFeatureRef, sagaContext, "SagaFeatureRef", "SagaFeatures", errors);
                }
            }
        }
    }

    private static void ValidateQuestTokenRef(World world, List<string> errors, string context,
        string? QuestTokenRef, Dictionary<string, List<string>> QuestTokenProviders)
    {
        if (string.IsNullOrEmpty(QuestTokenRef))
            return;

        // Check that the quest key exists in QuestTokensLookup
        if (!world.QuestTokensLookup.ContainsKey(QuestTokenRef))
        {
            errors.Add($"{context}: RequiredQuestTokenRef '{QuestTokenRef}' not found in QuestTokens catalog");
            return;
        }

        // Check that at least one entity provides this quest key (character or saga feature)
        if (!QuestTokenProviders.ContainsKey(QuestTokenRef))
        {
            errors.Add($"{context}: RequiredQuestTokenRef '{QuestTokenRef}' is not provided by any character or saga feature (orphaned quest key)");
        }
    }

    private static void ValidateSagaTriggerQuestTokens(World world, List<string> errors, string context, SagaTrigger trigger)
    {
        if (trigger == null) return;

        // Validate RequiresQuestTokenRef
        if (trigger.RequiresQuestTokenRef != null)
        {
            foreach (var questTokenRef in trigger.RequiresQuestTokenRef)
            {
                if (!string.IsNullOrEmpty(questTokenRef))
                {
                    ValidateReference(world.QuestTokensLookup, questTokenRef, context, "RequiresQuestTokenRef", "QuestTokens", errors);
                }
            }
        }

        // Validate GivesQuestTokenRef
        if (trigger.GivesQuestTokenRef != null)
        {
            foreach (var questTokenRef in trigger.GivesQuestTokenRef)
            {
                if (!string.IsNullOrEmpty(questTokenRef))
                {
                    ValidateReference(world.QuestTokensLookup, questTokenRef, context, "GivesQuestTokenRef", "QuestTokens", errors);
                }
            }
        }
    }

    private static void ValidateCharacterSpawn(World world, List<string> errors, string context, CharacterSpawn spawn)
    {
        if (spawn == null) return;

        // CharacterSpawn uses discriminated union: Item contains the RefName, ItemElementName tells us which type
        if (!string.IsNullOrEmpty(spawn.Item))
        {
            switch (spawn.ItemElementName)
            {
                case ItemChoiceType.CharacterRef:
                    ValidateReference(world.CharactersLookup, spawn.Item, context, "Spawn.CharacterRef", "Characters", errors);
                    break;
                case ItemChoiceType.CharacterArchetypeRef:
                    ValidateReference(world.CharacterArchetypesLookup, spawn.Item, context, "Spawn.CharacterArchetypeRef", "CharacterArchetypes", errors);
                    break;
            }
        }
        else
        {
            errors.Add($"{context}: Spawn must have either CharacterRef or CharacterArchetypeRef");
        }

        // Note: CharacterSpawn no longer has quest token conditions
        // Quest tokens are now handled at the Trigger level (RequiresQuestTokenRef/GivesQuestTokenRef)
    }

    private static void ValidateCharacterReferences(World world, List<string> errors)
    {
        if (world.Gameplay.Characters == null) return;

        foreach (var character in world.Gameplay.Characters)
        {
            var characterContext = $"Character '{character.RefName}'";

            // Validate DialogueTreeRef (in Interactable)
            if (!string.IsNullOrEmpty(character.Interactable?.DialogueTreeRef))
            {
                ValidateReference(world.DialogueTreesLookup, character.Interactable.DialogueTreeRef, characterContext, "DialogueTreeRef", "DialogueTrees", errors);
            }

            // Validate AffinityRef
            if (!string.IsNullOrEmpty(character.AffinityRef))
            {
                ValidateReference(world.CharacterAffinitiesLookup, character.AffinityRef, characterContext, "AffinityRef", "CharacterAffinities", errors);
            }

            // Validate all inventory items in Capabilities (what character owns/uses)
            ValidateItemCollection(world, errors, characterContext, character.Capabilities, "Capabilities");

            // Validate all inventory items in Loot (what character gives/drops to players)
            ValidateItemCollection(world, errors, characterContext, character.Interactable?.Loot, "Interactable.Loot");
        }
    }

    private static void ValidateCharacterAffinityReferences(World world, List<string> errors)
    {
        if (world.Gameplay.CharacterAffinities == null) return;

        foreach (var affinity in world.Gameplay.CharacterAffinities)
        {
            var affinityContext = $"CharacterAffinity '{affinity.RefName}'";

            // Validate that matchup references point to valid affinities
            if (affinity.Matchup != null)
            {
                foreach (var matchup in affinity.Matchup)
                {
                    if (!string.IsNullOrEmpty(matchup.TargetAffinityRef))
                    {
                        ValidateReference(world.CharacterAffinitiesLookup, matchup.TargetAffinityRef, affinityContext, "Matchup.TargetAffinityRef", "CharacterAffinities", errors);
                    }
                }
            }
        }
    }

    private static void ValidateCombatStanceReferences(World world, List<string> errors)
    {
        if (world.Gameplay.CombatStances == null) return;

        // CombatStances don't have cross-references like affinities do
        // This method exists for consistency and future expansion
        // Basic validation: confirm combat stances have valid effect values
        foreach (var combatStance in world.Gameplay.CombatStances)
        {
            var combatStanceContext = $"CombatStance '{combatStance.RefName}'";

            if (combatStance.Effects == null)
                continue;

            // Validate effects are reasonable (0.1 to 3.0 for multiplier-style values)
            if (combatStance.Effects.Strength < 0.1f || combatStance.Effects.Strength > 3.0f)
            {
                errors.Add($"{combatStanceContext}: Strength {combatStance.Effects.Strength} is outside reasonable range (0.1 - 3.0)");
            }
            if (combatStance.Effects.Defense < 0.1f || combatStance.Effects.Defense > 3.0f)
            {
                errors.Add($"{combatStanceContext}: Defense {combatStance.Effects.Defense} is outside reasonable range (0.1 - 3.0)");
            }
            if (combatStance.Effects.Speed < 0.1f || combatStance.Effects.Speed > 3.0f)
            {
                errors.Add($"{combatStanceContext}: Speed {combatStance.Effects.Speed} is outside reasonable range (0.1 - 3.0)");
            }
            if (combatStance.Effects.Magic < 0.1f || combatStance.Effects.Magic > 3.0f)
            {
                errors.Add($"{combatStanceContext}: Magic {combatStance.Effects.Magic} is outside reasonable range (0.1 - 3.0)");
            }
        }
    }

    private static void ValidateDialogueReferences(World world, List<string> errors)
    {
        if (world.Gameplay.DialogueTrees == null) return;

        foreach (var dialogueTree in world.Gameplay.DialogueTrees)
        {
            var treeContext = $"DialogueTree '{dialogueTree.RefName}'";

            // Validate StartNodeId exists
            if (!string.IsNullOrEmpty(dialogueTree.StartNodeId))
            {
                var startNodeExists = dialogueTree.Node?.Any(n => n.NodeId == dialogueTree.StartNodeId) == true;
                if (!startNodeExists)
                {
                    errors.Add($"{treeContext}: StartNodeId '{dialogueTree.StartNodeId}' does not match any Node.NodeId in this tree");
                }
            }
            else
            {
                errors.Add($"{treeContext}: StartNodeId is required but not specified");
            }

            if (dialogueTree.Node == null || dialogueTree.Node.Length == 0)
            {
                errors.Add($"{treeContext}: Must have at least one Node");
                continue;
            }

            // Build set of valid NodeIds in this tree
            var nodeIds = new HashSet<string>(dialogueTree.Node.Select(n => n.NodeId), StringComparer.OrdinalIgnoreCase);

            // Check for duplicate NodeIds
            var duplicateNodeIds = dialogueTree.Node
                .GroupBy(n => n.NodeId, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key);
            foreach (var duplicateId in duplicateNodeIds)
            {
                errors.Add($"{treeContext}: Duplicate NodeId '{duplicateId}' found");
            }

            foreach (var node in dialogueTree.Node)
            {
                var nodeContext = $"{treeContext} Node '{node.NodeId}'";

                // Validate NextNodeId if specified
                if (!string.IsNullOrEmpty(node.NextNodeId) && !nodeIds.Contains(node.NextNodeId))
                {
                    errors.Add($"{nodeContext}: NextNodeId '{node.NextNodeId}' does not match any Node.NodeId in this tree");
                }

                // Validate Conditions
                if (node.Condition != null)
                {
                    foreach (var condition in node.Condition)
                    {
                        ValidateDialogueCondition(world, errors, nodeContext, condition, dialogueTree.RefName, nodeIds);
                    }
                }

                // Validate Actions
                if (node.Action != null)
                {
                    foreach (var action in node.Action)
                    {
                        ValidateDialogueAction(world, errors, nodeContext, action);
                    }
                }

                // Validate Choices
                if (node.Choice != null)
                {
                    foreach (var choice in node.Choice)
                    {
                        var choiceContext = $"{nodeContext} Choice '{choice.Text}'";

                        // Validate NextNodeId
                        if (string.IsNullOrEmpty(choice.NextNodeId))
                        {
                            errors.Add($"{choiceContext}: NextNodeId is required");
                        }
                        else if (!nodeIds.Contains(choice.NextNodeId))
                        {
                            errors.Add($"{choiceContext}: NextNodeId '{choice.NextNodeId}' does not match any Node.NodeId in this tree");
                        }

                        // Validate Cost is non-negative
                        if (choice.Cost < 0)
                        {
                            errors.Add($"{choiceContext}: Cost cannot be negative (was {choice.Cost})");
                        }
                    }
                }
            }

            // Flow validation: check for unreachable and dead-end nodes
            // Get additional entry points from BattleDialogue triggers in characters
            var additionalEntryPoints = GetBattleDialogueEntryPoints(world, dialogueTree.RefName);
            ValidateDialogueFlow(dialogueTree, treeContext, additionalEntryPoints, errors);
        }
    }

    /// <summary>
    /// Get additional dialogue tree entry points from BattleDialogue triggers on characters.
    /// Boss characters have BattleDialogue that references specific nodes as entry points
    /// at different health thresholds (e.g., battle_opening, battle_berserk, battle_defeated).
    /// </summary>
    private static HashSet<string> GetBattleDialogueEntryPoints(World world, string dialogueTreeRef)
    {
        var entryPoints = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (world.Gameplay.Characters == null) return entryPoints;

        foreach (var character in world.Gameplay.Characters)
        {
            if (character.BattleDialogue == null) continue;

            foreach (var trigger in character.BattleDialogue)
            {
                // Check if this trigger references our dialogue tree
                if (trigger.DialogueTreeRef == dialogueTreeRef && !string.IsNullOrEmpty(trigger.StartNodeId))
                {
                    entryPoints.Add(trigger.StartNodeId);
                }
            }
        }

        return entryPoints;
    }

    private static void ValidateDialogueFlow(DialogueTree dialogueTree, string treeContext, HashSet<string> additionalEntryPoints, List<string> errors)
    {
        if (dialogueTree.Node == null || dialogueTree.Node.Length == 0) return;
        if (string.IsNullOrEmpty(dialogueTree.StartNodeId)) return;

        // Build node lookup
        var nodeMap = dialogueTree.Node.ToDictionary(n => n.NodeId, StringComparer.OrdinalIgnoreCase);

        // Collect all entry points: StartNodeId + BattleDialogue triggers + conditional fallback nodes
        var allEntryPoints = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { dialogueTree.StartNodeId };
        foreach (var ep in additionalEntryPoints)
        {
            allEntryPoints.Add(ep);
        }

        // Also add conditional fallback nodes (nodes with "_fail" or "_success" suffix that are condition results)
        // These are alternate paths from condition checks and are reachable via Condition evaluation
        foreach (var node in dialogueTree.Node)
        {
            // Pattern: bargain_hard_check leads to bargain_hard_fail on failure
            // We need to find if any node has a Condition that could branch to this node
            if (node.NodeId.EndsWith("_fail", StringComparison.OrdinalIgnoreCase) ||
                node.NodeId.EndsWith("_success", StringComparison.OrdinalIgnoreCase))
            {
                // Check if there's a corresponding _check node that has conditions
                var baseName = node.NodeId.Replace("_fail", "_check").Replace("_success", "_check");
                if (nodeMap.ContainsKey(baseName))
                {
                    // This is a conditional result node - mark the _check node as having a fallback
                    // Actually, we need to mark THIS node as reachable from the _check node
                    // For now, add it as an entry point since condition evaluation can reach it
                    allEntryPoints.Add(node.NodeId);
                }
            }
        }

        // Find all reachable nodes from all entry points
        var reachable = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<string>();

        foreach (var entryPoint in allEntryPoints)
        {
            if (!reachable.Contains(entryPoint))
            {
                queue.Enqueue(entryPoint);
                reachable.Add(entryPoint);
            }
        }

        while (queue.Count > 0)
        {
            var nodeId = queue.Dequeue();
            if (!nodeMap.TryGetValue(nodeId, out var node)) continue;

            // Collect all target nodes
            var targets = new List<string>();
            if (!string.IsNullOrEmpty(node.NextNodeId))
                targets.Add(node.NextNodeId);
            if (node.Choice != null)
                targets.AddRange(node.Choice.Where(c => !string.IsNullOrEmpty(c.NextNodeId)).Select(c => c.NextNodeId));

            foreach (var target in targets.Where(t => !reachable.Contains(t)))
            {
                reachable.Add(target);
                queue.Enqueue(target);
            }
        }

        // Report unreachable nodes
        foreach (var node in dialogueTree.Node)
        {
            if (!reachable.Contains(node.NodeId))
            {
                errors.Add($"{treeContext} Node '{node.NodeId}': Unreachable from any entry point");
            }
        }

        // Check for dead-end nodes (no choices, no NextNodeId, no terminal action)
        // BUT allow intentional terminal nodes:
        // - Nodes named "end" or ending with "_end" are intentional conversation endings
        // - BattleDialogue nodes (battle_*) that are entry points are allowed to be terminal
        foreach (var node in dialogueTree.Node)
        {
            var hasChoices = node.Choice != null && node.Choice.Length > 0;
            var hasNextNode = !string.IsNullOrEmpty(node.NextNodeId);
            var hasTerminalAction = node.Action != null && node.Action.Any(IsTerminalAction);

            if (!hasChoices && !hasNextNode && !hasTerminalAction)
            {
                // Check if this is an intentional terminal node
                var isIntentionalTerminal =
                    node.NodeId.Equals("end", StringComparison.OrdinalIgnoreCase) ||
                    node.NodeId.EndsWith("_end", StringComparison.OrdinalIgnoreCase) ||
                    // All battle_* dialogue nodes are intentional mid-battle interjections
                    // They display text/trigger actions and then combat continues (no player choice needed)
                    node.NodeId.StartsWith("battle_", StringComparison.OrdinalIgnoreCase);

                if (!isIntentionalTerminal)
                {
                    errors.Add($"{treeContext} Node '{node.NodeId}': Dead-end node (no choices, no NextNodeId, no terminal action)");
                }
            }
        }
    }

    private static bool IsTerminalAction(DialogueAction action)
    {
        return action.Type is DialogueActionType.StartCombat or DialogueActionType.StartBossBattle
            or DialogueActionType.EndBattle or DialogueActionType.AcceptQuest
            or DialogueActionType.CompleteQuest or DialogueActionType.OpenMerchantTrade;
    }

    private static void ValidateDialogueCondition(World world, List<string> errors, string context, DialogueCondition condition, string dialogueTreeRef, HashSet<string> validNodeIds)
    {
        if (condition == null) return;

        var conditionContext = $"{context} Condition '{condition.Type}'";

        switch (condition.Type)
        {
            // Quest tokens
            case DialogueConditionType.HasQuestToken:
            case DialogueConditionType.LacksQuestToken:
                ValidateConditionRefName(world.QuestTokensLookup, condition.RefName, conditionContext, "QuestTokens", errors, required: true);
                break;

            // Stackable items
            case DialogueConditionType.HasConsumable:
            case DialogueConditionType.LacksConsumable:
                ValidateConditionRefName(world.ConsumablesLookup, condition.RefName, conditionContext, "Consumables", errors, required: true);
                break;

            case DialogueConditionType.HasMaterial:
            case DialogueConditionType.LacksMaterial:
                ValidateConditionRefName(world.BuildingMaterialsLookup, condition.RefName, conditionContext, "Materials", errors, required: true);
                break;

            // Degradable items
            case DialogueConditionType.HasEquipment:
            case DialogueConditionType.LacksEquipment:
                ValidateConditionRefName(world.EquipmentLookup, condition.RefName, conditionContext, "Equipment", errors, required: true);
                break;

            case DialogueConditionType.HasTool:
            case DialogueConditionType.LacksTool:
                ValidateConditionRefName(world.ToolsLookup, condition.RefName, conditionContext, "Tools", errors, required: true);
                break;

            case DialogueConditionType.HasSpell:
            case DialogueConditionType.LacksSpell:
                ValidateConditionRefName(world.SpellsLookup, condition.RefName, conditionContext, "Spells", errors, required: true);
                break;

            // Player state
            case DialogueConditionType.HasAchievement:
                ValidateConditionRefName(world.AchievementsLookup, condition.RefName, conditionContext, "Achievements", errors, required: true);
                break;

            case DialogueConditionType.Credits:
            case DialogueConditionType.Health:
                ValidateNumericConditionValue(condition, conditionContext, errors);
                break;

            // Dialogue history
            case DialogueConditionType.PlayerVisitCount:
                ValidateConditionRefName(world.DialogueTreesLookup, condition.RefName, conditionContext, "DialogueTrees", errors, required: true);
                ValidateNumericConditionValue(condition, conditionContext, errors);
                break;

            case DialogueConditionType.NodeVisited:
                // RefName should be dialogue tree, Value should be node ID
                ValidateConditionRefName(world.DialogueTreesLookup, condition.RefName, conditionContext, "DialogueTrees", errors, required: true);
                if (string.IsNullOrEmpty(condition.Value))
                {
                    errors.Add($"{conditionContext}: NodeVisited requires Value to specify the NodeId");
                }
                else if (condition.RefName == dialogueTreeRef && !validNodeIds.Contains(condition.Value))
                {
                    errors.Add($"{conditionContext}: NodeVisited Value '{condition.Value}' does not match any NodeId in dialogue tree '{dialogueTreeRef}'");
                }
                break;

            // World state
            case DialogueConditionType.BossDefeatedCount:
                ValidateNumericConditionValue(condition, conditionContext, errors);
                break;

            // Quest state
            case DialogueConditionType.QuestActive:
            case DialogueConditionType.QuestCompleted:
            case DialogueConditionType.QuestNotStarted:
                ValidateConditionRefName(world.QuestsLookup, condition.RefName, conditionContext, "Quests", errors, required: true);
                break;

            // Reputation
            case DialogueConditionType.ReputationLevel:
            case DialogueConditionType.ReputationValue:
                ValidateConditionRefName(world.FactionsLookup, condition.FactionRef, conditionContext, "Factions", errors, required: true);
                ValidateNumericConditionValue(condition, conditionContext, errors);
                break;

            // Trait comparison
            case DialogueConditionType.TraitComparison:
                // Trait attribute is validated by the schema (CharacterTraitType enum)
                // Value is optional - used for comparing trait values
                // No additional validation needed here
                break;

            default:
                errors.Add($"{conditionContext}: Unknown condition type");
                break;
        }
    }

    private static void ValidateConditionRefName<T>(Dictionary<string, T> lookup, string refName, string context, string lookupName, List<string> errors, bool required)
    {
        if (required && string.IsNullOrEmpty(refName))
        {
            errors.Add($"{context}: RefName is required but not specified");
        }
        else if (!string.IsNullOrEmpty(refName) && !IsSpecialReference(refName) && !lookup.ContainsKey(refName))
        {
            errors.Add($"{context}: RefName '{refName}' not found in {lookupName}");
        }
    }

    private static void ValidateNumericConditionValue(DialogueCondition condition, string context, List<string> errors)
    {
        if (string.IsNullOrEmpty(condition.Value))
        {
            errors.Add($"{context}: Value is required for numeric comparison");
        }
        else if (!int.TryParse(condition.Value, out _))
        {
            errors.Add($"{context}: Value '{condition.Value}' is not a valid integer");
        }
    }

    private static void ValidateDialogueAction(World world, List<string> errors, string context, DialogueAction action)
    {
        if (action == null) return;

        var actionContext = $"{context} Action '{action.Type}'";

        switch (action.Type)
        {
            // Quest tokens
            case DialogueActionType.GiveQuestToken:
            case DialogueActionType.TakeQuestToken:
                ValidateActionRefName(world.QuestTokensLookup, action.RefName, actionContext, "QuestTokens", errors, required: true);
                break;

            // Stackable items (Amount attribute applies)
            case DialogueActionType.GiveConsumable:
            case DialogueActionType.TakeConsumable:
                ValidateActionRefName(world.ConsumablesLookup, action.RefName, actionContext, "Consumables", errors, required: true);
                ValidateActionAmount(action.Amount, actionContext, errors, allowZero: false);
                break;

            case DialogueActionType.GiveMaterial:
            case DialogueActionType.TakeMaterial:
                ValidateActionRefName(world.BuildingMaterialsLookup, action.RefName, actionContext, "Materials", errors, required: true);
                ValidateActionAmount(action.Amount, actionContext, errors, allowZero: false);
                break;

            case DialogueActionType.GiveBlock:
            case DialogueActionType.TakeBlock:
                // note, not possible to validate the block is valid
                //ValidateActionRefName(world.BlocksLookup, action.RefName, actionContext, "Blocks", errors, required: true);
                ValidateActionAmount(action.Amount, actionContext, errors, allowZero: false);
                break;

            // Degradable items (Amount ignored, but validate RefName)
            case DialogueActionType.GiveEquipment:
            case DialogueActionType.TakeEquipment:
                ValidateActionRefName(world.EquipmentLookup, action.RefName, actionContext, "Equipment", errors, required: true);
                break;

            case DialogueActionType.GiveTool:
            case DialogueActionType.TakeTool:
                ValidateActionRefName(world.ToolsLookup, action.RefName, actionContext, "Tools", errors, required: true);
                break;

            case DialogueActionType.GiveSpell:
            case DialogueActionType.TakeSpell:
                ValidateActionRefName(world.SpellsLookup, action.RefName, actionContext, "Spells", errors, required: true);
                break;

            // Currency
            case DialogueActionType.TransferCurrency:
                if (action.Amount == 0)
                {
                    errors.Add($"{actionContext}: TransferCurrency Amount should not be zero (no effect)");
                }
                break;

            // Achievements
            case DialogueActionType.UnlockAchievement:
                ValidateActionRefName(world.AchievementsLookup, action.RefName, actionContext, "Achievements", errors, required: true);
                break;

            // System transitions
            case DialogueActionType.OpenMerchantTrade:
            case DialogueActionType.StartBossBattle:
            case DialogueActionType.StartCombat:
                if (string.IsNullOrEmpty(action.CharacterRef))
                {
                    errors.Add($"{actionContext}: CharacterRef is required for system transition actions");
                }
                else
                {
                    ValidateReference(world.CharactersLookup, action.CharacterRef, actionContext, "CharacterRef", "Characters", errors);
                }
                break;

            case DialogueActionType.SpawnCharacters:
                // Either CharacterRef or CharacterArchetypeRef required (not both)
                var hasCharacterRef = !string.IsNullOrEmpty(action.CharacterRef);
                var hasArchetypeRef = !string.IsNullOrEmpty(action.CharacterArchetypeRef);

                if (!hasCharacterRef && !hasArchetypeRef)
                {
                    errors.Add($"{actionContext}: Either CharacterRef or CharacterArchetypeRef is required");
                }
                else if (hasCharacterRef && hasArchetypeRef)
                {
                    errors.Add($"{actionContext}: Cannot specify both CharacterRef and CharacterArchetypeRef (use one or the other)");
                }
                else if (hasCharacterRef)
                {
                    ValidateReference(world.CharactersLookup, action.CharacterRef, actionContext, "CharacterRef", "Characters", errors);
                }
                else if (hasArchetypeRef)
                {
                    ValidateReference(world.CharacterArchetypesLookup, action.CharacterArchetypeRef, actionContext, "CharacterArchetypeRef", "CharacterArchetypes", errors);
                }

                ValidateActionAmount(action.Amount, actionContext, errors, allowZero: false);
                break;

            // Character trait management
            case DialogueActionType.AssignTrait:
            case DialogueActionType.RemoveTrait:
                // Trait attribute is validated by the schema (CharacterTraitType enum)
                // TraitValue is optional and only used for numeric traits
                // No additional validation needed here
                break;

            // Character state management
            case DialogueActionType.SetCharacterState:
                // State is validated by the schema (CharacterStateType enum)
                // No additional validation needed here
                break;

            // Reputation management
            case DialogueActionType.ChangeReputation:
                ValidateActionRefName(world.FactionsLookup, action.FactionRef, actionContext, "Factions", errors, required: true);
                ValidateActionAmount(action.Amount, actionContext, errors, allowZero: true); // Can be negative
                break;

            // Quest actions
            case DialogueActionType.AcceptQuest:
            case DialogueActionType.CompleteQuest:
            case DialogueActionType.AbandonQuest:
                ValidateActionRefName(world.QuestsLookup, action.RefName, actionContext, "Quests", errors, required: true);
                break;

            // Mid-battle actions
            case DialogueActionType.ChangeStance:
                // RefName specifies the stance (validated by game logic, not world data)
                // No additional validation needed here
                break;

            case DialogueActionType.ChangeAffinity:
                // RefName specifies the affinity (validated by game logic, not world data)
                // No additional validation needed here
                break;

            case DialogueActionType.HealSelf:
                // Amount specifies heal percentage
                ValidateActionAmount(action.Amount, actionContext, errors, allowZero: false);
                break;

            case DialogueActionType.EndBattle:
                // No attributes required - just ends the battle
                break;

            case DialogueActionType.CastSpell:
                ValidateActionRefName(world.SpellsLookup, action.RefName, actionContext, "Spells", errors, required: true);
                break;

            case DialogueActionType.SummonAlly:
                // Similar to SpawnCharacters - requires either CharacterRef or CharacterArchetypeRef
                var hasSummonCharacterRef = !string.IsNullOrEmpty(action.CharacterRef);
                var hasSummonArchetypeRef = !string.IsNullOrEmpty(action.CharacterArchetypeRef);

                if (!hasSummonCharacterRef && !hasSummonArchetypeRef)
                {
                    errors.Add($"{actionContext}: Either CharacterRef or CharacterArchetypeRef is required");
                }
                else if (hasSummonCharacterRef && hasSummonArchetypeRef)
                {
                    errors.Add($"{actionContext}: Cannot specify both CharacterRef and CharacterArchetypeRef (use one or the other)");
                }
                else if (hasSummonCharacterRef)
                {
                    ValidateReference(world.CharactersLookup, action.CharacterRef, actionContext, "CharacterRef", "Characters", errors);
                }
                else if (hasSummonArchetypeRef)
                {
                    ValidateReference(world.CharacterArchetypesLookup, action.CharacterArchetypeRef, actionContext, "CharacterArchetypeRef", "CharacterArchetypes", errors);
                }
                break;

            case DialogueActionType.ApplyStatusEffect:
                // RefName specifies the status effect (validated by game logic, not world data)
                // No additional validation needed here
                break;

            // Party management
            case DialogueActionType.JoinParty:
            case DialogueActionType.LeaveParty:
                // CharacterRef specifies which character joins/leaves the party
                if (string.IsNullOrEmpty(action.CharacterRef))
                {
                    errors.Add($"{actionContext}: CharacterRef is required for party management actions");
                }
                else
                {
                    ValidateReference(world.CharactersLookup, action.CharacterRef, actionContext, "CharacterRef", "Characters", errors);
                }
                break;

            // Affinity granting
            case DialogueActionType.GrantAffinity:
                // RefName specifies the affinity type (e.g., "Fire", "Water")
                // CharacterRef specifies which character grants the affinity
                if (string.IsNullOrEmpty(action.RefName))
                {
                    errors.Add($"{actionContext}: RefName is required (specifies affinity type)");
                }
                if (string.IsNullOrEmpty(action.CharacterRef))
                {
                    errors.Add($"{actionContext}: CharacterRef is required (specifies granting character)");
                }
                else
                {
                    ValidateReference(world.CharactersLookup, action.CharacterRef, actionContext, "CharacterRef", "Characters", errors);
                }
                break;

            default:
                errors.Add($"{actionContext}: Unknown action type");
                break;
        }
    }

    private static void ValidateActionRefName<T>(Dictionary<string, T> lookup, string refName, string context, string lookupName, List<string> errors, bool required)
    {
        if (required && string.IsNullOrEmpty(refName))
        {
            errors.Add($"{context}: RefName is required but not specified");
        }
        else if (!string.IsNullOrEmpty(refName) && !IsSpecialReference(refName) && !lookup.ContainsKey(refName))
        {
            errors.Add($"{context}: RefName '{refName}' not found in {lookupName}");
        }
    }

    private static void ValidateActionAmount(int amount, string context, List<string> errors, bool allowZero)
    {
        if (amount < 0)
        {
            errors.Add($"{context}: Amount cannot be negative (was {amount})");
        }
        else if (!allowZero && amount == 0)
        {
            errors.Add($"{context}: Amount should not be zero (no effect)");
        }
    }

    /// <summary>
    /// Validates that characters have items in their loot pool that they promise to give in dialogue.
    /// This ensures dialogue doesn't promise items that don't exist in the character's Interactable.Loot.
    ///
    /// ARCHITECTURE NOTE:
    /// - Character.Capabilities = Items the character OWNS and USES (personal gear, not dropped)
    /// - Character.Interactable.Loot = Items the character GIVES/DROPS to players (rewards, shop inventory)
    /// - Dialogue rewards come from Loot, not Capabilities (game balance: boss can use powerful gear without dropping it)
    ///
    /// Validates "Give" actions only - "Take" actions are validated at runtime against player inventory.
    ///
    /// Checks:
    /// - GiveEquipment: Character.Interactable.Loot.Equipment contains EquipmentRef
    /// - GiveConsumable: Character.Interactable.Loot.Consumables has sufficient Quantity
    /// - GiveMaterial: Character.Interactable.Loot.BuildingMaterials has sufficient Quantity
    /// - GiveBlock: Character.Interactable.Loot.Blocks has sufficient Quantity
    /// - GiveTool: Character.Interactable.Loot.Tools contains ToolRef
    /// - GiveSpell: Character.Interactable.Loot.Spells contains SpellRef
    /// - GiveQuestToken: Character.Interactable.Loot.QuestTokens contains QuestTokenRef
    /// - TransferCurrency (positive): Character.Stats.Credits >= Amount
    /// </summary>
    private static void ValidateDialogueInventoryReferences(World world, List<string> errors)
    {
        if (world.Gameplay.Characters == null) return;

        foreach (var character in world.Gameplay.Characters)
        {
            // Only validate characters with dialogue
            var dialogueTreeRef = character.Interactable?.DialogueTreeRef;
            if (string.IsNullOrEmpty(dialogueTreeRef))
                continue;

            // Get the dialogue tree
            if (!world.DialogueTreesLookup.TryGetValue(dialogueTreeRef, out var dialogueTree))
                continue; // Already validated in ValidateCharacterReferences

            var characterContext = $"Character '{character.RefName}'";

            // Check all dialogue nodes for "Give" actions (not "Take" - those validate player inventory at runtime)
            if (dialogueTree.Node != null)
            {
                foreach (var node in dialogueTree.Node)
                {
                    if (node.Action == null) continue;

                    foreach (var action in node.Action)
                    {
                        var nodeContext = $"{characterContext} DialogueTree '{dialogueTreeRef}' Node '{node.NodeId}' Action '{action.Type}'";

                        switch (action.Type)
                        {
                            case DialogueActionType.GiveEquipment:
                                ValidateCharacterHasEquipment(character, action.RefName, nodeContext, errors);
                                break;

                            case DialogueActionType.GiveConsumable:
                                ValidateCharacterHasConsumable(character, action.RefName, action.Amount, nodeContext, errors);
                                break;

                            case DialogueActionType.GiveMaterial:
                                ValidateCharacterHasMaterial(character, action.RefName, action.Amount, nodeContext, errors);
                                break;

                            case DialogueActionType.GiveBlock:
                                ValidateCharacterHasBlock(character, action.RefName, action.Amount, nodeContext, errors);
                                break;

                            case DialogueActionType.GiveTool:
                                ValidateCharacterHasTool(character, action.RefName, nodeContext, errors);
                                break;

                            case DialogueActionType.GiveSpell:
                                ValidateCharacterHasSpell(character, action.RefName, nodeContext, errors);
                                break;

                            case DialogueActionType.GiveQuestToken:
                                // Quest tokens are abstract progress markers, not physical items
                                // They don't need to be in a character's loot pool - they're created by dialogue
                                // Only validate that the quest token exists in world definitions
                                if (!string.IsNullOrEmpty(action.RefName) && !world.QuestTokensLookup.ContainsKey(action.RefName))
                                {
                                    errors.Add($"{nodeContext}: Dialogue gives QuestToken '{action.RefName}' which does not exist in QuestTokens definitions");
                                }
                                break;

                            case DialogueActionType.TransferCurrency:
                                // Only validate positive transfers (giving to player)
                                if (action.Amount > 0)
                                {
                                    ValidateCharacterHasCurrency(character, action.Amount, nodeContext, errors);
                                }
                                break;
                        }
                    }
                }
            }
        }
    }

    private static void ValidateCharacterHasEquipment(Character character, string equipmentRef, string context, List<string> errors)
    {
        if (string.IsNullOrEmpty(equipmentRef)) return;

        var hasEquipment = character.Interactable?.Loot?.Equipment?.Any(e => e.EquipmentRef == equipmentRef) == true;
        if (!hasEquipment)
        {
            errors.Add($"{context}: Dialogue promises '{equipmentRef}' but character loot pool (Interactable.Loot.Equipment) does not contain it");
        }
    }

    private static void ValidateCharacterHasConsumable(Character character, string consumableRef, int amount, string context, List<string> errors)
    {
        if (string.IsNullOrEmpty(consumableRef)) return;

        var consumable = character.Interactable?.Loot?.Consumables?.FirstOrDefault(c => c.ConsumableRef == consumableRef);
        if (consumable == null)
        {
            errors.Add($"{context}: Dialogue promises {amount} '{consumableRef}' but character loot pool (Interactable.Loot.Consumables) does not contain it");
        }
        else if (consumable.Quantity < amount)
        {
            errors.Add($"{context}: Dialogue promises {amount} '{consumableRef}' but character loot only has {consumable.Quantity}");
        }
    }

    private static void ValidateCharacterHasMaterial(Character character, string materialRef, int amount, string context, List<string> errors)
    {
        if (string.IsNullOrEmpty(materialRef)) return;

        var material = character.Interactable?.Loot?.BuildingMaterials?.FirstOrDefault(m => m.BuildingMaterialRef == materialRef);
        if (material == null)
        {
            errors.Add($"{context}: Dialogue promises {amount} '{materialRef}' but character loot pool (Interactable.Loot.BuildingMaterials) does not contain it");
        }
        else if (material.Quantity < amount)
        {
            errors.Add($"{context}: Dialogue promises {amount} '{materialRef}' but character loot only has {material.Quantity}");
        }
    }

    private static void ValidateCharacterHasBlock(Character character, string blockRef, int amount, string context, List<string> errors)
    {
        if (string.IsNullOrEmpty(blockRef)) return;

        var block = character.Interactable?.Loot?.Blocks?.FirstOrDefault(b => b.BlockRef == blockRef);
        if (block == null)
        {
            errors.Add($"{context}: Dialogue promises {amount} '{blockRef}' but character loot pool (Interactable.Loot.Blocks) does not contain it");
        }
        else if (block.Quantity < amount)
        {
            errors.Add($"{context}: Dialogue promises {amount} '{blockRef}' but character loot only has {block.Quantity}");
        }
    }

    private static void ValidateCharacterHasTool(Character character, string toolRef, string context, List<string> errors)
    {
        if (string.IsNullOrEmpty(toolRef)) return;

        var hasTool = character.Interactable?.Loot?.Tools?.Any(t => t.ToolRef == toolRef) == true;
        if (!hasTool)
        {
            errors.Add($"{context}: Dialogue promises '{toolRef}' but character loot pool (Interactable.Loot.Tools) does not contain it");
        }
    }

    private static void ValidateCharacterHasSpell(Character character, string spellRef, string context, List<string> errors)
    {
        if (string.IsNullOrEmpty(spellRef)) return;

        var hasSpell = character.Interactable?.Loot?.Spells?.Any(s => s.SpellRef == spellRef) == true;
        if (!hasSpell)
        {
            errors.Add($"{context}: Dialogue promises '{spellRef}' but character loot pool (Interactable.Loot.Spells) does not contain it");
        }
    }

    private static void ValidateCharacterHasCurrency(Character character, int amount, string context, List<string> errors)
    {
        var credits = character.Stats?.Credits ?? 0;
        if (credits < amount)
        {
            errors.Add($"{context}: Dialogue promises {amount} credits but character only has {credits} (Stats.Credits)");
        }
    }

    private static void ValidateAchievementReferences(World world, List<string> errors)
    {
        if (world.Gameplay.Achievements == null)
            return;

        foreach (var achievement in world.Gameplay.Achievements)
        {
            if (achievement.Criteria == null)
                continue;

            var achievementContext = $"Achievement '{achievement.RefName}' ({achievement.DisplayName})";
            var criteria = achievement.Criteria;

            // Validate CharacterRef (used by CharactersDefeatedByRef)
            if (!string.IsNullOrEmpty(criteria.CharacterRef))
            {
                if (!world.CharactersLookup.ContainsKey(criteria.CharacterRef))
                {
                    errors.Add($"{achievementContext}: CharacterRef '{criteria.CharacterRef}' not found in Characters catalog");
                }
            }

            // Validate SagaArcRef (used by SagasDiscovered, SagasCompleted with filter)
            if (!string.IsNullOrEmpty(criteria.SagaArcRef))
            {
                if (!world.SagaArcLookup.ContainsKey(criteria.SagaArcRef))
                {
                    errors.Add($"{achievementContext}: SagaRef '{criteria.SagaArcRef}' not found in Sagas catalog");
                }
            }

            // Validate QuestTokenRef (used by QuestTokensEarned with filter)
            if (!string.IsNullOrEmpty(criteria.QuestTokenRef))
            {
                if (!world.QuestTokensLookup.ContainsKey(criteria.QuestTokenRef))
                {
                    errors.Add($"{achievementContext}: QuestTokenRef '{criteria.QuestTokenRef}' not found in QuestTokens catalog");
                }
            }

            // Validate FactionRef (used by ReputationLevel criteria)
            if (!string.IsNullOrEmpty(criteria.FactionRef))
            {
                ValidateReference(world.FactionsLookup, criteria.FactionRef, achievementContext, "FactionRef", "Factions", errors);
            }

            // Note: CharacterType, CharacterTag, and TraitType are string-based filters
            // They don't reference catalogs, so no validation needed
            // CharacterType: "Boss", "Merchant", etc. (free-form)
            // CharacterTag: Tags from Character.Tags array (free-form)
            // TraitType: "Friendly", "Hostile", etc. (free-form)
        }
    }

    private static void ValidateQuestReferences(World world, List<string> errors)
    {
        if (world.Gameplay.Quests == null) return;

        foreach (var quest in world.Gameplay.Quests)
        {
            var questContext = $"Quest '{quest.RefName}'";

            // Validate Prerequisites FactionRef
            if (quest.Prerequisites != null)
            {
                foreach (var prereq in quest.Prerequisites)
                {
                    if (!string.IsNullOrEmpty(prereq.FactionRef))
                    {
                        ValidateReference(world.FactionsLookup, prereq.FactionRef, $"{questContext} Prerequisite", "FactionRef", "Factions", errors);
                    }
                }
            }

            // Validate Rewards Reputation FactionRef
            if (quest.Rewards != null)
            {
                foreach (var reward in quest.Rewards)
                {
                    if (reward.Reputation != null)
                    {
                        foreach (var reputation in reward.Reputation)
                        {
                            if (!string.IsNullOrEmpty(reputation.FactionRef))
                            {
                                ValidateReference(world.FactionsLookup, reputation.FactionRef, $"{questContext} Reward (Condition: {reward.Condition})", "Reputation.FactionRef", "Factions", errors);
                            }
                        }
                    }
                }
            }

            // Validate quest structure and completability
            ValidateQuestStructure(quest, questContext, errors);
        }
    }

    private static void ValidateQuestStructure(Quest quest, string questContext, List<string> errors)
    {
        if (quest.Stages == null || quest.Stages.Stage == null || quest.Stages.Stage.Length == 0)
        {
            errors.Add($"{questContext}: Must have at least one Stage");
            return;
        }

        // Build stage lookup
        var stageMap = quest.Stages.Stage.ToDictionary(s => s.RefName, StringComparer.OrdinalIgnoreCase);
        var stageRefs = new HashSet<string>(stageMap.Keys, StringComparer.OrdinalIgnoreCase);

        // Validate StartStage exists
        if (string.IsNullOrEmpty(quest.Stages.StartStage))
        {
            errors.Add($"{questContext}: StartStage is required");
            return;
        }

        if (!stageRefs.Contains(quest.Stages.StartStage))
        {
            errors.Add($"{questContext}: StartStage '{quest.Stages.StartStage}' does not match any Stage.RefName");
            return;
        }

        // Validate NextStage references
        foreach (var stage in quest.Stages.Stage)
        {
            if (!string.IsNullOrEmpty(stage.NextStage) && !stageRefs.Contains(stage.NextStage))
            {
                errors.Add($"{questContext} Stage '{stage.RefName}': NextStage '{stage.NextStage}' does not match any Stage.RefName");
            }

            // Validate branch NextStage references
            if (stage.Item is QuestStageBranches branches && branches.Branch != null)
            {
                foreach (var branch in branches.Branch)
                {
                    if (!string.IsNullOrEmpty(branch.NextStage) && !stageRefs.Contains(branch.NextStage))
                    {
                        errors.Add($"{questContext} Stage '{stage.RefName}' Branch '{branch.RefName}': NextStage '{branch.NextStage}' does not match any Stage.RefName");
                    }
                }
            }
        }

        // Check quest completability: can we reach a terminal stage from StartStage?
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var canComplete = CanReachTerminalStage(quest.Stages.StartStage, stageMap, visited);

        if (!canComplete)
        {
            errors.Add($"{questContext}: Quest may not be completable (no terminal stage reachable from StartStage '{quest.Stages.StartStage}')");
        }

        // Check for unreachable stages
        var reachable = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        FindReachableStages(quest.Stages.StartStage, stageMap, reachable);

        foreach (var stage in quest.Stages.Stage)
        {
            if (!reachable.Contains(stage.RefName))
            {
                errors.Add($"{questContext} Stage '{stage.RefName}': Unreachable from StartStage '{quest.Stages.StartStage}'");
            }
        }
    }

    private static bool CanReachTerminalStage(string stageRef, Dictionary<string, QuestStage> stageMap, HashSet<string> visited)
    {
        if (string.IsNullOrEmpty(stageRef) || !stageMap.TryGetValue(stageRef, out var stage))
            return false;

        if (visited.Contains(stageRef))
            return false; // Cycle detected

        visited.Add(stageRef);

        // Terminal stage: no NextStage and either no branches or all branches have no NextStage
        var isTerminal = string.IsNullOrEmpty(stage.NextStage);
        if (stage.Item is QuestStageBranches branches && branches.Branch != null)
        {
            // Check if any branch leads somewhere
            foreach (var branch in branches.Branch)
            {
                if (!string.IsNullOrEmpty(branch.NextStage))
                {
                    isTerminal = false;
                    if (CanReachTerminalStage(branch.NextStage, stageMap, new HashSet<string>(visited, StringComparer.OrdinalIgnoreCase)))
                        return true;
                }
            }
        }

        if (isTerminal)
            return true;

        // Follow NextStage
        return CanReachTerminalStage(stage.NextStage, stageMap, visited);
    }

    private static void FindReachableStages(string stageRef, Dictionary<string, QuestStage> stageMap, HashSet<string> reachable)
    {
        if (string.IsNullOrEmpty(stageRef) || !stageMap.TryGetValue(stageRef, out var stage))
            return;

        if (reachable.Contains(stageRef))
            return;

        reachable.Add(stageRef);

        // Follow NextStage
        if (!string.IsNullOrEmpty(stage.NextStage))
            FindReachableStages(stage.NextStage, stageMap, reachable);

        // Follow branch NextStages
        if (stage.Item is QuestStageBranches branches && branches.Branch != null)
        {
            foreach (var branch in branches.Branch)
            {
                if (!string.IsNullOrEmpty(branch.NextStage))
                    FindReachableStages(branch.NextStage, stageMap, reachable);
            }
        }
    }

    private static void ValidateAvatarQuestReferences(World world, List<string> errors)
    {
        // Note: Quests are on AvatarBase, not in WorldDefinitions XML
        // This validates that QuestRefs in quest progress data (if present) reference valid quests
        // Currently no avatars in WorldDefinitions, so this is a placeholder for runtime validation

        // If we later add avatars to world templates, validate them here
        // For now, this is a no-op but provides the extension point
    }

    private static void ValidateFactionReferences(World world, List<string> errors)
    {
        if (world.Gameplay.Factions == null) return;

        foreach (var faction in world.Gameplay.Factions)
        {
            var factionContext = $"Faction '{faction.RefName}'";

            // Validate relationships reference other valid factions
            if (faction.Relationships != null)
            {
                foreach (var relationship in faction.Relationships)
                {
                    if (!string.IsNullOrEmpty(relationship.FactionRef))
                    {
                        ValidateReference(world.FactionsLookup, relationship.FactionRef, factionContext, "Relationship.FactionRef", "Factions", errors);
                    }
                }
            }

        }
    }

    private static void ValidateGameplayHeuristics(World world, List<string> errors)
    {
        // Validate that characters spawned in Sagas have dialogue configured
        // (unless they have the Ambient or BossFight trait - ambient NPCs and pure boss enemies may not need dialogue)
        ValidateSagaCharactersHaveDialogue(world, errors);

        // Track where quest tokens are granted (for duplicate grant detection)
        var tokenGrants = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        // Check dialogue actions for token grants and StartCombat without SetCharacterState
        if (world.Gameplay.DialogueTrees != null)
        {
            foreach (var dialogueTree in world.Gameplay.DialogueTrees)
            {
                if (dialogueTree.Node == null) continue;

                foreach (var node in dialogueTree.Node)
                {
                    if (node.Action == null) continue;

                    var nodeContext = $"DialogueTree '{dialogueTree.RefName}' Node '{node.NodeId}'";

                    // Track token grants
                    foreach (var action in node.Action)
                    {
                        if (action.Type == DialogueActionType.GiveQuestToken && !string.IsNullOrEmpty(action.RefName))
                        {
                            if (!tokenGrants.ContainsKey(action.RefName))
                                tokenGrants[action.RefName] = new List<string>();
                            tokenGrants[action.RefName].Add(nodeContext);
                        }
                    }

                    // Check StartCombat without SetCharacterState Hostile
                    var startCombatActions = node.Action
                        .Where(a => a.Type == DialogueActionType.StartCombat && !string.IsNullOrEmpty(a.CharacterRef))
                        .ToList();

                    foreach (var combatAction in startCombatActions)
                    {
                        var charRef = combatAction.CharacterRef;

                        // Check if there's a SetCharacterState for this character in the same node
                        // Note: DialogueAction doesn't expose State attribute directly, so we just check
                        // if SetCharacterState exists for this character (assuming it sets Hostile)
                        var hasSetCharacterState = node.Action.Any(a =>
                            a.Type == DialogueActionType.SetCharacterState &&
                            a.CharacterRef == charRef);

                        if (!hasSetCharacterState)
                        {
                            // Check if character is already hostile (Hostile trait = 1)
                            var isAlreadyHostile = false;
                            if (world.CharactersLookup.TryGetValue(charRef, out var character))
                            {
                                var hostileTrait = character.Traits?.FirstOrDefault(t => t.Name == CharacterTraitType.Hostile);
                                isAlreadyHostile = hostileTrait?.Value == 1;
                            }

                            if (!isAlreadyHostile)
                            {
                                errors.Add($"{nodeContext}: StartCombat with '{charRef}' without SetCharacterState (and character not already Hostile)");
                            }
                        }
                    }
                }
            }
        }

        // Check character loot for token grants
        if (world.Gameplay.Characters != null)
        {
            foreach (var character in world.Gameplay.Characters)
            {
                if (character.Interactable?.Loot?.QuestTokens != null)
                {
                    foreach (var tokenEntry in character.Interactable.Loot.QuestTokens)
                    {
                        if (!string.IsNullOrEmpty(tokenEntry.QuestTokenRef))
                        {
                            if (!tokenGrants.ContainsKey(tokenEntry.QuestTokenRef))
                                tokenGrants[tokenEntry.QuestTokenRef] = new List<string>();
                            tokenGrants[tokenEntry.QuestTokenRef].Add($"Character '{character.RefName}' Loot");
                        }
                    }
                }

                if (character.Interactable?.GivesQuestTokenRef != null)
                {
                    foreach (var tokenRef in character.Interactable.GivesQuestTokenRef)
                    {
                        if (!string.IsNullOrEmpty(tokenRef))
                        {
                            if (!tokenGrants.ContainsKey(tokenRef))
                                tokenGrants[tokenRef] = new List<string>();
                            tokenGrants[tokenRef].Add($"Character '{character.RefName}' GivesQuestTokenRef");
                        }
                    }
                }
            }
        }

        // Check saga features for token grants
        if (world.Gameplay.SagaFeatures != null)
        {
            foreach (var feature in world.Gameplay.SagaFeatures)
            {
                if (feature.Interactable?.GivesQuestTokenRef != null)
                {
                    foreach (var tokenRef in feature.Interactable.GivesQuestTokenRef)
                    {
                        if (!string.IsNullOrEmpty(tokenRef))
                        {
                            if (!tokenGrants.ContainsKey(tokenRef))
                                tokenGrants[tokenRef] = new List<string>();
                            tokenGrants[tokenRef].Add($"SagaFeature '{feature.RefName}'");
                        }
                    }
                }
            }
        }

        // Report tokens granted in multiple places (informational, not error)
        // This is useful to know but may be intentional (multiple ways to get a token)
        foreach (var (token, sources) in tokenGrants)
        {
            if (sources.Count > 1)
            {
                // Note: This could be a warning rather than error if intentional
                // For now, just log as informational - uncomment to make it an error
                // errors.Add($"QuestToken '{token}' granted in multiple locations: {string.Join(", ", sources)}");
            }
        }
    }

    private static void ValidateDataQuality(World world, List<string> errors)
    {
        // Validate character stats are in reasonable ranges
        if (world.Gameplay.Characters != null)
        {
            foreach (var character in world.Gameplay.Characters)
            {
                if (character.Stats == null) continue;

                var context = $"Character '{character.RefName}'";

                // Determine max stat value based on character type
                // Boss characters and characters with BossFight trait can have boosted stats
                var isBoss = character.Traits?.Any(t => t.Name == CharacterTraitType.BossFight && t.Value == 1) == true;
                var maxStat = isBoss ? 2.0f : 1.0f; // Bosses can have up to 2x normal stats

                ValidateStatRange(character.Stats.Health, "Health", 0, maxStat, context, errors);
                ValidateStatRange(character.Stats.Stamina, "Stamina", 0, maxStat, context, errors);
                ValidateStatRange(character.Stats.Mana, "Mana", 0, maxStat, context, errors);
                ValidateStatRange(character.Stats.Strength, "Strength", 0, maxStat, context, errors);
                ValidateStatRange(character.Stats.Defense, "Defense", 0, maxStat, context, errors);
                ValidateStatRange(character.Stats.Speed, "Speed", 0, maxStat, context, errors);
                ValidateStatRange(character.Stats.Magic, "Magic", 0, maxStat, context, errors);

                if (character.Stats.Credits < 0)
                {
                    errors.Add($"{context}: Credits cannot be negative (was {character.Stats.Credits})");
                }
            }
        }

        // Validate dialogue text is not empty
        if (world.Gameplay.DialogueTrees != null)
        {
            foreach (var dialogueTree in world.Gameplay.DialogueTrees)
            {
                if (dialogueTree.Node == null) continue;

                foreach (var node in dialogueTree.Node)
                {
                    if (node.Text != null)
                    {
                        foreach (var text in node.Text)
                        {
                            if (string.IsNullOrWhiteSpace(text))
                            {
                                errors.Add($"DialogueTree '{dialogueTree.RefName}' Node '{node.NodeId}': Empty Text element");
                            }
                        }
                    }
                }
            }
        }

        // Validate equipment condition is in range (0 to 1)
        if (world.Gameplay.Characters != null)
        {
            foreach (var character in world.Gameplay.Characters)
            {
                ValidateEquipmentCondition(character.Interactable?.Loot, $"Character '{character.RefName}'", errors);
                ValidateEquipmentCondition(character.Capabilities, $"Character '{character.RefName}'", errors);
            }
        }
    }

    private static void ValidateStatRange(float value, string statName, float min, float max, string context, List<string> errors)
    {
        if (value < min || value > max)
        {
            errors.Add($"{context}: {statName} value {value} is outside expected range [{min}, {max}]");
        }
    }

    private static void ValidateEquipmentCondition(ItemCollection? inventory, string context, List<string> errors)
    {
        if (inventory?.Equipment == null) return;

        foreach (var entry in inventory.Equipment)
        {
            if (entry.Condition < 0 || entry.Condition > 1)
            {
                errors.Add($"{context} Equipment '{entry.EquipmentRef}': Condition {entry.Condition} is outside expected range [0, 1]");
            }
        }
    }

    /// <summary>
    /// Validates that characters spawned in Sagas have dialogue configured.
    /// Characters without dialogue cannot be interacted with properly.
    ///
    /// Exceptions:
    /// - Characters with BossFight trait AND Hostile trait (pure combat enemies - but should have BattleDialogue)
    /// - Characters with Friendly trait set to 0 (unfriendly background NPCs)
    /// </summary>
    private static void ValidateSagaCharactersHaveDialogue(World world, List<string> errors)
    {
        if (world.Gameplay.SagaArcs == null) return;

        // Collect all CharacterRefs spawned in any Saga
        var spawnedCharacterRefs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var saga in world.Gameplay.SagaArcs)
        {
            if (saga.Items == null) continue;

            foreach (var item in saga.Items)
            {
                switch (item)
                {
                    case SagaTrigger inlineTrigger:
                        CollectSpawnedCharacters(inlineTrigger, spawnedCharacterRefs);
                        break;

                    case string patternRef:
                        var pattern = world.Gameplay?.SagaTriggerPatterns?
                            .FirstOrDefault(tp => tp.RefName == patternRef);
                        if (pattern?.SagaTrigger != null)
                        {
                            foreach (var trigger in pattern.SagaTrigger)
                            {
                                CollectSpawnedCharacters(trigger, spawnedCharacterRefs);
                            }
                        }
                        break;
                }
            }
        }

        // Validate each spawned character has dialogue (unless exempt)
        foreach (var charRef in spawnedCharacterRefs)
        {
            if (!world.CharactersLookup.TryGetValue(charRef, out var character))
                continue; // Already validated in ValidateCharacterSpawn

            var context = $"Character '{charRef}' (spawned in Saga)";

            // Check for exemptions
            var isBossFight = character.Traits?.Any(t => t.Name == CharacterTraitType.BossFight && t.Value == 1) == true;
            var isHostile = character.Traits?.Any(t => t.Name == CharacterTraitType.Hostile && t.Value == 1) == true;

            // Pure combat enemies (BossFight + Hostile with no dialogue) are allowed
            // BUT they should have BattleDialogue for mid-battle interactions
            if (isBossFight && isHostile)
            {
                // For boss fights, check if they have either regular dialogue OR battle dialogue
                var hasDialogue = !string.IsNullOrEmpty(character.Interactable?.DialogueTreeRef);
                var hasBattleDialogue = character.BattleDialogue?.Length > 0;

                if (!hasDialogue && !hasBattleDialogue)
                {
                    errors.Add($"{context}: BossFight character has no dialogue. Add either DialogueTreeRef (for pre-battle talk) or BattleDialogue (for mid-battle taunts).");
                }
                continue;
            }

            // Hostile-only characters (regular enemies) don't need dialogue - they just attack
            if (isHostile)
                continue;

            // Regular characters must have dialogue
            if (character.Interactable == null)
            {
                errors.Add($"{context}: No Interactable section. Add <Interactable><DialogueTreeRef>...</DialogueTreeRef></Interactable> to allow player interaction.");
            }
            else if (string.IsNullOrEmpty(character.Interactable.DialogueTreeRef))
            {
                errors.Add($"{context}: No DialogueTreeRef. Add <DialogueTreeRef>your_dialogue_tree</DialogueTreeRef> to allow conversation.");
            }
        }
    }

    private static void CollectSpawnedCharacters(SagaTrigger trigger, HashSet<string> characterRefs)
    {
        if (trigger.Spawn == null) return;

        foreach (var spawn in trigger.Spawn)
        {
            // Only collect direct CharacterRef spawns, not archetypes
            // Archetypes generate random characters that may or may not have dialogue
            if (spawn.ItemElementName == ItemChoiceType.CharacterRef && !string.IsNullOrEmpty(spawn.Item))
            {
                characterRefs.Add(spawn.Item);
            }
        }
    }
}