using Ambient.Domain;
using Ambient.Domain.DefinitionExtensions;
using Ambient.Domain.GameLogic.Gameplay.Avatar;

namespace Ambient.Saga.Engine.Domain.Rpg.Sagas.TransactionLog;

/// <summary>
/// State machine that derives current Saga state by replaying transactions.
/// This is the heart of the event sourcing system.
///
/// Key Principles:
/// - State is derived, not stored
/// - Transactions are immutable source of truth
/// - Replay is deterministic and idempotent
/// - Can replay to any point in time
/// </summary>
public class SagaStateMachine
{
    private readonly SagaArc _template;
    private readonly List<SagaTrigger> _expandedSagaTriggers;
    private readonly IWorld _world;

    public SagaStateMachine(SagaArc template, List<SagaTrigger> expandedSagaTriggers, IWorld world)
    {
        _template = template ?? throw new ArgumentNullException(nameof(template));
        _expandedSagaTriggers = expandedSagaTriggers ?? throw new ArgumentNullException(nameof(expandedSagaTriggers));
        _world = world ?? throw new ArgumentNullException(nameof(world));
    }

    /// <summary>
    /// Replays all committed transactions to derive the current state.
    /// </summary>
    public SagaState ReplayToNow(SagaInstance instance)
    {
        return Replay(instance.GetCommittedTransactions());
    }

    /// <summary>
    /// Replays transactions up to a specific point in time.
    /// Useful for debugging, time-travel, "what happened?" investigation.
    /// </summary>
    public SagaState ReplayToTimestamp(SagaInstance instance, DateTime timestamp)
    {
        var transactions = instance.GetCommittedTransactions()
            .Where(t => t.GetCanonicalTimestamp() <= timestamp)
            .ToList();

        return Replay(transactions);
    }

    /// <summary>
    /// Replays transactions up to a specific sequence number.
    /// </summary>
    public SagaState ReplayToSequence(SagaInstance instance, long sequenceNumber)
    {
        var transactions = instance.GetCommittedTransactions()
            .Where(t => t.SequenceNumber <= sequenceNumber)
            .ToList();

        return Replay(transactions);
    }

    /// <summary>
    /// Core replay logic - applies transactions in order to derive state.
    /// </summary>
    public SagaState Replay(List<SagaTransaction> transactions)
    {
        var state = CreateInitialState();

        foreach (var transaction in transactions.OrderBy(t => t.SequenceNumber))
        {
            ApplyTransaction(state, transaction);
        }

        state.TransactionCount = transactions.Count;
        return state;
    }

    /// <summary>
    /// Creates the initial empty state based on the template.
    /// </summary>
    public SagaState CreateInitialState()
    {
        var state = new SagaState
        {
            SagaRef = _template.RefName,
            Status = SagaStatus.Undiscovered
        };

        // Initialize trigger states from template
        foreach (var trigger in _expandedSagaTriggers)
        {
            state.Triggers[trigger.RefName] = new SagaTriggerState
            {
                SagaTriggerRef = trigger.RefName,
                Status = SagaTriggerStatus.Inactive
            };
        }

        return state;
    }

