using System.Globalization;
using Ambient.Domain;
using Ambient.Domain.Contracts;
using Ambient.Saga.Engine.Domain.Rpg.Sagas.TransactionLog;
using Ambient.Saga.Engine.Domain;

namespace Ambient.Saga.Engine.Domain.Rpg.Battle;

/// <summary>
/// Helper service for creating battle-related Saga transactions.
/// Ensures battles are replayable and deterministic with complete audit trail.
/// </summary>
public static class BattleTransactionHelper
{
    /// <summary>
    /// Creates a transaction for starting a battle.
    /// Includes initial equipment/affinity snapshot for both combatants and random seed.
    /// </summary>
    public static SagaTransaction CreateBattleStartedTransaction(
        string avatarId,
        string sagaRef,
        Guid avatarCombatantId,
        Guid enemyCombatantId,
        string enemyCharacterRef,
        int randomSeed,
        Combatant avatar,
        Combatant enemy,
        List<string> avatarAffinityRefs,
        Guid sagaInstanceId,
        IReadOnlyList<Combatant>? companions = null)
    {
        var transaction = new SagaTransaction
        {
            TransactionId = Guid.NewGuid(),
            Type = SagaTransactionType.BattleStarted,
            AvatarId = avatarId,
            LocalTimestamp = DateTime.UtcNow,
            Data = new Dictionary<string, string>
            {
                [TransactionDataKeys.SagaArcRef] = sagaRef,
                [TransactionDataKeys.AvatarCombatantId] = avatarCombatantId.ToString(),
                [TransactionDataKeys.EnemyCombatantId] = enemyCombatantId.ToString(),
                [TransactionDataKeys.EnemyCharacterRef] = enemyCharacterRef,
                [TransactionDataKeys.RandomSeed] = randomSeed.ToString(),
                [TransactionDataKeys.SagaInstanceId] = sagaInstanceId.ToString(),

                // Avatar stats (invariant culture: transaction data must round-trip
                // across machine locales — comma-decimal cultures corrupt the log)
                [TransactionDataKeys.AvatarHealth] = FormatFloat(avatar.Health),
                [TransactionDataKeys.AvatarEnergy] = FormatFloat(avatar.Stamina),
                [TransactionDataKeys.AvatarStrength] = FormatFloat(avatar.Strength),
                [TransactionDataKeys.AvatarDefense] = FormatFloat(avatar.Defense),
                [TransactionDataKeys.AvatarSpeed] = FormatFloat(avatar.Speed),
                [TransactionDataKeys.AvatarMagic] = FormatFloat(avatar.Magic),
                [TransactionDataKeys.AvatarAffinity] = avatar.AffinityRef ?? "",

                // Enemy stats
                [TransactionDataKeys.EnemyHealth] = FormatFloat(enemy.Health),
                [TransactionDataKeys.EnemyEnergy] = FormatFloat(enemy.Stamina),
                [TransactionDataKeys.EnemyStrength] = FormatFloat(enemy.Strength),
                [TransactionDataKeys.EnemyDefense] = FormatFloat(enemy.Defense),
                [TransactionDataKeys.EnemySpeed] = FormatFloat(enemy.Speed),
                [TransactionDataKeys.EnemyMagic] = FormatFloat(enemy.Magic),
                [TransactionDataKeys.EnemyAffinity] = enemy.AffinityRef ?? ""
            }
        };

        // Record avatar's equipment inventory (what they own)
        if (avatar.Capabilities?.Equipment != null)
        {
            var equipmentRefs = avatar.Capabilities.Equipment
                .Select(e => $"{e.EquipmentRef}:{FormatFloat(e.Condition, "F2")}")
                .ToList();
            if (equipmentRefs.Count > 0)
                transaction.Data[TransactionDataKeys.AvatarEquipment] = string.Join(",", equipmentRefs);
        }

        // Record avatar's initial equipped slots
        if (avatar.CombatProfile != null && avatar.CombatProfile.Count > 0)
        {
            var equippedSlots = avatar.CombatProfile
                .Select(kvp => $"{kvp.Key}:{kvp.Value}")
                .ToList();
            transaction.Data[TransactionDataKeys.AvatarEquippedSlots] = string.Join(",", equippedSlots);
        }

        // Record avatar's available affinities
        if (avatarAffinityRefs != null && avatarAffinityRefs.Count > 0)
        {
            transaction.Data[TransactionDataKeys.AvatarAffinities] = string.Join(",", avatarAffinityRefs);
        }

        // Record enemy's equipment inventory (what they own)
        if (enemy.Capabilities?.Equipment != null)
        {
            var equipmentRefs = enemy.Capabilities.Equipment
                .Select(e => $"{e.EquipmentRef}:{FormatFloat(e.Condition, "F2")}")
                .ToList();
            if (equipmentRefs.Count > 0)
                transaction.Data[TransactionDataKeys.EnemyEquipment] = string.Join(",", equipmentRefs);
        }

        // Record enemy's initial equipped slots
        if (enemy.CombatProfile != null && enemy.CombatProfile.Count > 0)
        {
            var equippedSlots = enemy.CombatProfile
                .Select(kvp => $"{kvp.Key}:{kvp.Value}")
                .ToList();
            transaction.Data[TransactionDataKeys.EnemyEquippedSlots] = string.Join(",", equippedSlots);
        }

        // Record the party's companions — reconstruction rebuilds them from here.
        // Without this, companions existed only for the live opening turn and vanished
        // at the first command boundary (their turn loop was unreachable dead code).
        if (companions is { Count: > 0 })
        {
            transaction.Data[TransactionDataKeys.Companions] = SerializeCompanions(companions);
        }

        return transaction;
    }

