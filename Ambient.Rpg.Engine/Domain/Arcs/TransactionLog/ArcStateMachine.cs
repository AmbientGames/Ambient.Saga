using System.Text.Json;
using System.Text.Json.Serialization;
using Ambient.Domain;
using Ambient.Domain.Contracts;
using Ambient.Domain.Extensions;
using Ambient.Domain.GameLogic.Gameplay.Avatar;
using Ambient.Rpg.Engine.Domain;
using Microsoft.Extensions.Logging;

namespace Ambient.Rpg.Engine.Domain.Arcs.TransactionLog;

/// <summary>
/// State machine that derives current Arc state by replaying transactions.
/// This is the heart of the event sourcing system.
///
/// Key Principles:
/// - State is derived, not stored
/// - Transactions are immutable source of truth
/// - Replay is deterministic and idempotent
/// - Can replay to any point in time
/// </summary>
public class ArcStateMachine
{
    private readonly Arc _template;
    private readonly List<ArcTrigger> _expandedArcTriggers;
    private readonly IWorld _world;
    private readonly ILogger<ArcStateMachine>? _logger;
    private readonly IArcMetrics _metrics;
    private readonly IExtensionTypeRegistry _extensionRegistry;
    private readonly UnknownTransactionPolicy _unknownPolicy;

    public ArcStateMachine(
        Arc template,
        List<ArcTrigger> expandedArcTriggers,
        IWorld world,
        ILogger<ArcStateMachine>? logger = null,
        IArcMetrics? metrics = null,
        IExtensionTypeRegistry? extensionRegistry = null,
        UnknownTransactionPolicy unknownPolicy = UnknownTransactionPolicy.Quarantine)
    {
        _template = template ?? throw new ArgumentNullException(nameof(template));
        _expandedArcTriggers = expandedArcTriggers ?? throw new ArgumentNullException(nameof(expandedArcTriggers));
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _logger = logger;
        _metrics = metrics ?? NullArcMetrics.Instance;
        // Permissive default: any self-identifying extension replays as host-layer
        // data without quarantine noise; pass an explicit registry for strict checking
        _extensionRegistry = extensionRegistry ?? PermissiveExtensionTypeRegistry.Instance;
        _unknownPolicy = unknownPolicy;
    }

    /// <summary>
    /// Replays all committed transactions to derive the current state.
    /// Returns cached state if the transaction log hasn't changed since last replay.
    /// </summary>
    public ArcState ReplayToNow(ArcInstance instance)
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
    public ArcState ReplayToTimestamp(ArcInstance instance, DateTime timestamp)
    {
        var transactions = instance.GetCommittedTransactions()
            .Where(t => t.GetCanonicalTimestamp() <= timestamp)
            .ToList();

        return Replay(transactions);
    }

