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
        Guid playerCombatantId,
        Guid enemyCombatantId,
        string enemyCharacterRef,
        int randomSeed,
        Combatant player,
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
                [TransactionDataKeys.PlayerCombatantId] = playerCombatantId.ToString(),
                [TransactionDataKeys.EnemyCombatantId] = enemyCombatantId.ToString(),
                [TransactionDataKeys.EnemyCharacterRef] = enemyCharacterRef,
                [TransactionDataKeys.RandomSeed] = randomSeed.ToString(),
                [TransactionDataKeys.SagaInstanceId] = sagaInstanceId.ToString(),

                // Avatar stats
                [TransactionDataKeys.PlayerHealth] = player.Health.ToString("F3"),
                [TransactionDataKeys.PlayerEnergy] = player.Stamina.ToString("F3"),
                [TransactionDataKeys.PlayerStrength] = player.Strength.ToString("F3"),
                [TransactionDataKeys.PlayerDefense] = player.Defense.ToString("F3"),
                [TransactionDataKeys.PlayerSpeed] = player.Speed.ToString("F3"),
                [TransactionDataKeys.PlayerMagic] = player.Magic.ToString("F3"),
                [TransactionDataKeys.PlayerAffinity] = player.AffinityRef ?? "",

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
        if (player.Capabilities?.Equipment != null)
        {
            var equipmentRefs = player.Capabilities.Equipment
                .Select(e => $"{e.EquipmentRef}:{e.Condition:F2}")
                .ToList();
            if (equipmentRefs.Count > 0)
                transaction.Data[TransactionDataKeys.PlayerEquipment] = string.Join(",", equipmentRefs);
        }

        // Record avatar's initial equipped slots
        if (player.CombatProfile != null && player.CombatProfile.Count > 0)
        {
            var equippedSlots = player.CombatProfile
                .Select(kvp => $"{kvp.Key}:{kvp.Value}")
                .ToList();
            transaction.Data[TransactionDataKeys.PlayerEquippedSlots] = string.Join(",", equippedSlots);
        }

        // Record avatar's available affinities
        if (playerAffinityRefs != null && playerAffinityRefs.Count > 0)
        {
            transaction.Data[TransactionDataKeys.PlayerAffinities] = string.Join(",", playerAffinityRefs);
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
                [TransactionDataKeys.IsPlayerTurn] = isPlayerTurn.ToString(),
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
                [TransactionDataKeys.PlayerVictory] = playerVictory.ToString(),
                [TransactionDataKeys.Victor] = victorRefName,
                [TransactionDataKeys.Defeated] = defeatedRefName,
                [TransactionDataKeys.SagaInstanceId] = sagaInstanceId.ToString()
            }
        };
    }
}