    /// <summary>
    /// Applies a single transaction to the state.
    /// This method must be deterministic and idempotent.
    /// </summary>
    public void ApplyTransaction(SagaState state, SagaTransaction tx)
    {
        switch (tx.Type)
        {
            case SagaTransactionType.SagaDiscovered:
                ApplySagaDiscovered(state, tx);
                break;

            case SagaTransactionType.SagaCompleted:
                ApplySagaCompleted(state, tx);
                break;

            case SagaTransactionType.TriggerActivated:
                ApplyTriggerActivated(state, tx);
                break;

            case SagaTransactionType.TriggerCompleted:
                ApplyTriggerCompleted(state, tx);
                break;

            case SagaTransactionType.CharacterSpawned:
                ApplyCharacterSpawned(state, tx);
                break;

            case SagaTransactionType.CharacterDamaged:
                ApplyCharacterDamaged(state, tx);
                break;

            case SagaTransactionType.CharacterHealed:
                ApplyCharacterHealed(state, tx);
                break;

            case SagaTransactionType.CharacterDefeated:
                ApplyCharacterDefeated(state, tx);
                break;

            case SagaTransactionType.CharacterDespawned:
                ApplyCharacterDespawned(state, tx);
                break;

            case SagaTransactionType.PlayerEntered:
                ApplyPlayerEntered(state, tx);
                break;

            case SagaTransactionType.PlayerExited:
                ApplyPlayerExited(state, tx);
                break;

            case SagaTransactionType.DialogueStarted:
                ApplyDialogueStarted(state, tx);
                break;

            case SagaTransactionType.DialogueNodeVisited:
                ApplyDialogueNodeVisited(state, tx);
                break;

            case SagaTransactionType.DialogueCompleted:
                ApplyDialogueCompleted(state, tx);
                break;

            case SagaTransactionType.TraitAssigned:
                ApplyTraitAssigned(state, tx);
                break;

            case SagaTransactionType.TraitRemoved:
                ApplyTraitRemoved(state, tx);
                break;

            case SagaTransactionType.ReputationChanged:
                ApplyReputationChanged(state, tx);
                break;

            case SagaTransactionType.ItemTraded:
                ApplyItemTraded(state, tx);
                break;

            case SagaTransactionType.LootAwarded:
                ApplyLootAwarded(state, tx);
                break;

            case SagaTransactionType.QuestTokenAwarded:
                ApplyQuestTokenAwarded(state, tx);
                break;

            case SagaTransactionType.QuestAccepted:
                ApplyQuestAccepted(state, tx);
                break;

            case SagaTransactionType.QuestObjectiveCompleted:
                ApplyQuestObjectiveCompleted(state, tx);
                break;

            case SagaTransactionType.QuestStageAdvanced:
                ApplyQuestStageAdvanced(state, tx);
                break;

            case SagaTransactionType.QuestBranchChosen:
                ApplyQuestBranchChosen(state, tx);
                break;

            case SagaTransactionType.QuestCompleted:
                ApplyQuestCompleted(state, tx);
                break;

            case SagaTransactionType.QuestFailed:
                ApplyQuestFailed(state, tx);
                break;

            case SagaTransactionType.QuestAbandoned:
                ApplyQuestAbandoned(state, tx);
                break;

            case SagaTransactionType.QuestProgressed:
                // DEPRECATED: Use QuestObjectiveCompleted instead
                ApplyQuestProgressed(state, tx);
                break;

            case SagaTransactionType.EntityInteracted:
                ApplyEntityInteracted(state, tx);
                break;

            // Voxel mining and building
            case SagaTransactionType.LocationClaimed:
                ApplyLocationClaimed(state, tx);
                break;

            case SagaTransactionType.ToolWearClaimed:
                ApplyToolWearClaimed(state, tx);
                break;

            case SagaTransactionType.MiningSessionClaimed:
                ApplyMiningSessionClaimed(state, tx);
                break;

            case SagaTransactionType.BuildingSessionClaimed:
                ApplyBuildingSessionClaimed(state, tx);
                break;

            case SagaTransactionType.InventorySnapshot:
                ApplyInventorySnapshot(state, tx);
                break;

            // Add more cases as needed
            default:
                // Unknown transaction type - log but don't fail
                // This allows forward compatibility with new transaction types
                break;
        }
    }

    // ===== Transaction Application Methods =====

    private void ApplySagaDiscovered(SagaState state, SagaTransaction tx)
    {
        if (state.Status == SagaStatus.Undiscovered)
        {
            state.Status = SagaStatus.Active;
            state.FirstDiscoveredAt = tx.GetCanonicalTimestamp();
        }

        if (!string.IsNullOrEmpty(tx.AvatarId))
        {
            state.DiscoveredByAvatars.Add(tx.AvatarId);
        }
    }

