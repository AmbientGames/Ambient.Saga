using Ambient.Domain;
using Ambient.Domain.Contracts;
using Ambient.Rpg.Engine.Domain;
using Ambient.Rpg.Engine.Domain.Battle;
using Ambient.Rpg.Engine.Domain.Arcs.TransactionLog;

namespace Ambient.Rpg.Engine.Application.Handlers.Arcs;

/// <summary>
/// Rebuilds mid-battle combatant state from the persisted battle transactions:
/// initial stats from BattleStarted, then every BattleTurnExecuted result folded in
/// (data-driven — no turns are re-simulated). Shared by ExecuteBattleTurnHandler and
/// GetBattleStateHandler so command replay and state queries can never disagree.
/// </summary>
internal static class BattleStateReconstructor
{
    internal static (Combatant avatar, Combatant enemy, int randomSeed, List<string> avatarAffinityRefs, Guid enemyCharacterInstanceId, List<Combatant> companions)
        Reconstruct(ArcTransaction battleStartedTx, ArcInstance instance, IWorld world)
    {
        // Parse initial state from BattleStarted transaction
        var avatarCombatant = new Combatant
        {
            RefName = battleStartedTx.Data[TransactionDataKeys.AvatarCombatantId],
            DisplayName = "Avatar",  // Overridden by UI
            Health = ParseFloat(battleStartedTx, TransactionDataKeys.AvatarHealth),
            Stamina = ParseFloat(battleStartedTx, TransactionDataKeys.AvatarEnergy),
            Strength = ParseFloat(battleStartedTx, TransactionDataKeys.AvatarStrength),
            Defense = ParseFloat(battleStartedTx, TransactionDataKeys.AvatarDefense),
            Speed = ParseFloat(battleStartedTx, TransactionDataKeys.AvatarSpeed),
            Magic = ParseFloat(battleStartedTx, TransactionDataKeys.AvatarMagic),
            AffinityRef = battleStartedTx.Data.TryGetValue(TransactionDataKeys.AvatarAffinity, out var pAff) ? pAff : null,
            CombatProfile = new Dictionary<string, string>()
        };

        var enemyCharacterRef = battleStartedTx.Data[TransactionDataKeys.EnemyCharacterRef];
        var enemyCharacter = world.GetCharacterByRefName(enemyCharacterRef);
        var enemyCombatant = new Combatant
        {
            RefName = enemyCharacterRef,
            DisplayName = enemyCharacter?.DisplayName ?? "Enemy",
            // Bias isn't persisted per-turn — it's immutable template data, so the
            // live opening turn and every reconstructed turn must agree on it
            ArchetypeBias = enemyCharacter?.ArchetypeBias,
            Health = ParseFloat(battleStartedTx, TransactionDataKeys.EnemyHealth),
            Stamina = ParseFloat(battleStartedTx, TransactionDataKeys.EnemyEnergy),
            Strength = ParseFloat(battleStartedTx, TransactionDataKeys.EnemyStrength),
            Defense = ParseFloat(battleStartedTx, TransactionDataKeys.EnemyDefense),
            Speed = ParseFloat(battleStartedTx, TransactionDataKeys.EnemySpeed),
            Magic = ParseFloat(battleStartedTx, TransactionDataKeys.EnemyMagic),
            AffinityRef = battleStartedTx.Data.TryGetValue(TransactionDataKeys.EnemyAffinity, out var eAff) ? eAff : null,
            CombatProfile = new Dictionary<string, string>()
        };

        ParseSlots(battleStartedTx, TransactionDataKeys.AvatarEquippedSlots, avatarCombatant);
        ParseSlots(battleStartedTx, TransactionDataKeys.EnemyEquippedSlots, enemyCombatant);

        // Rebuild the companion roster recorded at battle start (may be empty)
        var companions = battleStartedTx.Data.TryGetValue(TransactionDataKeys.Companions, out var companionsJson) &&
                         !string.IsNullOrEmpty(companionsJson)
            ? BattleTransactionHelper.DeserializeCompanions(companionsJson)
            : new List<Combatant>();

        // Fold every turn result into the combatants
        var turnTransactions = instance.Transactions
            .Where(t => t.Type == ArcTransactionType.BattleTurnExecuted &&
                       t.Data.TryGetValue(TransactionDataKeys.BattleTransactionId, out var battleId) &&
                       battleId == battleStartedTx.TransactionId.ToString())
            .OrderBy(t => t.SequenceNumber)
            .ToList();

        foreach (var turnTx in turnTransactions)
        {
            var isAvatarTurn = bool.Parse(turnTx.Data[TransactionDataKeys.IsAvatarTurn]);

            // Reaction transactions (avatar reacting to a telegraphed enemy attack)
            // record avatar-side results plus any counter damage to the enemy
            var isReaction = turnTx.Data.TryGetValue(TransactionDataKeys.ActionType, out var actionType) && actionType == "Reaction";

            // Preferred path: absolute post-turn snapshots of BOTH sides, written on every
            // turn (including tells and reactions) since the round-3 fix. No inference —
            // failure events (empty Target), companion turns, and self-heals all fold
            // correctly.
            if (turnTx.Data.TryGetValue(TransactionDataKeys.AvatarHealthAfterTurn, out var avatarHealthAbs) &&
                turnTx.Data.TryGetValue(TransactionDataKeys.EnemyHealthAfterTurn, out var enemyHealthAbs) &&
                BattleTransactionHelper.TryParseFloat(avatarHealthAbs, out var avatarHealthValue) &&
                BattleTransactionHelper.TryParseFloat(enemyHealthAbs, out var enemyHealthValue))
            {
                avatarCombatant.Health = avatarHealthValue;
                enemyCombatant.Health = enemyHealthValue;

                if (turnTx.Data.TryGetValue(TransactionDataKeys.AvatarEnergyAfterTurn, out var avatarEnergyAbs) &&
                    BattleTransactionHelper.TryParseFloat(avatarEnergyAbs, out var avatarEnergyValue))
                {
                    avatarCombatant.Stamina = avatarEnergyValue;
                }
                if (turnTx.Data.TryGetValue(TransactionDataKeys.EnemyEnergyAfterTurn, out var enemyEnergyAbs) &&
                    BattleTransactionHelper.TryParseFloat(enemyEnergyAbs, out var enemyEnergyValue))
                {
                    enemyCombatant.Stamina = enemyEnergyValue;
                }
            }
            else if (isReaction)
            {
                // Legacy reaction fallback (pre-fix transactions without absolute snapshots)
                avatarCombatant.Health = ParseFloat(turnTx, TransactionDataKeys.TargetHealthAfter);
                avatarCombatant.Stamina = ParseFloat(turnTx, TransactionDataKeys.ActorEnergyAfter);

                if (turnTx.Data.TryGetValue(TransactionDataKeys.EnemyHealthAfter, out var enemyHealthStr) &&
                    BattleTransactionHelper.TryParseFloat(enemyHealthStr, out var enemyHealthAfter))
                {
                    enemyCombatant.Health = enemyHealthAfter;
                }
                continue;
            }
            else
            {
                // Legacy fallback for battles persisted before the absolute snapshots:
                // infer who took the hit from the recorded Target name (companion turns are
                // IsAvatarTurn=false but hit the ENEMY, not the avatar), then turn parity
                var targetHealthAfter = ParseFloat(turnTx, TransactionDataKeys.TargetHealthAfter);
                var targetIsEnemy = turnTx.Data.TryGetValue(TransactionDataKeys.Target, out var targetName) && !string.IsNullOrEmpty(targetName)
                    ? targetName == enemyCombatant.DisplayName || targetName == enemyCombatant.RefName
                    : isAvatarTurn;
                var target = targetIsEnemy ? enemyCombatant : avatarCombatant;
                target.Health = targetHealthAfter;

                var legacyActor = ResolveActor(turnTx, isAvatarTurn, avatarCombatant, enemyCombatant);
                if (legacyActor != null)
                {
                    legacyActor.Stamina = ParseFloat(turnTx, TransactionDataKeys.ActorEnergyAfter);
                }
            }

            // Whose loadout/affinity: the actor. Companion turns (non-avatar actor that
            // isn't the enemy) are skipped — companions aren't reconstructed here.
            var actor = ResolveActor(turnTx, isAvatarTurn, avatarCombatant, enemyCombatant);

            if (actor != null)
            {
                if (turnTx.Data.TryGetValue(TransactionDataKeys.LoadoutSlotSnapshot, out var loadoutSnapshot))
                {
                    actor.CombatProfile.Clear();
                    foreach (var slot in loadoutSnapshot.Split(',', StringSplitOptions.RemoveEmptyEntries))
                    {
                        var parts = slot.Split(':');
                        if (parts.Length >= 2)
                        {
                            actor.CombatProfile[parts[0]] = parts[1];
                        }
                    }
                }

                if (turnTx.Data.TryGetValue(TransactionDataKeys.AffinitySnapshot, out var affinity))
                {
                    actor.AffinityRef = affinity;
                }
            }

            // Buff/debuff snapshots cover both sides (a spell may modify either
            // combatant on any turn) — last snapshot wins
            if (turnTx.Data.TryGetValue(TransactionDataKeys.AvatarStatModifiers, out var avatarMods))
            {
                BattleTransactionHelper.ApplyStatModifierSnapshot(avatarCombatant, avatarMods);
            }
            if (turnTx.Data.TryGetValue(TransactionDataKeys.EnemyStatModifiers, out var enemyMods))
            {
                BattleTransactionHelper.ApplyStatModifierSnapshot(enemyCombatant, enemyMods);
            }

            // Status effects survive the same way — last snapshot wins
            if (turnTx.Data.TryGetValue(TransactionDataKeys.AvatarStatusEffects, out var avatarEffects))
            {
                BattleTransactionHelper.ApplyStatusEffectSnapshot(avatarCombatant, avatarEffects);
            }
            if (turnTx.Data.TryGetValue(TransactionDataKeys.EnemyStatusEffects, out var enemyEffects))
            {
                BattleTransactionHelper.ApplyStatusEffectSnapshot(enemyCombatant, enemyEffects);
            }

            // Defensive stance (Defend/Adjust) survives the boundary too — the
            // Defend halving in the next command keys off IsDefending, which
            // used to be silently dropped here (enemy Defend never fired)
            if (turnTx.Data.TryGetValue(BattleTransactionHelper.AvatarStanceAfterTurnKey, out var avatarStance))
            {
                BattleTransactionHelper.ApplyStanceSnapshot(avatarCombatant, avatarStance);
            }
            if (turnTx.Data.TryGetValue(BattleTransactionHelper.EnemyStanceAfterTurnKey, out var enemyStance))
            {
                BattleTransactionHelper.ApplyStanceSnapshot(enemyCombatant, enemyStance);
            }

            // Companion health/energy/effects — last snapshot wins
            if (companions.Count > 0 &&
                turnTx.Data.TryGetValue(TransactionDataKeys.CompanionStatesAfterTurn, out var companionStates) &&
                !string.IsNullOrEmpty(companionStates))
            {
                BattleTransactionHelper.ApplyCompanionTurnStates(companions, companionStates);
            }
        }

        var randomSeed = int.Parse(battleStartedTx.Data[TransactionDataKeys.RandomSeed]);
        var avatarAffinityRefs = battleStartedTx.Data.TryGetValue(TransactionDataKeys.AvatarAffinities, out var affinities)
            ? affinities.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList()
            : new List<string>();
        var enemyCharacterInstanceId = Guid.Parse(battleStartedTx.Data[TransactionDataKeys.EnemyCombatantId]);

        return (avatarCombatant, enemyCombatant, randomSeed, avatarAffinityRefs, enemyCharacterInstanceId, companions);
    }

