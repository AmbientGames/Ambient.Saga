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
        List<string> playerAffinityRefs,
        Guid sagaInstanceId)
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

                // Avatar stats
                [TransactionDataKeys.AvatarHealth] = avatar.Health.ToString("F3"),
                [TransactionDataKeys.AvatarEnergy] = avatar.Stamina.ToString("F3"),
                [TransactionDataKeys.AvatarStrength] = avatar.Strength.ToString("F3"),
                [TransactionDataKeys.AvatarDefense] = avatar.Defense.ToString("F3"),
                [TransactionDataKeys.AvatarSpeed] = avatar.Speed.ToString("F3"),
                [TransactionDataKeys.AvatarMagic] = avatar.Magic.ToString("F3"),
                [TransactionDataKeys.AvatarAffinity] = avatar.AffinityRef ?? "",

                // Enemy stats
                [TransactionDataKeys.EnemyHealth] = enemy.Health.ToString("F3"),
                [TransactionDataKeys.EnemyEnergy] = enemy.Stamina.ToString("F3"),
                [TransactionDataKeys.EnemyStrength] = enemy.Strength.ToString("F3"),
                [TransactionDataKeys.EnemyDefense] = enemy.Defense.ToString("F3"),
                [TransactionDataKeys.EnemySpeed] = enemy.Speed.ToString("F3"),
                [TransactionDataKeys.EnemyMagic] = enemy.Magic.ToString("F3"),
                [TransactionDataKeys.EnemyAffinity] = enemy.AffinityRef ?? ""
            }
        };

        // Record avatar's equipment inventory (what they own)
        if (avatar.Capabilities?.Equipment != null)
        {
            var equipmentRefs = avatar.Capabilities.Equipment
                .Select(e => $"{e.EquipmentRef}:{e.Condition:F2}")
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
        if (playerAffinityRefs != null && playerAffinityRefs.Count > 0)
        {
            transaction.Data[TransactionDataKeys.AvatarAffinities] = string.Join(",", playerAffinityRefs);
        }

        // Record enemy's equipment inventory (what they own)
        if (enemy.Capabilities?.Equipment != null)
        {
            var equipmentRefs = enemy.Capabilities.Equipment
                .Select(e => $"{e.EquipmentRef}:{e.Condition:F2}")
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
        bool isPlayerTurn,
        ActionType decisionType,
        string? itemRefName,
        float damageDealt,
        float healingDone,
        string targetRefName,
        float targetHealthAfter,
        float actorEnergyAfter,
        Combatant actorAfterAction,
        IWorld world,
        Guid sagaInstanceId)
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
                [TransactionDataKeys.IsAvatarTurn] = isPlayerTurn.ToString(),
                [TransactionDataKeys.DecisionType] = decisionType.ToString(),
                [TransactionDataKeys.DamageDealt] = damageDealt.ToString("F3"),
                [TransactionDataKeys.HealingDone] = healingDone.ToString("F3"),
                [TransactionDataKeys.Target] = targetRefName,
                [TransactionDataKeys.TargetHealthAfter] = targetHealthAfter.ToString("F3"),
                [TransactionDataKeys.ActorEnergyAfter] = actorEnergyAfter.ToString("F3"),
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
                    LoadoutSlots.Add($"{slotName}:{equipmentRef}:{condition:F2}");
                }

                if (LoadoutSlots.Count > 0)
                    transaction.Data[TransactionDataKeys.LoadoutSlotSnapshot] = string.Join(",", LoadoutSlots);
            }

            // Record current affinity
            if (!string.IsNullOrEmpty(actorAfterAction.AffinityRef))
                transaction.Data[TransactionDataKeys.AffinitySnapshot] = actorAfterAction.AffinityRef;
        }

        return transaction;
    }

    /// <summary>
    /// Creates a transaction for battle conclusion.
    /// Records the victor and final state.
    /// </summary>
    public static SagaTransaction CreateBattleEndedTransaction(
        string avatarId,
        Guid battleTransactionId,
        int totalTurns,
        bool playerVictory,
        string victorRefName,
        string defeatedRefName,
        Guid sagaInstanceId)
    {
        return new SagaTransaction
        {
            TransactionId = Guid.NewGuid(),
            Type = SagaTransactionType.BattleEnded,
            AvatarId = avatarId,
            LocalTimestamp = DateTime.UtcNow,
            Data = new Dictionary<string, string>
            {
                [TransactionDataKeys.BattleTransactionId] = battleTransactionId.ToString(),
                [TransactionDataKeys.TotalTurns] = totalTurns.ToString(),
                [TransactionDataKeys.AvatarVictory] = playerVictory.ToString(),
                [TransactionDataKeys.Victor] = victorRefName,
                [TransactionDataKeys.Defeated] = defeatedRefName,
                [TransactionDataKeys.SagaInstanceId] = sagaInstanceId.ToString()
            }
        };
    }
}