    private void ApplySagaCompleted(SagaState state, SagaTransaction tx)
    {
        state.Status = SagaStatus.Completed;
        state.CompletedAt = tx.GetCanonicalTimestamp();

        if (!string.IsNullOrEmpty(tx.AvatarId))
        {
            state.CompletedByAvatars.Add(tx.AvatarId);
        }
    }

    private void ApplyTriggerActivated(SagaState state, SagaTransaction tx)
    {
        var triggerRef = tx.GetData<string>("SagaTriggerRef");
        if (string.IsNullOrEmpty(triggerRef) || !state.Triggers.TryGetValue(triggerRef, out var trigger))
            return;

        trigger.Status = SagaTriggerStatus.Active;
        trigger.ActivationCount++;
        trigger.LastActivatedAt = tx.GetCanonicalTimestamp();

        if (trigger.FirstActivatedAt == null)
        {
            trigger.FirstActivatedAt = tx.GetCanonicalTimestamp();
        }

        if (!string.IsNullOrEmpty(tx.AvatarId))
        {
            trigger.TriggeredByAvatars.Add(tx.AvatarId);
        }
    }

    private void ApplyTriggerCompleted(SagaState state, SagaTransaction tx)
    {
        var triggerRef = tx.GetData<string>("SagaTriggerRef");
        if (string.IsNullOrEmpty(triggerRef) || !state.Triggers.TryGetValue(triggerRef, out var trigger))
            return;

        trigger.Status = SagaTriggerStatus.Completed;
        trigger.CompletedAt = tx.GetCanonicalTimestamp();
    }

    private void ApplyCharacterSpawned(SagaState state, SagaTransaction tx)
    {
        var characterInstanceId = tx.GetData<Guid>("CharacterInstanceId");
        var characterRef = tx.GetData<string>("CharacterRef");
        var triggerRef = tx.GetData<string>("SagaTriggerRef");

        // Try new Saga-relative format first (X, Z), fallback to old GPS format (LongitudeX, LatitudeZ)
        var x = tx.TryGetData<double>("X", out var xVal) ? xVal : tx.GetData<double>("LongitudeX");
        var z = tx.TryGetData<double>("Z", out var zVal) ? zVal : tx.GetData<double>("LatitudeZ");
        var spawnHeight = tx.TryGetData<double>("SpawnHeight", out var heightVal) ? heightVal : tx.GetData<double>("Y");

        if (characterInstanceId == Guid.Empty || string.IsNullOrEmpty(characterRef))
            return;

        // Look up character template from world
        var characterTemplate = _world.CharactersLookup.TryGetValue(characterRef, out var template) ? template : null;
        if (characterTemplate == null)
            return; // Can't spawn without valid template

        // Copy stats from template
        CharacterStats? copiedStats = null;
        if (characterTemplate.Stats != null)
        {
            copiedStats = new CharacterStats();
            CharacterStatsCopier.CopyCharacterStats(characterTemplate.Stats, copiedStats);
        }

        // Copy inventory from template
        ItemCollection? copiedInventory = null;
        if (characterTemplate.Capabilities != null)
        {
            copiedInventory = new ItemCollection
            {
                Blocks = characterTemplate.Capabilities.Blocks?.ToArray(),
                Tools = characterTemplate.Capabilities.Tools?.ToArray(),
                Equipment = characterTemplate.Capabilities.Equipment?.ToArray(),
                Consumables = characterTemplate.Capabilities.Consumables?.ToArray(),
                Spells = characterTemplate.Capabilities.Spells?.ToArray(),
                BuildingMaterials = characterTemplate.Capabilities.BuildingMaterials?.ToArray(),
                QuestTokens = characterTemplate.Capabilities.QuestTokens?.ToArray()
            };
        }

        var characterState = new CharacterState
        {
            CharacterInstanceId = characterInstanceId,
            CharacterRef = characterRef!,
            SpawnedByTriggerRef = triggerRef ?? string.Empty,
            IsSpawned = true,
            IsAlive = true,
            CurrentHealth = 1.0,
            SpawnedAt = tx.GetCanonicalTimestamp(),
            CurrentStats = copiedStats,
            CurrentInventory = copiedInventory,
            CombatProfile = null, // TODO: Copy from template if needed
            CurrentLatitudeZ = z,           // Saga-relative Z (using old field name for now)
            CurrentLongitudeX = x,          // Saga-relative X (using old field name for now)
            CurrentY = spawnHeight          // Height (terrain-adjusted by game)
        };

        // Copy traits from character template definition (e.g., Hostile, Friendly, BossFight)
        if (characterTemplate.Traits != null)
        {
            foreach (var trait in characterTemplate.Traits)
            {
                var traitName = trait.Name.ToString();
                // ValueSpecified indicates if a numeric value was provided in XML
                // When ValueSpecified is false, treat as boolean flag (null value)
                characterState.Traits[traitName] = trait.ValueSpecified ? trait.Value : null;
            }
        }

        // Copy any existing traits from the global trait list for this character template
        // (traits assigned through dialogue or game events override template traits)
        if (state.CharacterTraits.TryGetValue(characterRef!, out var existingTraits))
        {
            foreach (var traitName in existingTraits)
            {
                characterState.Traits[traitName] = null; // Boolean flag trait
            }
        }

        state.Characters[characterInstanceId.ToString()] = characterState;

        // Add to trigger's spawned characters
        if (!string.IsNullOrEmpty(triggerRef) && state.Triggers.TryGetValue(triggerRef, out var trigger))
        {
            trigger.SpawnedCharacters[characterInstanceId.ToString()] = characterState;
        }
    }

