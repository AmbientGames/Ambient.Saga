using Ambient.Domain;
using Ambient.Saga.Engine.Domain;
using Ambient.Saga.Engine.Domain.Rpg.Battle;
using Ambient.Saga.Engine.Domain.Rpg.Sagas.TransactionLog;

namespace Ambient.Saga.Engine.Application.Handlers.Saga;

/// <summary>
/// Builds the BattleTurnExecuted transaction for a resolved reaction, from the
/// SERVER-computed <see cref="ReactionResult"/>. Shared by SubmitReactionHandler
/// (player reacted) and ExecuteBattleTurnHandler (abandoned tell auto-resolved as
/// un-reacted). Client-supplied damage numbers are never persisted.
/// </summary>
internal static class ReactionTransactionFactory
{
    /// <summary>Grace added to the authored reaction window before the server treats a tell as abandoned.</summary>
    internal const int TimeoutGraceMs = 3000;

    /// <summary>Whether the persisted tell's reaction window (plus grace) has passed.</summary>
    internal static bool IsExpired(SagaTransaction tellTx, DateTime utcNow)
    {
        var windowMs = tellTx.Data.TryGetValue(TransactionDataKeys.ReactionWindowMs, out var windowStr) &&
                       int.TryParse(windowStr, out var parsed)
            ? parsed
            : 0;
        // LiteDB round-trips DateTime back as LOCAL kind — normalize to UTC before comparing
        var issuedAtUtc = tellTx.LocalTimestamp.Kind == DateTimeKind.Utc
            ? tellTx.LocalTimestamp
            : tellTx.LocalTimestamp.ToUniversalTime();
        return utcNow > issuedAtUtc.AddMilliseconds(windowMs + TimeoutGraceMs);
    }

    /// <summary>
    /// The battle's unresolved telegraphed enemy attack, or null. A tell is unresolved
    /// while it is the LAST turn transaction (its reaction is always persisted directly
    /// after it).
    /// </summary>
    internal static SagaTransaction? FindUnresolvedTell(SagaInstance instance, Guid battleInstanceId)
    {
        var lastTurnTx = instance.Transactions
            .Where(t => t.Type == SagaTransactionType.BattleTurnExecuted &&
                        t.Data.TryGetValue(TransactionDataKeys.BattleTransactionId, out var battleId) &&
                        battleId == battleInstanceId.ToString())
            .OrderBy(t => t.SequenceNumber)
            .LastOrDefault();

        return lastTurnTx != null &&
               lastTurnTx.Data.TryGetValue(TransactionDataKeys.ActionType, out var actionType) &&
               actionType == "Tell"
            ? lastTurnTx
            : null;
    }

    internal static SagaTransaction Create(
        Guid avatarId,
        Guid battleInstanceId,
        int turnNumber,
        ReactionResult result,
        string tellRefName,
        float baseDamage,
        BattleEngine battleEngine,
        Guid sagaInstanceId)
    {
        var avatar = battleEngine.GetAvatar();
        var enemy = battleEngine.GetEnemy();

        var transaction = new SagaTransaction
        {
            TransactionId = Guid.NewGuid(),
            Type = SagaTransactionType.BattleTurnExecuted,
            AvatarId = avatarId.ToString(),
            Status = TransactionStatus.Pending,
            LocalTimestamp = DateTime.UtcNow,
            Data = new Dictionary<string, string>
            {
                [TransactionDataKeys.BattleTransactionId] = battleInstanceId.ToString(),
                [TransactionDataKeys.ActionType] = "Reaction",
                [TransactionDataKeys.DecisionType] = ActionType.Defend.ToString(), // Reactions are defensive actions
                [TransactionDataKeys.ReactionType] = result.ChosenReaction.ToString(),
                [TransactionDataKeys.IsAvatarTurn] = bool.TrueString,
                [TransactionDataKeys.TurnNumber] = turnNumber.ToString(),

                // Reaction-specific data (server-computed)
                [TransactionDataKeys.TellRefName] = tellRefName,
                [TransactionDataKeys.BaseDamage] = BattleTransactionHelper.FormatFloat(baseDamage),
                [TransactionDataKeys.DamageDealt] = BattleTransactionHelper.FormatFloat(result.FinalDamage), // Damage TO avatar
                [TransactionDataKeys.HealingDone] = "0",
                [TransactionDataKeys.CounterDamage] = BattleTransactionHelper.FormatFloat(result.CounterDamage ?? 0f),
                [TransactionDataKeys.StaminaGained] = BattleTransactionHelper.FormatFloat(result.StaminaGained),
                [TransactionDataKeys.WasOptimal] = result.WasOptimal.ToString(),
                [TransactionDataKeys.TimedOut] = result.TimedOut.ToString(),

                // State after reaction (from the engine, not the client)
                [TransactionDataKeys.Target] = avatar.DisplayName,
                [TransactionDataKeys.TargetHealthAfter] = BattleTransactionHelper.FormatFloat(avatar.Health),
                [TransactionDataKeys.ActorEnergyAfter] = BattleTransactionHelper.FormatFloat(avatar.Stamina),
                [TransactionDataKeys.EnemyHealthAfter] = BattleTransactionHelper.FormatFloat(enemy.Health),

                // Absolute both-sides snapshots — the reconstruction fold's preferred source
                [TransactionDataKeys.AvatarHealthAfterTurn] = BattleTransactionHelper.FormatFloat(avatar.Health),
                [TransactionDataKeys.AvatarEnergyAfterTurn] = BattleTransactionHelper.FormatFloat(avatar.Stamina),
                [TransactionDataKeys.EnemyHealthAfterTurn] = BattleTransactionHelper.FormatFloat(enemy.Health),
                [TransactionDataKeys.EnemyEnergyAfterTurn] = BattleTransactionHelper.FormatFloat(enemy.Stamina),

                // Status effect/modifier snapshots (last snapshot wins during folds)
                [TransactionDataKeys.AvatarStatModifiers] = BattleTransactionHelper.SerializeStatModifiers(avatar.CombatStatModifiers),
                [TransactionDataKeys.EnemyStatModifiers] = BattleTransactionHelper.SerializeStatModifiers(enemy.CombatStatModifiers),
                [TransactionDataKeys.AvatarStatusEffects] = BattleTransactionHelper.SerializeStatusEffects(avatar.ActiveStatusEffects),
                [TransactionDataKeys.EnemyStatusEffects] = BattleTransactionHelper.SerializeStatusEffects(enemy.ActiveStatusEffects),

                [TransactionDataKeys.SagaInstanceId] = sagaInstanceId.ToString()
            }
        };

        var companions = battleEngine.GetCompanions();
        if (companions.Count > 0)
        {
            transaction.Data[TransactionDataKeys.CompanionStatesAfterTurn] =
                BattleTransactionHelper.SerializeCompanionTurnStates(companions);
        }

        return transaction;
    }
}
