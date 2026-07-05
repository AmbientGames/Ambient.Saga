using System.Text.Json;
using System.Text.Json.Serialization;
using Ambient.Domain;
using Ambient.Domain.Contracts;
using Ambient.Domain.Extensions;
using Ambient.Domain.GameLogic.Gameplay.Avatar;
using Ambient.Saga.Engine.Domain;
using Microsoft.Extensions.Logging;

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
    private readonly ILogger<SagaStateMachine>? _logger;
    private readonly ISagaMetrics _metrics;
    private readonly IExtensionTypeRegistry _extensionRegistry;
    private readonly UnknownTransactionPolicy _unknownPolicy;

    public SagaStateMachine(
        SagaArc template,
        List<SagaTrigger> expandedSagaTriggers,
        IWorld world,
        ILogger<SagaStateMachine>? logger = null,
        ISagaMetrics? metrics = null,
        IExtensionTypeRegistry? extensionRegistry = null,
        UnknownTransactionPolicy unknownPolicy = UnknownTransactionPolicy.Quarantine)
    {
        _template = template ?? throw new ArgumentNullException(nameof(template));
        _expandedSagaTriggers = expandedSagaTriggers ?? throw new ArgumentNullException(nameof(expandedSagaTriggers));
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _logger = logger;
        _metrics = metrics ?? NullSagaMetrics.Instance;
        // Permissive default: any self-identifying extension replays as host-layer
        // data without quarantine noise; pass an explicit registry for strict checking
        _extensionRegistry = extensionRegistry ?? PermissiveExtensionTypeRegistry.Instance;
        _unknownPolicy = unknownPolicy;
    }

    /// <summary>
    /// Replays all committed transactions to derive the current state.
    /// Returns cached state if the transaction log hasn't changed since last replay.
    /// </summary>
    public SagaState ReplayToNow(SagaInstance instance)
    {
        if (!instance.IsDirty && instance.CachedState != null)
            return instance.CachedState;

        var committed = instance.GetCommittedTransactions();
        var state = Replay(committed);
        instance.CachedState = state;
        instance.CachedAtSequenceNumber = committed.Count > 0
            ? committed[^1].SequenceNumber // Already ordered by GetCommittedTransactions
            : 0;
        instance.IsDirty = false;
        return state;
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
    /// If a StateSnapshot transaction exists, resumes from it instead of replaying from the beginning.
    /// Falls back to full replay if the snapshot can't be deserialized.
    /// </summary>
    public SagaState Replay(List<SagaTransaction> transactions)
    {
        var ordered = transactions.OrderBy(t => t.SequenceNumber).ToList();

        // TransactionReversed compensations need the referenced transaction's data
        // to compute the inverse fold
        var transactionsById = new Dictionary<Guid, SagaTransaction>(ordered.Count);
        foreach (var tx in ordered)
        {
            transactionsById[tx.TransactionId] = tx;
        }

        // Find the latest snapshot to avoid replaying the entire log
        SagaState? state = null;
        var replayFrom = 0;

        for (var i = ordered.Count - 1; i >= 0; i--)
        {
            if (ordered[i].Type == SagaTransactionType.StateSnapshot
                && TryDeserializeSnapshot(ordered[i], out var snapshotState))
            {
                state = snapshotState;
                replayFrom = i + 1; // Apply only transactions after the snapshot
                break;
            }
        }

        state ??= CreateInitialState();

        // A snapshot freezes the trigger set as of its creation. Merge in template
        // triggers added since (fresh initial state), otherwise their
        // TriggerActivated transactions are silently discarded on resume and
        // content updates stay invisible to migrated saves (audit C4).
        foreach (var trigger in _expandedSagaTriggers)
        {
            if (!state.Triggers.ContainsKey(trigger.RefName))
            {
                state.Triggers[trigger.RefName] = new SagaTriggerState
                {
                    SagaTriggerRef = trigger.RefName,
                    Status = SagaTriggerStatus.Inactive
                };
            }
        }

        for (var i = replayFrom; i < ordered.Count; i++)
        {
            ApplyTransaction(state, ordered[i], transactionsById);
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
        ApplyTransaction(state, tx, transactionsById: null);
    }

    /// <summary>
    /// Applies a single transaction, with the full log available by id so
    /// TransactionReversed compensations can look up the transaction they undo.
    /// </summary>
    private void ApplyTransaction(SagaState state, SagaTransaction tx, IReadOnlyDictionary<Guid, SagaTransaction>? transactionsById)
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

            case SagaTransactionType.AvatarEntered:
                ApplyAvatarEntered(state, tx);
                break;

            case SagaTransactionType.AvatarExited:
                ApplyAvatarExited(state, tx);
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

            case SagaTransactionType.EntityInteracted:
                ApplyEntityInteracted(state, tx);
                break;

            case SagaTransactionType.TransactionReversed:
                ApplyTransactionReversed(state, tx, transactionsById);
                break;

            // Snapshots are consumed by Replay(), not applied individually
            case SagaTransactionType.StateSnapshot:
                break;

            // Extension types are handled by domain-specific appliers (e.g., Ambient.Core)
            // The transaction log itself is the source of truth for extension data
            case SagaTransactionType.Extension:
                ApplyExtension(state, tx);
                break;

            // Add more cases as needed
            default:
                if (StatelessTransactionTypes.Contains(tx.Type))
                {
                    // Legitimately emitted but carries no SagaState fold: battle turns are
                    // read directly from the log by the battle handlers, equipment/
                    // consumable/teleport events update the avatar entity, etc.
                    // Routing these through the unknown handler saturated the anti-drift
                    // signal with false positives — and would crash every replay if the
                    // policy were ever flipped to Throw.
                    break;
                }
                HandleUnknownTransactionType(tx);
                break;
        }
    }

    /// <summary>
    /// Types the engine emits that intentionally have no SagaState application —
    /// their consumers read the log directly or mutate the avatar entity.
    /// </summary>
    private static readonly HashSet<SagaTransactionType> StatelessTransactionTypes = new()
    {
        SagaTransactionType.BattleStarted,
        SagaTransactionType.BattleTurnExecuted,
        SagaTransactionType.BattleEnded,
        SagaTransactionType.EquipmentChanged,
        SagaTransactionType.ConsumableUsed,
        SagaTransactionType.ToolSharpened,
        SagaTransactionType.AvatarTeleported,
        SagaTransactionType.PartyMemberJoined,
        SagaTransactionType.PartyMemberLeft,
        SagaTransactionType.AffinityGranted,
        SagaTransactionType.EffectApplied,
        SagaTransactionType.CurrencyChanged, // read by QuestProgressEvaluator (CurrencyCollected objectives)
        SagaTransactionType.LootAwarded, // retired feature (corpse looting removed 2026-07-04); value reserved so stray historical transactions don't trip the unknown-type quarantine
    };

    private void HandleUnknownTransactionType(SagaTransaction tx)
    {
        var typeValue = (int)tx.Type;
        _metrics.IncrementUnknownTransactionType(typeValue);

        if (_unknownPolicy == UnknownTransactionPolicy.Throw)
        {
            throw new InvalidOperationException(
                $"Unknown SagaTransactionType value {typeValue} encountered during replay " +
                $"(TransactionId={tx.TransactionId}, SequenceNumber={tx.SequenceNumber}).");
        }

        _logger?.LogWarning(
            "Unknown SagaTransactionType value {TypeValue} encountered during replay; quarantined. TransactionId={TransactionId} SequenceNumber={SequenceNumber}",
            typeValue,
            tx.TransactionId,
            tx.SequenceNumber);
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
        var triggerRef = tx.GetData<string>(TransactionDataKeys.SagaTriggerRef);
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
        var triggerRef = tx.GetData<string>(TransactionDataKeys.SagaTriggerRef);
        if (string.IsNullOrEmpty(triggerRef) || !state.Triggers.TryGetValue(triggerRef, out var trigger))
            return;

        trigger.Status = SagaTriggerStatus.Completed;
        trigger.CompletedAt = tx.GetCanonicalTimestamp();
    }

    private void ApplyCharacterSpawned(SagaState state, SagaTransaction tx)
    {
        var characterInstanceId = tx.GetData<Guid>(TransactionDataKeys.CharacterInstanceId);
        var characterRef = tx.GetData<string>(TransactionDataKeys.CharacterRef);
        var triggerRef = tx.GetData<string>(TransactionDataKeys.SagaTriggerRef);

        // Try new Saga-relative format first (X, Z), fallback to old GPS format (LongitudeX, LatitudeZ)
        var x = tx.TryGetData<double>(TransactionDataKeys.X, out var xVal) ? xVal : tx.GetData<double>(TransactionDataKeys.Longitude);
        var z = tx.TryGetData<double>(TransactionDataKeys.Z, out var zVal) ? zVal : tx.GetData<double>(TransactionDataKeys.Latitude);
        var spawnHeight = tx.TryGetData<double>(TransactionDataKeys.SpawnHeight, out var heightVal) ? heightVal : tx.GetData<double>(TransactionDataKeys.Y);

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

        // Deep clone inventory from template - each item is cloned to avoid shared state
        ItemCollection? copiedInventory = null;
        if (characterTemplate.Capabilities != null)
        {
            copiedInventory = new ItemCollection
            {
                Blocks = characterTemplate.Capabilities.Blocks?.Select(b => new BlockEntry
                    { BlockRef = b.BlockRef, Quantity = b.Quantity }).ToArray(),
                Tools = characterTemplate.Capabilities.Tools?.Select(t => new ToolEntry
                    { ToolRef = t.ToolRef, Condition = t.Condition }).ToArray(),
                Equipment = characterTemplate.Capabilities.Equipment?.Select(e => new EquipmentEntry
                    { EquipmentRef = e.EquipmentRef, Condition = e.Condition }).ToArray(),
                Consumables = characterTemplate.Capabilities.Consumables?.Select(c => new ConsumableEntry
                    { ConsumableRef = c.ConsumableRef, Quantity = c.Quantity }).ToArray(),
                Spells = characterTemplate.Capabilities.Spells?.Select(s => new SpellEntry
                    { SpellRef = s.SpellRef, Condition = s.Condition }).ToArray(),
                BuildingMaterials = characterTemplate.Capabilities.BuildingMaterials?.Select(m => new BuildingMaterialEntry
                    { BuildingMaterialRef = m.BuildingMaterialRef, Quantity = m.Quantity }).ToArray(),
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
        // (traits assigned through dialogue or game events override template traits).
        // Disengaged is deliberately NOT inherited: it is per-encounter combat state
        // (assigned when the avatar successfully flees) and a fresh spawn is a fresh
        // encounter — respawned instances of a fled-from enemy assault again.
        if (state.CharacterTraits.TryGetValue(characterRef!, out var existingTraits))
        {
            foreach (var traitName in existingTraits)
            {
                if (traitName == "Disengaged")
                    continue;

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
        var characterInstanceId = tx.GetData<Guid>(TransactionDataKeys.CharacterInstanceId);
        var damage = tx.GetData<double>(TransactionDataKeys.Damage);

        if (characterInstanceId == Guid.Empty || !state.Characters.TryGetValue(characterInstanceId.ToString(), out var character))
            return;

        character.CurrentHealth = Math.Max(0, character.CurrentHealth - damage);

        // Track damage by avatar
        if (!string.IsNullOrEmpty(tx.AvatarId))
        {
            if (!character.DamageByAvatar.ContainsKey(tx.AvatarId))
            {
                character.DamageByAvatar[tx.AvatarId] = 0;
            }
            character.DamageByAvatar[tx.AvatarId] += damage;
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
        var characterInstanceId = tx.GetData<Guid>(TransactionDataKeys.CharacterInstanceId);
        var healing = tx.GetData<double>(TransactionDataKeys.Healing);

        if (characterInstanceId == Guid.Empty || !state.Characters.TryGetValue(characterInstanceId.ToString(), out var character))
            return;

        character.CurrentHealth = Math.Min(1.0, character.CurrentHealth + healing);
    }

    private void ApplyCharacterDefeated(SagaState state, SagaTransaction tx)
    {
        var characterInstanceId = tx.GetData<Guid>(TransactionDataKeys.CharacterInstanceId);

        if (characterInstanceId == Guid.Empty || !state.Characters.TryGetValue(characterInstanceId.ToString(), out var character))
            return;

        character.IsAlive = false;
        character.CurrentHealth = 0;
        character.DefeatedAt = tx.GetCanonicalTimestamp();
    }

    private void ApplyCharacterDespawned(SagaState state, SagaTransaction tx)
    {
        var characterInstanceId = tx.GetData<Guid>(TransactionDataKeys.CharacterInstanceId);

        if (characterInstanceId == Guid.Empty || !state.Characters.TryGetValue(characterInstanceId.ToString(), out var character))
            return;

        character.IsSpawned = false;
        character.DespawnedAt = tx.GetCanonicalTimestamp();
    }

    private void ApplyAvatarEntered(SagaState state, SagaTransaction tx)
    {
        // Avatar entered Saga - may trigger discovery if first time
        if (state.Status == SagaStatus.Undiscovered && !string.IsNullOrEmpty(tx.AvatarId))
        {
            state.Status = SagaStatus.Active;
            state.FirstDiscoveredAt = tx.GetCanonicalTimestamp();
            state.DiscoveredByAvatars.Add(tx.AvatarId);
        }

        // Mark the trigger's ring as occupied by this avatar. Occupancy is what
        // gates AvatarExited emission (transition-based, audit B9).
        var triggerRef = tx.GetData<string>(TransactionDataKeys.TriggerRef);
        if (!string.IsNullOrEmpty(triggerRef) && !string.IsNullOrEmpty(tx.AvatarId)
            && state.Triggers.TryGetValue(triggerRef, out var trigger))
        {
            trigger.OccupyingAvatars.Add(tx.AvatarId);
        }
    }

    private void ApplyAvatarExited(SagaState state, SagaTransaction tx)
    {
        // The trigger's ring is no longer occupied by this avatar. Spawned
        // characters stay where they are — encounters persist across visits.
        var triggerRef = tx.GetData<string>(TransactionDataKeys.TriggerRef);
        if (!string.IsNullOrEmpty(triggerRef) && !string.IsNullOrEmpty(tx.AvatarId)
            && state.Triggers.TryGetValue(triggerRef, out var trigger))
        {
            trigger.OccupyingAvatars.Remove(tx.AvatarId);
        }
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
        var characterRef = tx.GetData<string>(TransactionDataKeys.CharacterRef) ?? string.Empty;
        var dialogueTreeRef = tx.GetData<string>(TransactionDataKeys.DialogueTreeRef) ?? string.Empty;
        var nodeId = tx.GetData<string>(TransactionDataKeys.DialogueNodeId) ?? string.Empty;

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
                ItemsAwarded = tx.GetData<string>(TransactionDataKeys.ItemsAwarded) ?? string.Empty,
                TraitsAssigned = tx.GetData<string>(TransactionDataKeys.TraitsAssigned) ?? string.Empty,
                QuestTokensAwarded = tx.GetData<string>(TransactionDataKeys.QuestTokensAwarded) ?? string.Empty,
                CurrencyTransferred = tx.GetData<int>(TransactionDataKeys.CurrencyTransferred)
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
        var characterRef = tx.GetData<string>(TransactionDataKeys.CharacterRef);
        var traitType = tx.GetData<string>(TransactionDataKeys.TraitType);
        var traitValue = tx.TryGetData<int?>(TransactionDataKeys.TraitValue, out var val) ? val : null;

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
        var characterRef = tx.GetData<string>(TransactionDataKeys.CharacterRef);
        var traitType = tx.GetData<string>(TransactionDataKeys.TraitType);

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
        var factionRef = tx.GetData<string>(TransactionDataKeys.FactionRef);
        var reputationChange = tx.GetData<int>(TransactionDataKeys.Amount);

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

    private void ApplyItemTraded(SagaState state, SagaTransaction tx, bool invert = false)
    {
        var characterInstanceId = tx.GetData<Guid>(TransactionDataKeys.CharacterInstanceId);
        var itemRef = tx.GetData<string>(TransactionDataKeys.ItemRef);
        var quantity = tx.GetData<int>(TransactionDataKeys.Quantity);
        var isBuying = tx.GetData<bool>(TransactionDataKeys.IsBuying);

        if (characterInstanceId == Guid.Empty || string.IsNullOrEmpty(itemRef) || quantity <= 0)
            return;

        if (!state.Characters.TryGetValue(characterInstanceId.ToString(), out var character))
            return;

        character.CurrentInventory ??= new ItemCollection();
        var inv = character.CurrentInventory;

        // Merchant's delta is opposite the player's direction.
        //   IsBuying=true  (player buys)  → merchant loses → delta negative
        //   IsBuying=false (player sells) → merchant gains → delta positive
        var delta = isBuying ? -quantity : +quantity;

        // A TransactionReversed compensation re-applies the trade with the
        // opposite delta, restoring the merchant's pre-trade stock
        if (invert)
            delta = -delta;

        // Dispatch by world catalog (Saga's IWorld doesn't expose BlocksLookup — Block is the else-fallback).
        if (_world.ConsumablesLookup.ContainsKey(itemRef))
        {
            var c = inv.GetOrAddConsumable(itemRef);
            c.Quantity += delta;
            if (c.Quantity <= 0)
                inv.Consumables = (inv.Consumables ?? Array.Empty<ConsumableEntry>()).Where(x => x.ConsumableRef != itemRef).ToArray();
        }
        else if (_world.BuildingMaterialsLookup.ContainsKey(itemRef))
        {
            var m = inv.GetOrAddBuildingMaterial(itemRef);
            m.Quantity += delta;
            if (m.Quantity <= 0)
                inv.BuildingMaterials = (inv.BuildingMaterials ?? Array.Empty<BuildingMaterialEntry>()).Where(x => x.BuildingMaterialRef != itemRef).ToArray();
        }
        else if (_world.EquipmentLookup.ContainsKey(itemRef))
        {
            var list = (inv.Equipment ?? Array.Empty<EquipmentEntry>()).ToList();
            if (delta > 0)
            {
                for (var i = 0; i < delta; i++) list.Add(new EquipmentEntry { EquipmentRef = itemRef, Condition = 1f });
            }
            else
            {
                for (var i = 0; i < -delta; i++)
                {
                    var idx = list.FindIndex(e => e.EquipmentRef == itemRef);
                    if (idx >= 0) list.RemoveAt(idx);
                }
            }
            inv.Equipment = list.ToArray();
        }
        else if (_world.ToolsLookup.ContainsKey(itemRef))
        {
            var list = (inv.Tools ?? Array.Empty<ToolEntry>()).ToList();
            if (delta > 0)
            {
                for (var i = 0; i < delta; i++) list.Add(new ToolEntry { ToolRef = itemRef, Condition = 1f });
            }
            else
            {
                for (var i = 0; i < -delta; i++)
                {
                    var idx = list.FindIndex(t => t.ToolRef == itemRef);
                    if (idx >= 0) list.RemoveAt(idx);
                }
            }
            inv.Tools = list.ToArray();
        }
        else if (_world.SpellsLookup.ContainsKey(itemRef))
        {
            var list = (inv.Spells ?? Array.Empty<SpellEntry>()).ToList();
            if (delta > 0)
            {
                for (var i = 0; i < delta; i++) list.Add(new SpellEntry { SpellRef = itemRef, Condition = 1f });
            }
            else
            {
                for (var i = 0; i < -delta; i++)
                {
                    var idx = list.FindIndex(s => s.SpellRef == itemRef);
                    if (idx >= 0) list.RemoveAt(idx);
                }
            }
            inv.Spells = list.ToArray();
        }
        else
        {
            // Block fallback
            var b = inv.GetOrAddBlock(itemRef);
            b.Quantity += delta;
            if (b.Quantity <= 0)
                inv.Blocks = (inv.Blocks ?? Array.Empty<BlockEntry>()).Where(x => x.BlockRef != itemRef).ToArray();
        }
    }

    private void ApplyQuestTokenAwarded(SagaState state, SagaTransaction tx)
    {
        var tokenRef = tx.GetData<string>(TransactionDataKeys.QuestTokenRef);
        if (!string.IsNullOrEmpty(tokenRef))
        {
            state.AwardedQuestTokens.Add(tokenRef);
        }
    }

    private void ApplyQuestAccepted(SagaState state, SagaTransaction tx)
    {
        var questRef = tx.GetData<string>(TransactionDataKeys.QuestRef);
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
            QuestGiverRef = tx.GetData<string>(TransactionDataKeys.QuestGiverRef) ?? string.Empty,
            SagaRef = tx.GetData<string>(TransactionDataKeys.SagaArcRef) ?? state.SagaRef
        };

        state.ActiveQuests[questRef] = questState;
    }

    private void ApplyQuestObjectiveCompleted(SagaState state, SagaTransaction tx)
    {
        var questRef = tx.GetData<string>(TransactionDataKeys.QuestRef);
        var stageRef = tx.GetData<string>(TransactionDataKeys.StageRef);
        var objectiveRef = tx.GetData<string>(TransactionDataKeys.ObjectiveRef);

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
        if (tx.TryGetData<int>(TransactionDataKeys.CurrentValue, out var currentValue))
        {
            questState.ObjectiveProgress[objectiveRef] = currentValue;
        }
    }

    private void ApplyQuestStageAdvanced(SagaState state, SagaTransaction tx)
    {
        var questRef = tx.GetData<string>(TransactionDataKeys.QuestRef);
        var nextStageRef = tx.GetData<string>(TransactionDataKeys.NextStage);

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
        var questRef = tx.GetData<string>(TransactionDataKeys.QuestRef);
        var branchRef = tx.GetData<string>(TransactionDataKeys.BranchRef);

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
        var questRef = tx.GetData<string>(TransactionDataKeys.QuestRef);
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
        var questRef = tx.GetData<string>(TransactionDataKeys.QuestRef);
        if (string.IsNullOrEmpty(questRef))
            return;

        // Only apply if quest is active
        if (!state.ActiveQuests.TryGetValue(questRef, out var questState))
            return;

        // Mark as failed
        questState.IsFailed = true;
        questState.FailureReason = tx.GetData<string>(TransactionDataKeys.FailureReason);
        questState.CompletedAt = tx.GetCanonicalTimestamp();

        // Remove from active quests (not added to completed)
        state.ActiveQuests.Remove(questRef);
    }

    private void ApplyQuestAbandoned(SagaState state, SagaTransaction tx)
    {
        var questRef = tx.GetData<string>(TransactionDataKeys.QuestRef);
        if (string.IsNullOrEmpty(questRef))
            return;

        // Simply remove from active quests (not added to completed)
        state.ActiveQuests.Remove(questRef);
    }
    private void ApplyEntityInteracted(SagaState state, SagaTransaction tx)
    {
        var featureRef = tx.GetData<string>(TransactionDataKeys.FeatureRef);
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

    // ===== Compensating Transaction Application =====

    /// <summary>
    /// Applies a TransactionReversed compensation: the referenced transaction
    /// committed to the log but its avatar-side persistence failed (see
    /// TradeItemHandler), so its saga-side fold must be undone. Reverses the
    /// types with a state fold (ItemTraded); anything else is logged and
    /// skipped — never quarantined as unknown.
    /// </summary>
    private void ApplyTransactionReversed(SagaState state, SagaTransaction tx, IReadOnlyDictionary<Guid, SagaTransaction>? transactionsById)
    {
        var reversedIdRaw = tx.GetData<string>(TransactionDataKeys.ReversedTransactionId);
        if (!Guid.TryParse(reversedIdRaw, out var reversedId) ||
            transactionsById == null ||
            !transactionsById.TryGetValue(reversedId, out var original))
        {
            // Without the original transaction there is nothing to undo (e.g. a
            // single-transaction ApplyTransaction call outside Replay) — skip
            _logger?.LogWarning(
                "TransactionReversed {TransactionId} references transaction '{ReversedId}' which is not available; skipping reversal",
                tx.TransactionId,
                reversedIdRaw);
            return;
        }

        switch (original.Type)
        {
            case SagaTransactionType.ItemTraded:
                ApplyItemTraded(state, original, invert: true);
                break;

            default:
                // No reversible saga-side fold for this type; the reversal remains
                // in the log as an audit record
                _logger?.LogInformation(
                    "TransactionReversed {TransactionId} for {OriginalType} has no state fold to reverse; skipping",
                    tx.TransactionId,
                    original.Type);
                break;
        }
    }

    // ===== Extension Transaction Application =====

    private void ApplyExtension(SagaState state, SagaTransaction tx)
    {
        // Extension transactions are handled by domain-specific appliers (e.g., Ambient.Core).
        // The base Saga engine records them and surfaces an observability signal when the
        // extension name is not registered — so a rename in the extension package fails the
        // registry check here instead of drifting into a silent replay skip.
        //
        // The ExtensionTypeName property indicates the domain-specific type:
        // - LocationClaimed, ToolWearClaimed, MiningSessionClaimed, etc. (voxel claims)
        // - ProcessingCycleCompleted (crafting/processing)
        // - BlockExtensionCreated/Updated/Destroyed (cache blocks)

        var name = tx.ExtensionTypeName;
        if (string.IsNullOrEmpty(name) || _extensionRegistry.IsKnown(name))
        {
            return;
        }

        _metrics.IncrementQuarantinedExtension(name);

        if (_unknownPolicy == UnknownTransactionPolicy.Throw)
        {
            throw new InvalidOperationException(
                $"Unknown extension transaction type '{name}' encountered during replay " +
                $"(TransactionId={tx.TransactionId}, SequenceNumber={tx.SequenceNumber}). " +
                $"Register it with IExtensionTypeRegistry or switch policy to Quarantine.");
        }

        _logger?.LogWarning(
            "Unknown extension transaction type '{ExtensionTypeName}' encountered during replay; quarantined. TransactionId={TransactionId} SequenceNumber={SequenceNumber}",
            name,
            tx.TransactionId,
            tx.SequenceNumber);
    }

    // ===== Snapshot Support =====

    private static readonly JsonSerializerOptions SnapshotJsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>
    /// Creates a StateSnapshot transaction that captures the current derived state.
    /// When present in the transaction log, Replay() resumes from the snapshot
    /// instead of replaying from the beginning — reducing both replay time and sync payload.
    /// </summary>
    public SagaTransaction CreateSnapshotTransaction(SagaInstance instance, string? avatarId = null)
    {
        var state = ReplayToNow(instance);

        return new SagaTransaction
        {
            TransactionId = Guid.NewGuid(),
            Type = SagaTransactionType.StateSnapshot,
            AvatarId = avatarId,
            LocalTimestamp = DateTime.UtcNow,
            Data = new Dictionary<string, string>
            {
                [TransactionDataKeys.StateJson] = JsonSerializer.Serialize(state, SnapshotJsonOptions),
                [TransactionDataKeys.AtSequenceNumber] = instance.CachedAtSequenceNumber.ToString()
            }
        };
    }

    private bool TryDeserializeSnapshot(SagaTransaction snapshotTx, out SagaState state)
    {
        state = null!;
        var json = snapshotTx.GetData<string>(TransactionDataKeys.StateJson);
        if (string.IsNullOrEmpty(json))
            return false;

        try
        {
            var deserialized = JsonSerializer.Deserialize<SagaState>(json, SnapshotJsonOptions);
            if (deserialized != null)
            {
                state = deserialized;
                return true;
            }
            return false;
        }
        catch (Exception ex)
        {
            // Snapshot deserialization failed — Replay() will fall back to full replay.
            // Surface this so corruption or schema drift is visible rather than manifesting as a perf regression.
            _logger?.LogError(
                ex,
                "Saga snapshot deserialization failed; falling back to full replay. TransactionId={TransactionId} SequenceNumber={SequenceNumber}",
                snapshotTx.TransactionId,
                snapshotTx.SequenceNumber);
            _metrics.IncrementSnapshotDeserializationFailure(snapshotTx.TransactionId, snapshotTx.SequenceNumber);
            return false;
        }
    }
}