    private void ApplyCharacterDamaged(SagaState state, SagaTransaction tx)
    {
        var characterInstanceId = tx.GetData<Guid>("CharacterInstanceId");
        var damage = tx.GetData<double>("Damage");

        if (characterInstanceId == Guid.Empty || !state.Characters.TryGetValue(characterInstanceId.ToString(), out var character))
            return;

        character.CurrentHealth = Math.Max(0, character.CurrentHealth - damage);

        // Track damage by player
        if (!string.IsNullOrEmpty(tx.AvatarId))
        {
            if (!character.DamageByPlayer.ContainsKey(tx.AvatarId))
            {
                character.DamageByPlayer[tx.AvatarId] = 0;
            }
            character.DamageByPlayer[tx.AvatarId] += damage;
        }

        // Auto-defeat if health reaches 0
        if (character.CurrentHealth <= 0 && character.IsAlive)
        {
            character.IsAlive = false;
            character.DefeatedAt = tx.GetCanonicalTimestamp();
        }
    }

    private void ApplyCharacterHealed(SagaState state, SagaTransaction tx)
    {
        var characterInstanceId = tx.GetData<Guid>("CharacterInstanceId");
        var healing = tx.GetData<double>("Healing");

        if (characterInstanceId == Guid.Empty || !state.Characters.TryGetValue(characterInstanceId.ToString(), out var character))
            return;

        character.CurrentHealth = Math.Min(1.0, character.CurrentHealth + healing);
    }

    private void ApplyCharacterDefeated(SagaState state, SagaTransaction tx)
    {
        var characterInstanceId = tx.GetData<Guid>("CharacterInstanceId");

        if (characterInstanceId == Guid.Empty || !state.Characters.TryGetValue(characterInstanceId.ToString(), out var character))
            return;

        character.IsAlive = false;
        character.CurrentHealth = 0;
        character.DefeatedAt = tx.GetCanonicalTimestamp();
    }

    private void ApplyCharacterDespawned(SagaState state, SagaTransaction tx)
    {
        var characterInstanceId = tx.GetData<Guid>("CharacterInstanceId");

        if (characterInstanceId == Guid.Empty || !state.Characters.TryGetValue(characterInstanceId.ToString(), out var character))
            return;

        character.IsSpawned = false;
        character.DespawnedAt = tx.GetCanonicalTimestamp();
    }