    /// <summary>
    /// Replays transactions up to a specific sequence number.
    /// </summary>
    public ArcState ReplayToSequence(ArcInstance instance, long sequenceNumber)
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
    public ArcState Replay(List<ArcTransaction> transactions)
    {
        var ordered = transactions.OrderBy(t => t.SequenceNumber).ToList();

        // TransactionReversed compensations need the referenced transaction's data
        // to compute the inverse fold
        var transactionsById = new Dictionary<Guid, ArcTransaction>(ordered.Count);
        foreach (var tx in ordered)
        {
            transactionsById[tx.TransactionId] = tx;
        }

        // Find the latest snapshot to avoid replaying the entire log
        ArcState? state = null;
        var replayFrom = 0;

        for (var i = ordered.Count - 1; i >= 0; i--)
        {
            if (ordered[i].Type == ArcTransactionType.StateSnapshot
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
        foreach (var trigger in _expandedArcTriggers)
        {
            if (!state.Triggers.ContainsKey(trigger.RefName))
            {
                state.Triggers[trigger.RefName] = new ArcTriggerState
                {
                    ArcTriggerRef = trigger.RefName,
                    Status = ArcTriggerStatus.Inactive
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
    public ArcState CreateInitialState()
    {
        var state = new ArcState
        {
            ArcRef = _template.RefName,
            Status = ArcStatus.Undiscovered
        };

        // Initialize trigger states from template
        foreach (var trigger in _expandedArcTriggers)
        {
            state.Triggers[trigger.RefName] = new ArcTriggerState
            {
                ArcTriggerRef = trigger.RefName,
                Status = ArcTriggerStatus.Inactive
            };
        }

        return state;
    }

    /// <summary>
    /// Applies a single transaction to the state.
    /// This method must be deterministic and idempotent.
    /// </summary>
    public void ApplyTransaction(ArcState state, ArcTransaction tx)
    {
        ApplyTransaction(state, tx, transactionsById: null);
    }

    /// <summary>
    /// Applies a single transaction, with the full log available by id so
    /// TransactionReversed compensations can look up the transaction they undo.
    /// </summary>
    private void ApplyTransaction(ArcState state, ArcTransaction tx, IReadOnlyDictionary<Guid, ArcTransaction>? transactionsById)
    {
        switch (tx.Type)
        {
            case ArcTransactionType.ArcDiscovered:
                ApplyArcDiscovered(state, tx);
                break;

            case ArcTransactionType.ArcCompleted:
                ApplyArcCompleted(state, tx);
                break;

            case ArcTransactionType.TriggerActivated:
                ApplyTriggerActivated(state, tx);
                break;

            case ArcTransactionType.TriggerCompleted:
                ApplyTriggerCompleted(state, tx);
                break;

            case ArcTransactionType.CharacterSpawned:
                ApplyCharacterSpawned(state, tx);
                break;

            case ArcTransactionType.CharacterDamaged:
                ApplyCharacterDamaged(state, tx);
                break;

            case ArcTransactionType.CharacterHealed:
                ApplyCharacterHealed(state, tx);
                break;

            case ArcTransactionType.CharacterDefeated:
                ApplyCharacterDefeated(state, tx);
                break;

            case ArcTransactionType.CharacterDespawned:
                ApplyCharacterDespawned(state, tx);
                break;

            case ArcTransactionType.AvatarEntered:
                ApplyAvatarEntered(state, tx);
                break;

            case ArcTransactionType.AvatarExited:
                ApplyAvatarExited(state, tx);
                break;

            case ArcTransactionType.DialogueStarted:
                ApplyDialogueStarted(state, tx);
                break;

            case ArcTransactionType.DialogueNodeVisited:
                ApplyDialogueNodeVisited(state, tx);
                break;

            case ArcTransactionType.DialogueCompleted:
                ApplyDialogueCompleted(state, tx);
                break;

            case ArcTransactionType.TraitAssigned:
                ApplyTraitAssigned(state, tx);
                break;

            case ArcTransactionType.TraitRemoved:
                ApplyTraitRemoved(state, tx);
                break;

            case ArcTransactionType.ReputationChanged:
                ApplyReputationChanged(state, tx);
                break;

            case ArcTransactionType.ItemTraded:
                ApplyItemTraded(state, tx);
                break;

            case ArcTransactionType.ShopPriceSet:
                ApplyShopPriceSet(state, tx);
                break;

            case ArcTransactionType.QuestTokenAwarded:
                ApplyQuestTokenAwarded(state, tx);
                break;

            case ArcTransactionType.QuestAccepted:
                ApplyQuestAccepted(state, tx);
                break;

            case ArcTransactionType.QuestObjectiveCompleted:
                ApplyQuestObjectiveCompleted(state, tx);
                break;

            case ArcTransactionType.QuestStageAdvanced:
                ApplyQuestStageAdvanced(state, tx);
                break;

            case ArcTransactionType.QuestBranchChosen:
                ApplyQuestBranchChosen(state, tx);
                break;

            case ArcTransactionType.QuestCompleted:
                ApplyQuestCompleted(state, tx);
                break;

            case ArcTransactionType.QuestAbandoned:
                ApplyQuestAbandoned(state, tx);
                break;

            case ArcTransactionType.EntityInteracted:
                ApplyEntityInteracted(state, tx);
                break;

            case ArcTransactionType.TransactionReversed:
                ApplyTransactionReversed(state, tx, transactionsById);
                break;

            // Snapshots are consumed by Replay(), not applied individually
            case ArcTransactionType.StateSnapshot:
                break;

            // Extension types are handled by domain-specific appliers (e.g., the host game)
            // The transaction log itself is the source of truth for extension data
            case ArcTransactionType.Extension:
                ApplyExtension(state, tx);
                break;

            // Add more cases as needed
            default:
                if (StatelessTransactionTypes.Contains(tx.Type))
                {
                    // Legitimately emitted but carries no ArcState fold: battle turns are
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
    /// Types the engine emits that intentionally have no ArcState application —
    /// their consumers read the log directly or mutate the avatar entity.
    /// </summary>
    private static readonly HashSet<ArcTransactionType> StatelessTransactionTypes = new()
    {
        ArcTransactionType.BattleStarted,
        ArcTransactionType.BattleTurnExecuted,
        ArcTransactionType.BattleEnded,
        ArcTransactionType.EquipmentChanged,
        ArcTransactionType.ConsumableUsed,
        ArcTransactionType.ToolSharpened,
        ArcTransactionType.AvatarTeleported,
        ArcTransactionType.PartyMemberJoined,
        ArcTransactionType.PartyMemberLeft,
        ArcTransactionType.AffinityGranted,
        ArcTransactionType.EffectApplied,
        ArcTransactionType.CurrencyChanged, // read by QuestProgressEvaluator (CurrencyCollected objectives)
        ArcTransactionType.LootAwarded, // retired feature (corpse looting removed 2026-07-04); value reserved so stray historical transactions don't trip the unknown-type quarantine
    };

    private void HandleUnknownTransactionType(ArcTransaction tx)
    {
        var typeValue = (int)tx.Type;
        _metrics.IncrementUnknownTransactionType(typeValue);

        if (_unknownPolicy == UnknownTransactionPolicy.Throw)
        {
            throw new InvalidOperationException(
                $"Unknown ArcTransactionType value {typeValue} encountered during replay " +
                $"(TransactionId={tx.TransactionId}, SequenceNumber={tx.SequenceNumber}).");
        }

        _logger?.LogWarning(
            "Unknown ArcTransactionType value {TypeValue} encountered during replay; quarantined. TransactionId={TransactionId} SequenceNumber={SequenceNumber}",
            typeValue,
            tx.TransactionId,
            tx.SequenceNumber);
    }

    // ===== Transaction Application Methods =====

    private void ApplyArcDiscovered(ArcState state, ArcTransaction tx)
    {
        if (state.Status == ArcStatus.Undiscovered)
        {
            state.Status = ArcStatus.Active;
            state.FirstDiscoveredAt = tx.GetCanonicalTimestamp();
        }

        if (!string.IsNullOrEmpty(tx.AvatarId))
        {
            state.DiscoveredByAvatars.Add(tx.AvatarId);
        }
    }

    private void ApplyArcCompleted(ArcState state, ArcTransaction tx)
    {
        state.Status = ArcStatus.Completed;
        state.CompletedAt = tx.GetCanonicalTimestamp();

        if (!string.IsNullOrEmpty(tx.AvatarId))
        {
            state.CompletedByAvatars.Add(tx.AvatarId);
        }
    }

    private void ApplyTriggerActivated(ArcState state, ArcTransaction tx)
    {
        var triggerRef = tx.GetData<string>(TransactionDataKeys.ArcTriggerRef);
        if (string.IsNullOrEmpty(triggerRef) || !state.Triggers.TryGetValue(triggerRef, out var trigger))
            return;

        trigger.Status = ArcTriggerStatus.Active;
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

    private void ApplyTriggerCompleted(ArcState state, ArcTransaction tx)
    {
        var triggerRef = tx.GetData<string>(TransactionDataKeys.ArcTriggerRef);
        if (string.IsNullOrEmpty(triggerRef) || !state.Triggers.TryGetValue(triggerRef, out var trigger))
            return;

        trigger.Status = ArcTriggerStatus.Completed;
        trigger.CompletedAt = tx.GetCanonicalTimestamp();
    }

    private void ApplyCharacterSpawned(ArcState state, ArcTransaction tx)
    {
        var characterInstanceId = tx.GetData<Guid>(TransactionDataKeys.CharacterInstanceId);
        var characterRef = tx.GetData<string>(TransactionDataKeys.CharacterRef);
        var triggerRef = tx.GetData<string>(TransactionDataKeys.ArcTriggerRef);

        // Try new Arc-relative format first (X, Z), fallback to old GPS format (LongitudeX, LatitudeZ)
        var x = tx.TryGetData<double>(TransactionDataKeys.X, out var xVal) ? xVal : tx.GetData<double>(TransactionDataKeys.Longitude);
        var z = tx.TryGetData<double>(TransactionDataKeys.Z, out var zVal) ? zVal : tx.GetData<double>(TransactionDataKeys.Latitude);
        var spawnHeight = tx.TryGetData<double>(TransactionDataKeys.SpawnHeight, out var heightVal) ? heightVal : tx.GetData<double>(TransactionDataKeys.Y);

        if (characterInstanceId == Guid.Empty || string.IsNullOrEmpty(characterRef))
            return;

        // Look up character template from world
        var characterTemplate = _world.CharactersLookup.TryGetValue(characterRef, out var template) ? template : null;
        if (characterTemplate == null)
        {
            // Catalog miss used to be silent. CharacterSpawned then never ran, so every
            // following ItemTraded seed on this instance vanished (empty shops / loot).
            // The generator emits the runtime catalog (BATTLE_LOOT, REMNANT_LOOT,
            // GEOCACHE, SHOPKEEPER_Generic_0..9). A miss here is a missing catalog
            // entry or a typo — do not fabricate a shell.
            _logger?.LogError(
                "CharacterSpawned CharacterRef '{CharacterRef}' is not in the world catalog; spawn dropped (instance {InstanceId}). Following ItemTraded stock will not attach.",
                characterRef,
                characterInstanceId);
            return;
        }

        // Copy stats from template
        CharacterStats? copiedStats = null;
        if (characterTemplate.Stats != null)
        {
            copiedStats = new CharacterStats();
            CharacterStatsCopier.CopyCharacterStats(characterTemplate.Stats, copiedStats);
        }

        // Deep clone the GIVEABLE stock from the template - each item is cloned to avoid
        // shared state. The source is Interactable.Loot ("the stuff they give you"):
        // merchant trade stock for traders, victory drops for defeated hostiles/bosses.
        // Capabilities is deliberately NOT the source — that is the gear the character
        // USES in battle (re-attached from the template by the battle handlers), never
        // the player-acquirable inventory. Because the clone happens per CharacterSpawned
        // (per spawn instance), a RESPAWNED character comes back with fresh template Loot
        // while the previous life's ItemTraded takes stay folded on the old instance.
        var lootTemplate = characterTemplate.Interactable?.Loot;
        ItemCollection? copiedInventory = lootTemplate?.DeepClone();

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
            CurrentLatitudeZ = z,           // Arc-relative Z (using old field name for now)
            CurrentLongitudeX = x,          // Arc-relative X (using old field name for now)
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

    private void ApplyCharacterDamaged(ArcState state, ArcTransaction tx)
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
            // The avatar whose damage dropped the character to zero is the victor
            if (!string.IsNullOrEmpty(tx.AvatarId))
            {
                character.DefeatedByAvatarId = tx.AvatarId;
            }
        }
    }

    private void ApplyCharacterHealed(ArcState state, ArcTransaction tx)
    {
        var characterInstanceId = tx.GetData<Guid>(TransactionDataKeys.CharacterInstanceId);
        var healing = tx.GetData<double>(TransactionDataKeys.Healing);

        if (characterInstanceId == Guid.Empty || !state.Characters.TryGetValue(characterInstanceId.ToString(), out var character))
            return;

        character.CurrentHealth = Math.Min(1.0, character.CurrentHealth + healing);
    }

    private void ApplyCharacterDefeated(ArcState state, ArcTransaction tx)
    {
        var characterInstanceId = tx.GetData<Guid>(TransactionDataKeys.CharacterInstanceId);

        if (characterInstanceId == Guid.Empty || !state.Characters.TryGetValue(characterInstanceId.ToString(), out var character))
            return;

        character.IsAlive = false;
        character.CurrentHealth = 0;
        character.DefeatedAt = tx.GetCanonicalTimestamp();

        // Record the victor for this spawn instance — gates the victory-loot free-take
        // (BattleEndTransactionFactory / DefeatCharacterHandler stamp VictorAvatarId;
        // fall back to the transaction author for older logs).
        var victor = tx.GetData<string>(TransactionDataKeys.VictorAvatarId);
        character.DefeatedByAvatarId = !string.IsNullOrEmpty(victor) ? victor : tx.AvatarId;
    }

    private void ApplyCharacterDespawned(ArcState state, ArcTransaction tx)
    {
        var characterInstanceId = tx.GetData<Guid>(TransactionDataKeys.CharacterInstanceId);

        if (characterInstanceId == Guid.Empty || !state.Characters.TryGetValue(characterInstanceId.ToString(), out var character))
            return;

        character.IsSpawned = false;
        character.DespawnedAt = tx.GetCanonicalTimestamp();
    }

    private void ApplyAvatarEntered(ArcState state, ArcTransaction tx)
    {
        // Avatar entered Arc - may trigger discovery if first time
        if (state.Status == ArcStatus.Undiscovered && !string.IsNullOrEmpty(tx.AvatarId))
        {
            state.Status = ArcStatus.Active;
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

    private void ApplyAvatarExited(ArcState state, ArcTransaction tx)
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

    private void ApplyDialogueStarted(ArcState state, ArcTransaction tx)
    {
        // Dialogue started - currently just for tracking/achievements
        // Could add state.CurrentDialogues[avatarId] = dialogueTreeRef if needed
    }

    private void ApplyDialogueNodeVisited(ArcState state, ArcTransaction tx)
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

    private void ApplyDialogueCompleted(ArcState state, ArcTransaction tx)
    {
        // Dialogue completed - currently just for tracking/achievements
        // Could add completion stats if needed
    }

    private void ApplyTraitAssigned(ArcState state, ArcTransaction tx)
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

    private void ApplyTraitRemoved(ArcState state, ArcTransaction tx)
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

    private void ApplyReputationChanged(ArcState state, ArcTransaction tx)
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

    /// <summary>
    /// The shop owner's per-item listing: last write wins on replay; a price of 0 (or a
    /// missing price) clears the listing so the item falls back to catalog x markup.
    /// </summary>
    private static void ApplyShopPriceSet(ArcState state, ArcTransaction tx)
    {
        var listedItemRef = tx.GetData<string>(TransactionDataKeys.ItemRef);
        if (string.IsNullOrEmpty(listedItemRef))
            return;

        if (tx.TryGetData<int>(TransactionDataKeys.PricePerItem, out var listedPrice) && listedPrice > 0)
            state.ShopPrices[listedItemRef] = listedPrice;
        else
            state.ShopPrices.Remove(listedItemRef);
    }

    private void ApplyItemTraded(ArcState state, ArcTransaction tx, bool invert = false)
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

        // Dispatch by world catalog (Arc's IWorld doesn't expose BlocksLookup — Block is the else-fallback).
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
                inv.Blocks = (inv.Blocks ?? Array.Empty<BlockEntry>()).Where(x => !ReferenceEquals(x, b)).ToArray();
        }
    }

    private void ApplyQuestTokenAwarded(ArcState state, ArcTransaction tx)
    {
        var tokenRef = tx.GetData<string>(TransactionDataKeys.QuestTokenRef);
        if (!string.IsNullOrEmpty(tokenRef))
        {
            state.AwardedQuestTokens.Add(tokenRef);
        }
    }

    private void ApplyQuestAccepted(ArcState state, ArcTransaction tx)
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
            ArcRef = tx.GetData<string>(TransactionDataKeys.ArcRef) ?? state.ArcRef
        };

        state.ActiveQuests[questRef] = questState;
    }

    private void ApplyQuestObjectiveCompleted(ArcState state, ArcTransaction tx)
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

    private void ApplyQuestStageAdvanced(ArcState state, ArcTransaction tx)
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

    private void ApplyQuestBranchChosen(ArcState state, ArcTransaction tx)
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

    private void ApplyQuestCompleted(ArcState state, ArcTransaction tx)
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

    private void ApplyQuestAbandoned(ArcState state, ArcTransaction tx)
    {
        var questRef = tx.GetData<string>(TransactionDataKeys.QuestRef);
        if (string.IsNullOrEmpty(questRef))
            return;

        // Simply remove from active quests (not added to completed)
        state.ActiveQuests.Remove(questRef);
    }
    private void ApplyEntityInteracted(ArcState state, ArcTransaction tx)
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
    /// Applies a TransactionReversed compensation against a still-Committed
    /// original: the referenced transaction committed to the log but a later
    /// step failed (avatar persistence — see TradeItemHandler and the dialogue
    /// reward handlers), so its arc-side fold must be undone. Reverses the
    /// avatar-affecting types with an invertible fold (ItemTraded,
    /// ReputationChanged, QuestTokenAwarded, QuestAccepted, QuestCompleted,
    /// TraitAssigned); anything else is logged and skipped — never quarantined
    /// as unknown.
    ///
    /// Note: server-REJECTED transactions take a different path — they are
    /// marked Rejected (excluded from replay entirely), so their compensation
    /// deliberately dangles here and skips (the original isn't in
    /// transactionsById). See ArcInstanceRepository.RejectAndCompensateTransactionsAsync.
    /// </summary>
    private void ApplyTransactionReversed(ArcState state, ArcTransaction tx, IReadOnlyDictionary<Guid, ArcTransaction>? transactionsById)
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
            case ArcTransactionType.ItemTraded:
                ApplyItemTraded(state, original, invert: true);
                break;

            case ArcTransactionType.ReputationChanged:
            {
                var factionRef = original.GetData<string>(TransactionDataKeys.FactionRef);
                var amount = original.GetData<int>(TransactionDataKeys.Amount);
                if (!string.IsNullOrEmpty(factionRef) && state.FactionReputation.ContainsKey(factionRef))
                {
                    state.FactionReputation[factionRef] -= amount;
                }
                break;
            }

            case ArcTransactionType.QuestTokenAwarded:
            {
                var tokenRef = original.GetData<string>(TransactionDataKeys.QuestTokenRef);
                if (!string.IsNullOrEmpty(tokenRef))
                {
                    state.AwardedQuestTokens.Remove(tokenRef);
                }
                break;
            }

            case ArcTransactionType.QuestAccepted:
            {
                var questRef = original.GetData<string>(TransactionDataKeys.QuestRef);
                if (!string.IsNullOrEmpty(questRef))
                {
                    state.ActiveQuests.Remove(questRef);
                }
                break;
            }

            case ArcTransactionType.QuestCompleted:
            {
                // Inverse is "not completed": cross-arc gates stop counting it.
                // Stage progress at completion time is unrecoverable from the
                // single transaction, so the quest does NOT return to ActiveQuests —
                // the avatar can re-accept it.
                var questRef = original.GetData<string>(TransactionDataKeys.QuestRef);
                if (!string.IsNullOrEmpty(questRef))
                {
                    state.CompletedQuests.Remove(questRef);
                }
                break;
            }

            case ArcTransactionType.TraitAssigned:
            {
                // Prior value is unknowable — removal is the closest inverse
                // (mirrors ApplyTraitRemoved)
                var characterRef = original.GetData<string>(TransactionDataKeys.CharacterRef);
                var traitType = original.GetData<string>(TransactionDataKeys.TraitType);
                if (!string.IsNullOrEmpty(characterRef) && !string.IsNullOrEmpty(traitType))
                {
                    if (state.CharacterTraits.TryGetValue(characterRef, out var traits))
                    {
                        traits.Remove(traitType);
                    }
                    foreach (var characterState in state.Characters.Values)
                    {
                        if (characterState.CharacterRef == characterRef)
                        {
                            characterState.Traits.Remove(traitType);
                        }
                    }
                }
                break;
            }

            default:
                // No reversible arc-side fold for this type; the reversal remains
                // in the log as an audit record
                _logger?.LogInformation(
                    "TransactionReversed {TransactionId} for {OriginalType} has no state fold to reverse; skipping",
                    tx.TransactionId,
                    original.Type);
                break;
        }
    }

    // ===== Extension Transaction Application =====

    private void ApplyExtension(ArcState state, ArcTransaction tx)
    {
        // Extension transactions are handled by domain-specific appliers (e.g., the host game).
        // The base Arc engine records them and surfaces an observability signal when the
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
    public ArcTransaction CreateSnapshotTransaction(ArcInstance instance, string? avatarId = null)
    {
        var state = ReplayToNow(instance);

        return new ArcTransaction
        {
            TransactionId = Guid.NewGuid(),
            Type = ArcTransactionType.StateSnapshot,
            AvatarId = avatarId,
            LocalTimestamp = DateTime.UtcNow,
            Data = new Dictionary<string, string>
            {
                [TransactionDataKeys.StateJson] = JsonSerializer.Serialize(state, SnapshotJsonOptions),
                [TransactionDataKeys.AtSequenceNumber] = instance.CachedAtSequenceNumber.ToString()
            }
        };
    }

    private bool TryDeserializeSnapshot(ArcTransaction snapshotTx, out ArcState state)
    {
        state = null!;
        var json = snapshotTx.GetData<string>(TransactionDataKeys.StateJson);
        if (string.IsNullOrEmpty(json))
            return false;

        try
        {
            var deserialized = JsonSerializer.Deserialize<ArcState>(json, SnapshotJsonOptions);
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
                "Arc snapshot deserialization failed; falling back to full replay. TransactionId={TransactionId} SequenceNumber={SequenceNumber}",
                snapshotTx.TransactionId,
                snapshotTx.SequenceNumber);
            _metrics.IncrementSnapshotDeserializationFailure(snapshotTx.TransactionId, snapshotTx.SequenceNumber);
            return false;
        }
    }
}