    /// <summary>
    /// Creates a transaction for a battle turn.
    /// Records the action taken, damage dealt, and any state changes.
    /// For ChangeLoadout/affinity actions, records complete slot/affinity snapshot for replay.
    /// </summary>
    public static SagaTransaction CreateBattleTurnExecutedTransaction(
        string avatarId,
        Guid battleTransactionId,
        int turnNumber,
        string actorRefName,
        bool isAvatarTurn,
        ActionType decisionType,
        string? itemRefName,
        float damageDealt,
        float healingDone,
        string targetRefName,
        float targetHealthAfter,
        float actorEnergyAfter,
        Combatant actorAfterAction,
        IWorld world,
        Guid sagaInstanceId,
        Combatant? avatarState = null,
        Combatant? enemyState = null,
        IReadOnlyList<Combatant>? companionStates = null)
    {
        var transaction = new SagaTransaction
        {
            TransactionId = Guid.NewGuid(),
            Type = SagaTransactionType.BattleTurnExecuted,
            AvatarId = avatarId,
            LocalTimestamp = DateTime.UtcNow,
            Data = new Dictionary<string, string>
            {
                [TransactionDataKeys.BattleTransactionId] = battleTransactionId.ToString(),
                [TransactionDataKeys.TurnNumber] = turnNumber.ToString(),
                [TransactionDataKeys.Actor] = actorRefName,
                [TransactionDataKeys.IsAvatarTurn] = isAvatarTurn.ToString(),
                [TransactionDataKeys.DecisionType] = decisionType.ToString(),
                [TransactionDataKeys.DamageDealt] = FormatFloat(damageDealt),
                [TransactionDataKeys.HealingDone] = FormatFloat(healingDone),
                [TransactionDataKeys.Target] = targetRefName,
                [TransactionDataKeys.TargetHealthAfter] = FormatFloat(targetHealthAfter),
                [TransactionDataKeys.ActorEnergyAfter] = FormatFloat(actorEnergyAfter),
                [TransactionDataKeys.SagaInstanceId] = sagaInstanceId.ToString()
            }
        };

        if (!string.IsNullOrEmpty(itemRefName))
            transaction.Data[TransactionDataKeys.ItemRefName] = itemRefName;

        // ALWAYS snapshot equipment slots and affinity for every turn (for replay)
        if (world != null)
        {
            // Record complete equipment state from EquippedItems dictionary
            if (actorAfterAction.CombatProfile != null && actorAfterAction.CombatProfile.Count > 0)
            {
                // Store as "SlotName:RefName:Condition" for each equipped item
                var LoadoutSlots = new List<string>();

                foreach (var slot in actorAfterAction.CombatProfile)
                {
                    var slotName = slot.Key;
                    var equipmentRef = slot.Value;

                    // Find the condition from Capabilities.Equipment
                    var condition = 1.0f;
                    if (actorAfterAction.Capabilities?.Equipment != null)
                    {
                        var equipmentEntry = actorAfterAction.Capabilities.Equipment
                            .FirstOrDefault(e => e.EquipmentRef == equipmentRef);
                        if (equipmentEntry != null)
                        {
                            condition = equipmentEntry.Condition;
                        }
                    }

                    // Format: "MainHand:WoodenSword:1.00"
                    LoadoutSlots.Add($"{slotName}:{equipmentRef}:{FormatFloat(condition, "F2")}");
                }

                if (LoadoutSlots.Count > 0)
                    transaction.Data[TransactionDataKeys.LoadoutSlotSnapshot] = string.Join(",", LoadoutSlots);
            }

            // Record current affinity
            if (!string.IsNullOrEmpty(actorAfterAction.AffinityRef))
                transaction.Data[TransactionDataKeys.AffinitySnapshot] = actorAfterAction.AffinityRef;
        }

        // Snapshot both sides' buff/debuff modifiers so mid-battle buffs survive
        // the per-command reconstruction (a spell this turn may have modified
        // either combatant, so the actor snapshot alone is not enough)
        if (avatarState != null)
            AddStatModifierSnapshot(transaction, TransactionDataKeys.AvatarStatModifiers, avatarState);
        if (enemyState != null)
            AddStatModifierSnapshot(transaction, TransactionDataKeys.EnemyStatModifiers, enemyState);

        // Snapshot both sides' active status effects for the same reason —
        // poison/stun/etc. must survive into the next command's reconstruction
        if (avatarState != null)
            transaction.Data[TransactionDataKeys.AvatarStatusEffects] = SerializeStatusEffects(avatarState.ActiveStatusEffects);
        if (enemyState != null)
            transaction.Data[TransactionDataKeys.EnemyStatusEffects] = SerializeStatusEffects(enemyState.ActiveStatusEffects);

        // Absolute health/energy for BOTH sides after this turn. Reconstruction and
        // battle-end avatar persistence read these directly instead of inferring who
        // was hit from Target/turn parity — inference corrupted state whenever the
        // event was a failure (empty Target), a companion turn, or an enemy self-heal.
        if (avatarState != null)
        {
            transaction.Data[TransactionDataKeys.AvatarHealthAfterTurn] = FormatFloat(avatarState.Health);
            transaction.Data[TransactionDataKeys.AvatarEnergyAfterTurn] = FormatFloat(avatarState.Stamina);
        }
        if (enemyState != null)
        {
            transaction.Data[TransactionDataKeys.EnemyHealthAfterTurn] = FormatFloat(enemyState.Health);
            transaction.Data[TransactionDataKeys.EnemyEnergyAfterTurn] = FormatFloat(enemyState.Stamina);
        }

        // Companion state after this turn (health/energy/effects, last snapshot wins) —
        // companion damage used to fold into the AVATAR via turn-parity inference
        if (companionStates is { Count: > 0 })
        {
            transaction.Data[TransactionDataKeys.CompanionStatesAfterTurn] = SerializeCompanionTurnStates(companionStates);
        }

        return transaction;
    }