    private void ApplyPlayerEntered(SagaState state, SagaTransaction tx)
    {
        // Player entered Saga - may trigger discovery if first time
        if (state.Status == SagaStatus.Undiscovered && !string.IsNullOrEmpty(tx.AvatarId))
        {
            state.Status = SagaStatus.Active;
            state.FirstDiscoveredAt = tx.GetCanonicalTimestamp();
            state.DiscoveredByAvatars.Add(tx.AvatarId);
        }
    }

    private void ApplyPlayerExited(SagaState state, SagaTransaction tx)
    {
        // Player exited Saga - could trigger cleanup, despawns, etc.
        // Implementation depends on game design
    }

    private void ApplyDialogueStarted(SagaState state, SagaTransaction tx)
    {
        // Dialogue started - currently just for tracking/achievements
        // Could add state.CurrentDialogues[avatarId] = dialogueTreeRef if needed
    }

    private void ApplyDialogueNodeVisited(SagaState state, SagaTransaction tx)
    {
        // CRITICAL: This method ensures idempotent replay
        // Dialogue rewards (items, traits, quest tokens) only awarded on FIRST visit

        var avatarId = tx.AvatarId ?? string.Empty;
        var characterRef = tx.GetData<string>("CharacterRef") ?? string.Empty;
        var dialogueTreeRef = tx.GetData<string>("DialogueTreeRef") ?? string.Empty;
        var nodeId = tx.GetData<string>("DialogueNodeId") ?? string.Empty;

        if (string.IsNullOrEmpty(avatarId) || string.IsNullOrEmpty(characterRef) || string.IsNullOrEmpty(nodeId))
            return;

        // Create unique visit key
        var visitKey = $"{avatarId}_{characterRef}_{nodeId}";

        if (!state.DialogueNodeVisits.TryGetValue(visitKey, out var visit))
        {
            // FIRST VISIT - Record all actions that should be performed
            visit = new DialogueVisit
            {
                VisitKey = visitKey,
                AvatarId = avatarId,
                CharacterRef = characterRef,
                DialogueTreeRef = dialogueTreeRef,
                DialogueNodeId = nodeId,
                FirstVisitedAt = tx.GetCanonicalTimestamp(),
                VisitCount = 1,
                ItemsAwarded = tx.GetData<string>("ItemsAwarded") ?? string.Empty,
                TraitsAssigned = tx.GetData<string>("TraitsAssigned") ?? string.Empty,
                QuestTokensAwarded = tx.GetData<string>("QuestTokensAwarded") ?? string.Empty,
                CurrencyTransferred = tx.GetData<int>("CurrencyTransferred")
            };

            state.DialogueNodeVisits[visitKey] = visit;

            // NOTE: Actual item/trait/token application happens in separate transactions
            // This just records the INTENT for idempotency checking
        }
        else
        {
            // SUBSEQUENT VISIT - Increment count but DO NOT re-award items
            visit.VisitCount++;
            visit.LastVisitedAt = tx.GetCanonicalTimestamp();
        }
    }

    private void ApplyDialogueCompleted(SagaState state, SagaTransaction tx)
    {
        // Dialogue completed - currently just for tracking/achievements
        // Could add completion stats if needed
    }

    private void ApplyTraitAssigned(SagaState state, SagaTransaction tx)
    {
        var characterRef = tx.GetData<string>("CharacterRef");
        var traitType = tx.GetData<string>("TraitType");
        var traitValue = tx.TryGetData<int?>("TraitValue", out var val) ? val : null;

        if (string.IsNullOrEmpty(characterRef) || string.IsNullOrEmpty(traitType))
            return;

        // Get or create trait list for this character template (for backward compat and new spawns)
        if (!state.CharacterTraits.TryGetValue(characterRef, out var traits))
        {
            traits = new List<string>();
            state.CharacterTraits[characterRef] = traits;
        }

        // Add trait if not already present (idempotent)
        if (!traits.Contains(traitType))
        {
            traits.Add(traitType);
        }

        // Also update all live instances of this character with the new trait
        foreach (var characterState in state.Characters.Values)
        {
            if (characterState.CharacterRef == characterRef && characterState.IsAlive)
            {
                characterState.Traits[traitType] = traitValue;
            }
        }
    }