    /// <summary>
    /// Folds recorded mid-battle consumable use back onto the freshly attached
    /// capabilities (absolute post-use quantities, last snapshot wins per consumable).
    /// Call AFTER assigning <c>Capabilities</c> — the log carries stats/slots only, so
    /// the attached collection holds the persisted PRE-battle quantities and, without
    /// this fold, a mid-battle reload resurrected every potion drunk during the fight
    /// (the equipment-condition fold got this fix earlier; consumables didn't).
    /// </summary>
    internal static void ApplyRecordedConsumableQuantities(ArcInstance instance, Guid battleInstanceId, Combatant avatarCombatant)
    {
        if (avatarCombatant.Capabilities?.Consumables == null)
            return;

        var usageTxs = instance.Transactions
            .Where(t => t.Type == ArcTransactionType.BattleTurnExecuted &&
                        t.Data.TryGetValue(TransactionDataKeys.BattleTransactionId, out var battleId) &&
                        battleId == battleInstanceId.ToString() &&
                        t.Data.TryGetValue(TransactionDataKeys.IsAvatarTurn, out var isAvatarStr) &&
                        bool.TryParse(isAvatarStr, out var isAvatar) && isAvatar &&
                        t.Data.TryGetValue(TransactionDataKeys.DecisionType, out var decision) &&
                        decision == nameof(ActionType.UseConsumable) &&
                        t.Data.ContainsKey(TransactionDataKeys.ItemRefName) &&
                        t.Data.ContainsKey(TransactionDataKeys.RemainingQuantity))
            .OrderBy(t => t.SequenceNumber);

        foreach (var usageTx in usageTxs)
        {
            if (!int.TryParse(usageTx.Data[TransactionDataKeys.RemainingQuantity], out var remaining))
                continue;

            var entry = avatarCombatant.Capabilities.Consumables
                .FirstOrDefault(c => c.ConsumableRef == usageTx.Data[TransactionDataKeys.ItemRefName]);
            if (entry != null)
            {
                entry.Quantity = remaining;
            }
        }
    }