    /// <summary>
    /// Serializes the full companion roster for the BattleStarted transaction
    /// (identity + stats + equipped slots). JSON keeps names with arbitrary
    /// characters safe and is culture-invariant.
    /// </summary>
    public static string SerializeCompanions(IReadOnlyList<Combatant> companions)
        => System.Text.Json.JsonSerializer.Serialize(companions.Select(c => new CompanionSnapshot
        {
            RefName = c.RefName,
            DisplayName = c.DisplayName,
            Health = c.Health,
            Stamina = c.Stamina,
            Strength = c.Strength,
            Defense = c.Defense,
            Speed = c.Speed,
            Magic = c.Magic,
            AffinityRef = c.AffinityRef,
            Slots = c.CombatProfile is { Count: > 0 } ? new Dictionary<string, string>(c.CombatProfile) : null
        }).ToList());

    /// <summary>Rebuilds companion combatants recorded by <see cref="SerializeCompanions"/>.</summary>
    public static List<Combatant> DeserializeCompanions(string json)
    {
        var snapshots = System.Text.Json.JsonSerializer.Deserialize<List<CompanionSnapshot>>(json)
                        ?? new List<CompanionSnapshot>();
        return snapshots.Select(s => new Combatant
        {
            RefName = s.RefName ?? "",
            DisplayName = s.DisplayName ?? s.RefName ?? "Companion",
            Health = s.Health,
            Stamina = s.Stamina,
            Strength = s.Strength,
            Defense = s.Defense,
            Speed = s.Speed,
            Magic = s.Magic,
            AffinityRef = s.AffinityRef,
            CombatProfile = s.Slots != null ? new Dictionary<string, string>(s.Slots) : new Dictionary<string, string>()
        }).ToList();
    }