    private void ApplyTraitRemoved(SagaState state, SagaTransaction tx)
    {
        var characterRef = tx.GetData<string>("CharacterRef");
        var traitType = tx.GetData<string>("TraitType");

        if (string.IsNullOrEmpty(characterRef) || string.IsNullOrEmpty(traitType))
            return;

        // Remove trait from template list (for backward compat and new spawns)
        if (state.CharacterTraits.TryGetValue(characterRef, out var traits))
        {
            traits.Remove(traitType);
        }

        // Also remove from all live instances of this character
        foreach (var characterState in state.Characters.Values)
        {
            if (characterState.CharacterRef == characterRef && characterState.IsAlive)
            {
                characterState.Traits.Remove(traitType);
            }
        }
    }

    private void ApplyReputationChanged(SagaState state, SagaTransaction tx)
    {
        var factionRef = tx.GetData<string>("FactionRef");
        var reputationChange = tx.GetData<int>("ReputationChange");

        if (string.IsNullOrEmpty(factionRef))
            return;

        // Initialize reputation for this faction if not present
        if (!state.FactionReputation.ContainsKey(factionRef))
            state.FactionReputation[factionRef] = 0;

        // Apply reputation change
        state.FactionReputation[factionRef] += reputationChange;

        // Note: Spillover is calculated and logged as separate transactions
        // by the ChangeReputation dialogue action handler, so we don't
        // need to recalculate spillover here during replay.
    }

    private void ApplyItemTraded(SagaState state, SagaTransaction tx)
    {
        // Item trade completed - currently just for tracking/achievements
        // Could track trade history if needed:
        // - AvatarId (who traded)
        // - CharacterRef (who they traded with)
        // - ItemRef (what was traded)
        // - Quantity
        // - Direction (buy/sell)
    }

    private void ApplyLootAwarded(SagaState state, SagaTransaction tx)
    {
        var characterInstanceId = tx.GetData<Guid>("CharacterInstanceId");

        if (characterInstanceId == Guid.Empty || !state.Characters.TryGetValue(characterInstanceId.ToString(), out var character))
            return;

        // Clear inventory and mark as looted
        character.CurrentInventory = null;
        character.HasBeenLooted = true;
        character.LootedAt = tx.GetCanonicalTimestamp();

        System.Diagnostics.Debug.WriteLine($"[Replay] Character {characterInstanceId} looted at {tx.GetCanonicalTimestamp()}");
    }

    private void ApplyQuestTokenAwarded(SagaState state, SagaTransaction tx)
    {
        // Quest token awarded - currently just for tracking/achievements
        // Could track quest token collection if needed
    }

    private void ApplyQuestAccepted(SagaState state, SagaTransaction tx)
    {
        var questRef = tx.GetData<string>("QuestRef");
        if (string.IsNullOrEmpty(questRef))
            return;

        // Don't accept if already accepted or completed
        if (state.ActiveQuests.ContainsKey(questRef) || state.CompletedQuests.Contains(questRef))
            return;

        // Look up quest template from world
        var quest = _world.TryGetQuestByRefName(questRef);
        if (quest == null)
            return;

        // Get start stage
        var startStageRef = quest.Stages?.StartStage;
        if (string.IsNullOrEmpty(startStageRef))
            return;

        // Add to active quests with new multi-stage structure
        var questState = new QuestState
        {
            QuestRef = questRef,
            DisplayName = quest.DisplayName,
            CurrentStage = startStageRef,
            AcceptedAt = tx.GetCanonicalTimestamp(),
            QuestGiverRef = tx.GetData<string>("QuestGiverRef") ?? string.Empty,
            SagaRef = tx.GetData<string>("SagaArcRef") ?? state.SagaRef
        };

        state.ActiveQuests[questRef] = questState;
    }