    private static void ParseSlots(ArcTransaction battleStartedTx, string dataKey, Combatant combatant)
    {
        if (!battleStartedTx.Data.TryGetValue(dataKey, out var slots))
            return;

        foreach (var slot in slots.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = slot.Split(':');
            if (parts.Length >= 2)
            {
                combatant.CombatProfile[parts[0]] = parts[1];
            }
        }
    }

    /// <summary>
    /// The turn's actor: the avatar on avatar turns; the enemy on non-avatar turns unless
    /// the recorded Actor name is some other party member (companion turns aren't
    /// reconstructed here).
    /// </summary>
    private static Combatant? ResolveActor(ArcTransaction turnTx, bool isAvatarTurn, Combatant avatarCombatant, Combatant enemyCombatant)
    {
        if (isAvatarTurn)
            return avatarCombatant;

        if (!turnTx.Data.TryGetValue(TransactionDataKeys.Actor, out var actorName) ||
            string.IsNullOrEmpty(actorName) ||
            actorName == enemyCombatant.DisplayName ||
            actorName == enemyCombatant.RefName)
        {
            return enemyCombatant;
        }

        return null;
    }

    private static float ParseFloat(ArcTransaction tx, string dataKey)
    {
        var raw = tx.Data.TryGetValue(dataKey, out var text) ? text : null;
        if (!BattleTransactionHelper.TryParseFloat(raw, out var value))
        {
            throw new FormatException($"Battle transaction {tx.TransactionId} has unparseable '{dataKey}' value '{raw}'");
        }
        return value;
    }
}