    /// <summary>Serializes the companions' per-turn mutable state (health/energy/effects).</summary>
    public static string SerializeCompanionTurnStates(IReadOnlyList<Combatant> companions)
        => System.Text.Json.JsonSerializer.Serialize(companions.Select(c => new CompanionTurnState
        {
            RefName = c.RefName,
            Health = c.Health,
            Stamina = c.Stamina,
            Effects = c.ActiveStatusEffects.Count > 0 ? SerializeStatusEffects(c.ActiveStatusEffects) : null
        }).ToList());

    /// <summary>
    /// Applies a per-turn companion state snapshot onto reconstructed companions
    /// (matched by RefName; last snapshot wins).
    /// </summary>
    public static void ApplyCompanionTurnStates(List<Combatant> companions, string json)
    {
        var states = System.Text.Json.JsonSerializer.Deserialize<List<CompanionTurnState>>(json);
        if (states == null)
            return;

        foreach (var state in states)
        {
            var companion = companions.FirstOrDefault(c => c.RefName == state.RefName);
            if (companion == null)
                continue;

            companion.Health = state.Health;
            companion.Stamina = state.Stamina;
            ApplyStatusEffectSnapshot(companion, state.Effects ?? "");
        }
    }

    private sealed class CompanionSnapshot
    {
        public string? RefName { get; set; }
        public string? DisplayName { get; set; }
        public float Health { get; set; }
        public float Stamina { get; set; }
        public float Strength { get; set; }
        public float Defense { get; set; }
        public float Speed { get; set; }
        public float Magic { get; set; }
        public string? AffinityRef { get; set; }
        public Dictionary<string, string>? Slots { get; set; }
    }

    private sealed class CompanionTurnState
    {
        public string? RefName { get; set; }
        public float Health { get; set; }
        public float Stamina { get; set; }
        public string? Effects { get; set; }
    }

    /// <summary>Formats a float for transaction data (invariant culture, F3 by default).</summary>
    public static string FormatFloat(float value, string format = "F3")
        => value.ToString(format, CultureInfo.InvariantCulture);