    private void ApplyQuestObjectiveCompleted(SagaState state, SagaTransaction tx)
    {
        var questRef = tx.GetData<string>("QuestRef");
        var stageRef = tx.GetData<string>("StageRef");
        var objectiveRef = tx.GetData<string>("ObjectiveRef");

        if (string.IsNullOrEmpty(questRef) || string.IsNullOrEmpty(stageRef) || string.IsNullOrEmpty(objectiveRef))
            return;

        // Only apply if quest is active
        if (!state.ActiveQuests.TryGetValue(questRef, out var questState))
            return;

        // Mark objective as completed
        if (!questState.CompletedObjectives.ContainsKey(stageRef))
            questState.CompletedObjectives[stageRef] = new HashSet<string>();

        questState.CompletedObjectives[stageRef].Add(objectiveRef);

        // Update objective progress value
        if (tx.TryGetData<int>("CurrentValue", out var currentValue))
        {
            questState.ObjectiveProgress[objectiveRef] = currentValue;
        }
    }

    private void ApplyQuestStageAdvanced(SagaState state, SagaTransaction tx)
    {
        var questRef = tx.GetData<string>("QuestRef");
        var nextStageRef = tx.GetData<string>("NextStage");

        if (string.IsNullOrEmpty(questRef))
            return;

        // Only apply if quest is active
        if (!state.ActiveQuests.TryGetValue(questRef, out var questState))
            return;

        // Advance to next stage (or null if quest complete)
        questState.CurrentStage = nextStageRef ?? string.Empty;
    }

    private void ApplyQuestBranchChosen(SagaState state, SagaTransaction tx)
    {
        var questRef = tx.GetData<string>("QuestRef");
        var branchRef = tx.GetData<string>("BranchRef");

        if (string.IsNullOrEmpty(questRef) || string.IsNullOrEmpty(branchRef))
            return;

        // Only apply if quest is active
        if (!state.ActiveQuests.TryGetValue(questRef, out var questState))
            return;

        // Record chosen branch (locks out other branches)
        questState.ChosenBranch = branchRef;
    }

    private void ApplyQuestCompleted(SagaState state, SagaTransaction tx)
    {
        var questRef = tx.GetData<string>("QuestRef");
        if (string.IsNullOrEmpty(questRef))
            return;

        // Only apply if quest is active
        if (!state.ActiveQuests.TryGetValue(questRef, out var questState))
            return;

        // Mark as successfully completed
        questState.IsSuccess = true;
        questState.CompletedAt = tx.GetCanonicalTimestamp();

        // Remove from active quests and add to completed
        state.ActiveQuests.Remove(questRef);
        state.CompletedQuests.Add(questRef);
    }

    private void ApplyQuestFailed(SagaState state, SagaTransaction tx)
    {
        var questRef = tx.GetData<string>("QuestRef");
        if (string.IsNullOrEmpty(questRef))
            return;

        // Only apply if quest is active
        if (!state.ActiveQuests.TryGetValue(questRef, out var questState))
            return;

        // Mark as failed
        questState.IsFailed = true;
        questState.FailureReason = tx.GetData<string>("FailureReason");
        questState.CompletedAt = tx.GetCanonicalTimestamp();

        // Remove from active quests (not added to completed)
        state.ActiveQuests.Remove(questRef);
    }

    private void ApplyQuestAbandoned(SagaState state, SagaTransaction tx)
    {
        var questRef = tx.GetData<string>("QuestRef");
        if (string.IsNullOrEmpty(questRef))
            return;

        // Simply remove from active quests (not added to completed)
        state.ActiveQuests.Remove(questRef);
    }