    /// <summary>
    /// Parses a float written by <see cref="FormatFloat"/>. Transactions persisted
    /// before the invariant-culture fix may carry comma decimals ("0,85") — F-format
    /// never writes group separators, so a lone comma is always a decimal separator.
    /// </summary>
    public static bool TryParseFloat(string? text, out float value)
    {
        if (string.IsNullOrEmpty(text))
        {
            value = 0f;
            return false;
        }
        if (text.Contains(',') && !text.Contains('.'))
        {
            text = text.Replace(',', '.');
        }
        return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private static void AddStatModifierSnapshot(SagaTransaction transaction, string dataKey, Combatant combatant)
    {
        // Written even when empty: a cleared/never-buffed state must overwrite
        // any earlier snapshot during the reconstruction fold
        transaction.Data[dataKey] = SerializeStatModifiers(combatant.CombatStatModifiers);
    }

    /// <summary>Serializes combat stat modifiers as "Strength:0.2,Defense:-0.1" (invariant culture).</summary>
    public static string SerializeStatModifiers(Dictionary<string, float> modifiers)
        => string.Join(",", modifiers
            .Where(kv => kv.Value != 0f)
            .Select(kv => $"{kv.Key}:{kv.Value.ToString("F4", System.Globalization.CultureInfo.InvariantCulture)}"));

    /// <summary>Replaces the combatant's stat modifiers from a serialized snapshot.</summary>
    public static void ApplyStatModifierSnapshot(Combatant combatant, string? snapshot)
    {
        combatant.CombatStatModifiers.Clear();
        if (string.IsNullOrEmpty(snapshot))
            return; // empty snapshot = no active modifiers (LiteDB may round-trip "" as null)

        foreach (var entry in snapshot.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var sep = entry.LastIndexOf(':');
            if (sep <= 0) continue;
            if (float.TryParse(entry[(sep + 1)..], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var value))
            {
                combatant.CombatStatModifiers[entry[..sep]] = value;
            }
        }
    }

    /// <summary>Serializes active status effects as "Poison:3:2:1" (Ref:RemainingTurns:Stacks:AppliedOnTurn), comma-joined.</summary>
    public static string SerializeStatusEffects(List<ActiveStatusEffect> effects)
        => string.Join(",", effects.Select(e => $"{e.StatusEffectRef}:{e.RemainingTurns}:{e.Stacks}:{e.AppliedOnTurn}"));

    /// <summary>Replaces the combatant's active status effects from a serialized snapshot.</summary>
    public static void ApplyStatusEffectSnapshot(Combatant combatant, string? snapshot)
    {
        combatant.ActiveStatusEffects.Clear();
        if (string.IsNullOrEmpty(snapshot))
            return; // empty snapshot = no active effects (LiteDB may round-trip "" as null)

        foreach (var entry in snapshot.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = entry.Split(':');
            if (parts.Length != 4) continue;
            if (int.TryParse(parts[1], out var remaining) &&
                int.TryParse(parts[2], out var stacks) &&
                int.TryParse(parts[3], out var appliedOn))
            {
                combatant.ActiveStatusEffects.Add(new ActiveStatusEffect
                {
                    StatusEffectRef = parts[0],
                    RemainingTurns = remaining,
                    Stacks = stacks,
                    AppliedOnTurn = appliedOn
                });
            }
        }
    }

    /// <summary>
    /// Creates a transaction for battle conclusion.
    /// Records the victor and final state.
    /// </summary>
    public static SagaTransaction CreateBattleEndedTransaction(
        string avatarId,
        Guid battleTransactionId,
        int totalTurns,
        bool avatarVictory,
        string victorRefName,
        string defeatedRefName,
        Guid sagaInstanceId,
        BattleState? outcome = null)
    {
        var transaction = new SagaTransaction
        {
            TransactionId = Guid.NewGuid(),
            Type = SagaTransactionType.BattleEnded,
            AvatarId = avatarId,
            LocalTimestamp = DateTime.UtcNow,
            Data = new Dictionary<string, string>
            {
                [TransactionDataKeys.BattleTransactionId] = battleTransactionId.ToString(),
                [TransactionDataKeys.TotalTurns] = totalTurns.ToString(),
                [TransactionDataKeys.AvatarVictory] = avatarVictory.ToString(),
                [TransactionDataKeys.Victor] = victorRefName,
                [TransactionDataKeys.Defeated] = defeatedRefName,
                [TransactionDataKeys.SagaInstanceId] = sagaInstanceId.ToString()
            }
        };

        // Distinct outcome (Victory/Defeat/Fled). AvatarVictory alone cannot represent
        // Fled — fleeing used to be recorded (and shown, and trigger-evaluated) as a defeat.
        if (outcome.HasValue)
        {
            transaction.Data[TransactionDataKeys.BattleOutcome] = outcome.Value.ToString();
        }

        return transaction;
    }
}