    private void ApplyQuestProgressed(SagaState state, SagaTransaction tx)
    {
        // DEPRECATED: This is for backward compatibility only
        // New quests should use QuestObjectiveCompleted instead
        var questRef = tx.GetData<string>("QuestRef");
        if (string.IsNullOrEmpty(questRef))
            return;

        // Only progress if quest is active
        if (!state.ActiveQuests.TryGetValue(questRef, out var questState))
            return;

        // For old simple quests, just update a generic progress counter
        var progressAmount = tx.TryGetData<int>("ProgressAmount", out var amount) ? amount : 1;
        var currentProgress = questState.ObjectiveProgress.GetValueOrDefault("_legacy_", 0);
        questState.ObjectiveProgress["_legacy_"] = currentProgress + progressAmount;
    }

    private void ApplyEntityInteracted(SagaState state, SagaTransaction tx)
    {
        var featureRef = tx.GetData<string>("FeatureRef");
        if (string.IsNullOrEmpty(featureRef))
            return;

        // Get or create feature interaction state
        if (!state.FeatureInteractions.TryGetValue(featureRef, out var featureState))
        {
            featureState = new FeatureInteractionState
            {
                FeatureRef = featureRef
            };
            state.FeatureInteractions[featureRef] = featureState;
        }

        // Update total interaction count
        featureState.TotalInteractionCount++;
        featureState.LastInteractedAt = tx.GetCanonicalTimestamp();

        if (featureState.FirstInteractedAt == null)
        {
            featureState.FirstInteractedAt = tx.GetCanonicalTimestamp();
        }

        // Update per-avatar tracking
        if (!string.IsNullOrEmpty(tx.AvatarId))
        {
            if (!featureState.AvatarInteractions.TryGetValue(tx.AvatarId, out var avatarInteraction))
            {
                avatarInteraction = new AvatarFeatureInteraction
                {
                    AvatarId = tx.AvatarId,
                    FirstInteractedAt = tx.GetCanonicalTimestamp()
                };
                featureState.AvatarInteractions[tx.AvatarId] = avatarInteraction;
            }

            avatarInteraction.InteractionCount++;
            avatarInteraction.LastInteractedAt = tx.GetCanonicalTimestamp();
        }
    }

    // ===== Voxel Transaction Application Methods =====

    private void ApplyLocationClaimed(SagaState state, SagaTransaction tx)
    {
        // Location claims are primarily for anti-cheat analytics
        // The transaction log itself is the source of truth for position history
        // We can optionally track latest position in state for queries if needed
        // For now, just track that location was claimed (logged)
    }

    private void ApplyToolWearClaimed(SagaState state, SagaTransaction tx)
    {
        // Tool wear claims are primarily for anti-cheat analytics
        // The transaction log itself is the source of truth for tool usage history
        // Actual tool condition is managed by the avatar entity, not Saga state
    }

    private void ApplyMiningSessionClaimed(SagaState state, SagaTransaction tx)
    {
        // Mining session claims are primarily for anti-cheat analytics
        // The transaction log itself is the source of truth for mining history
        // Block inventory is managed by the avatar entity, not Saga state

        // We could optionally track total blocks mined for statistics:
        // state.TotalBlocksMined += tx.GetData<int>("BlockCount");
    }

    private void ApplyBuildingSessionClaimed(SagaState state, SagaTransaction tx)
    {
        // Building session claims are primarily for anti-cheat analytics
        // The transaction log itself is the source of truth for building history
        // Block placement is managed by the chunk server, not Saga state

        // We could optionally track total blocks placed for statistics:
        // state.TotalBlocksPlaced += tx.GetData<int>("BlockCount");
    }

    private void ApplyInventorySnapshot(SagaState state, SagaTransaction tx)
    {
        // Inventory snapshots are validation baselines for anti-cheat
        // The transaction log itself is the source of truth for inventory history
        // Actual inventory is managed by the avatar entity, not Saga state

        // These snapshots allow retrospective analysis:
        // "Did player's inventory grow impossibly fast between snapshots?"
    }
}
