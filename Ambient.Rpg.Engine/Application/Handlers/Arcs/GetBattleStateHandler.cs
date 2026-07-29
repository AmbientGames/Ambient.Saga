using MediatR;
using Ambient.Rpg.Engine.Domain.Arcs.TransactionLog;
using Ambient.Rpg.Engine.Application.Results.Arcs;
using Ambient.Rpg.Engine.Domain.Battle;
using Ambient.Rpg.Engine.Contracts.Cqrs;
using Ambient.Rpg.Engine.Application.Queries.Arcs;
using Ambient.Domain.Contracts;
using Ambient.Rpg.Engine.Domain;

namespace Ambient.Rpg.Engine.Application.Handlers.Arcs;

/// <summary>
/// Handler for GetBattleStateQuery.
/// Replays battle transactions to determine current combatant states, turn number, and battle status.
/// </summary>
internal sealed class GetBattleStateHandler : IRequestHandler<GetBattleStateQuery, BattleStateResult>
{
    private readonly IArcInstanceRepository _instanceRepository;
    private readonly IWorld _world;

    public GetBattleStateHandler(
        IArcInstanceRepository instanceRepository,
        IWorld world)
    {
        _instanceRepository = instanceRepository;
        _world = world;
    }

    public async Task<BattleStateResult> Handle(GetBattleStateQuery query, CancellationToken ct)
    {
        System.Diagnostics.Debug.WriteLine($"[GetBattleState] Querying battle state for battle {query.BattleInstanceId}");

        try
        {
            // Get Arc instance
            var instance = await _instanceRepository.GetOrCreateInstanceAsync(query.AvatarId, query.ArcRef, ct);

            // Find BattleStarted transaction
            var battleStartedTx = instance.Transactions
                .FirstOrDefault(t => t.TransactionId == query.BattleInstanceId && t.Type == ArcTransactionType.BattleStarted);

            if (battleStartedTx == null)
            {
                System.Diagnostics.Debug.WriteLine($"[GetBattleState] Battle {query.BattleInstanceId} not found");
                return new BattleStateResult { IsActive = false, HasEnded = false };
            }

            // Check if battle ended
            var battleEndedTx = instance.Transactions
                .FirstOrDefault(t => t.Type == ArcTransactionType.BattleEnded &&
                                    t.Data.TryGetValue(TransactionDataKeys.BattleTransactionId, out var battleId) &&
                                    battleId == query.BattleInstanceId.ToString());

            var battleHasEnded = battleEndedTx != null;
            bool? avatarVictory = null;
            BattleState? endedOutcome = null;
            if (battleHasEnded)
            {
                if (battleEndedTx!.Data.TryGetValue(TransactionDataKeys.AvatarVictory, out var victoryStr))
                {
                    avatarVictory = bool.Parse(victoryStr);
                }

                // Distinct outcome (Victory/Defeat/Fled) written since the round-3 fix;
                // legacy transactions only carry AvatarVictory (fled looked like defeat)
                if (battleEndedTx.Data.TryGetValue(TransactionDataKeys.BattleOutcome, out var outcomeStr) &&
                    Enum.TryParse<BattleState>(outcomeStr, out var parsedOutcome))
                {
                    endedOutcome = parsedOutcome;
                    if (parsedOutcome == BattleState.Fled)
                    {
                        avatarVictory = null; // fled is neither victory nor defeat
                    }
                }
            }

            // Reconstruct combatants from transactions
            var (avatarCombatant, enemyCombatant, randomSeed, avatarAffinityRefs, enemyCharacterInstanceId, companions) =
                BattleStateReconstructor.Reconstruct(battleStartedTx, instance, _world);

            // Attach avatar's current capabilities (for equipment change modal)
            // This comes from the live avatar, not the transaction log
            if (query.Avatar?.Capabilities != null)
            {
                avatarCombatant.Capabilities = query.Avatar.Capabilities;
            }
            if (query.Avatar != null)
            {
                avatarCombatant.ArchetypeBias = query.Avatar.ArchetypeBias;
            }

            // Get all turn transactions
            var turnTransactions = instance.Transactions
                .Where(t => t.Type == ArcTransactionType.BattleTurnExecuted &&
                           t.Data.TryGetValue(TransactionDataKeys.BattleTransactionId, out var battleId) &&
                           battleId == query.BattleInstanceId.ToString())
                .OrderBy(t => t.SequenceNumber)
                .ToList();

            var turnNumber = turnTransactions.Count;

            // Determine current battle state
            BattleState battleState;
            ArcTransaction? pendingTellTx = null;
            if (battleHasEnded)
            {
                battleState = endedOutcome ??
                             (avatarVictory == true ? BattleState.Victory :
                              avatarVictory == false ? BattleState.Defeat :
                              BattleState.Fled);
            }
            else
            {
                // Last turn determines whose turn it is next
                if (turnTransactions.Count == 0)
                {
                    // No turns executed yet (shouldn't happen - StartBattle executes enemy's first turn)
                    battleState = BattleState.EnemyTurn;
                }
                else
                {
                    var lastTurn = turnTransactions.Last();
                    var lastActionType = lastTurn.Data.TryGetValue(TransactionDataKeys.ActionType, out var at) ? at : null;

                    if (lastActionType == "Tell")
                    {
                        // A telegraphed enemy attack is pending — the player must react
                        battleState = BattleState.AwaitingReaction;
                        pendingTellTx = lastTurn;
                    }
                    else if (lastActionType == "Reaction")
                    {
                        // The reaction RESOLVED the enemy's turn — the avatar acts next.
                        // (Reactions are recorded IsAvatarTurn=true; naive parity said
                        // EnemyTurn, wedging UIs that drive from this query.)
                        battleState = BattleState.AvatarTurn;
                    }
                    else
                    {
                        // Battle is ongoing and the last turn is neither a tell nor a reaction.
                        // Companion/enemy turns are executed and committed in the SAME command as
                        // the avatar's action — and since FAILED avatar actions (no energy, stun,
                        // rooted flee, failed flee roll) consume the turn too, every committed
                        // avatar action is followed by the enemy's response (or by a tell /
                        // reaction, handled above). The last committed turn here is therefore the
                        // enemy's, and the avatar is up next. The old parity ("last was avatar =>
                        // EnemyTurn") reported EnemyTurn after a failed action and wedged the UI
                        // forever.
                        battleState = BattleState.AvatarTurn;
                    }
                }
            }

            // Build battle log from transactions
            var battleLog = new List<string>
            {
                "=== BATTLE START ===",
                $"{avatarCombatant.DisplayName} vs {enemyCombatant.DisplayName}!"
            };

            foreach (var turnTx in turnTransactions)
            {
                var isAvatarTurn = bool.Parse(turnTx.Data[TransactionDataKeys.IsAvatarTurn]);

                // Check if this is a reaction or tell transaction
                var isReaction = turnTx.Data.TryGetValue(TransactionDataKeys.ActionType, out var actionTypeStr) && actionTypeStr == "Reaction";
                var isTell = actionTypeStr == "Tell";

                if (isTell)
                {
                    battleLog.Add($"{enemyCombatant.DisplayName} telegraphs an attack!");
                    continue;
                }

                if (isReaction)
                {
                    // Reaction log entry
                    var reactionType = turnTx.Data[TransactionDataKeys.ReactionType];
                    BattleTransactionHelper.TryParseFloat(turnTx.Data[TransactionDataKeys.DamageDealt], out var damage);
                    var counterDamage = turnTx.Data.TryGetValue(TransactionDataKeys.CounterDamage, out var counterStr) &&
                                        BattleTransactionHelper.TryParseFloat(counterStr, out var parsedCounter)
                        ? parsedCounter
                        : 0f;
                    var wasOptimal = turnTx.Data.TryGetValue(TransactionDataKeys.WasOptimal, out var optStr) && bool.Parse(optStr);
                    var timedOut = turnTx.Data.TryGetValue(TransactionDataKeys.TimedOut, out var timeStr) && bool.Parse(timeStr);

                    if (timedOut)
                    {
                        battleLog.Add($"{avatarCombatant.DisplayName} failed to react - took {damage:F1} damage");
                    }
                    else if (damage == 0)
                    {
                        battleLog.Add($"{avatarCombatant.DisplayName} {reactionType}d - avoided all damage!");
                    }
                    else
                    {
                        var optimalTag = wasOptimal ? " (optimal!)" : "";
                        battleLog.Add($"{avatarCombatant.DisplayName} {reactionType}d - took {damage:F1} damage{optimalTag}");
                    }

                    if (counterDamage > 0)
                    {
                        battleLog.Add($"Counter-attack dealt {counterDamage:F1} damage to {enemyCombatant.DisplayName}!");
                    }
                }
                else
                {
                    // Normal turn log entry
                    var actor = isAvatarTurn ? avatarCombatant.DisplayName : enemyCombatant.DisplayName;
                    var target = isAvatarTurn ? enemyCombatant.DisplayName : avatarCombatant.DisplayName;
                    var actionType = Enum.Parse<ActionType>(turnTx.Data[TransactionDataKeys.DecisionType]);
                    BattleTransactionHelper.TryParseFloat(turnTx.Data[TransactionDataKeys.DamageDealt], out var damage);
                    BattleTransactionHelper.TryParseFloat(turnTx.Data[TransactionDataKeys.HealingDone], out var healing);

                    if (damage > 0)
                    {
                        battleLog.Add($"{actor} used {actionType} - dealt {damage:F1} damage to {target}");
                    }
                    else if (healing > 0)
                    {
                        battleLog.Add($"{actor} used {actionType} - healed {healing:F1}");
                    }
                    else
                    {
                        battleLog.Add($"{actor} used {actionType}");
                    }
                }
            }

            if (battleHasEnded)
            {
                battleLog.Add(avatarVictory == true ? "\n=== AVATAR WINS! ===" :
                             avatarVictory == false ? "\n=== OPPONENT WINS! ===" :
                             "\n=== FLED FROM BATTLE ===");
            }

            System.Diagnostics.Debug.WriteLine($"[GetBattleState] Battle state: {battleState}, Turn: {turnNumber}, Ended: {battleHasEnded}");

            var result = new BattleStateResult
            {
                IsActive = !battleHasEnded,
                BattleState = battleState,
                BattleInstanceId = query.BattleInstanceId,
                TurnNumber = turnNumber,
                AvatarCombatant = avatarCombatant,
                EnemyCombatant = enemyCombatant,
                Companions = companions,
                BattleLog = battleLog,
                AvatarVictory = avatarVictory,
                HasEnded = battleHasEnded,
                AvatarAffinityRefs = avatarAffinityRefs,
                EnemyCharacterInstanceId = enemyCharacterInstanceId
            };

            // Surface the pending tell so a UI resuming mid-reaction (reload, reconnect,
            // or window-close-then-re-engage) can re-show the telegraph instead of wedging.
            // Populate BOTH field shapes off the same tell transaction, the single source
            // of truth:
            //   - PendingTellRefName / PendingTellBaseDamage / PendingTellReactionWindowMs /
            //     PendingTellIssuedAtUtc: the metadata the tell-expiry and real-content
            //     battle tests read.
            //   - IsAwaitingReaction / PendingTellText / PendingReactionWindowMs /
            //     PendingTellRef: the fields BattleModal.RefreshBattleStateAsync actually
            //     consumes to enter the reaction phase. These were never set, so a resumed
            //     mid-tell battle rendered "Enemy's turn..." with no reaction buttons and no
            //     way to advance — a permanent soft-lock (the enemy stayed unfightable and
            //     the server's expired-tell auto-resolve could never be triggered).
            if (pendingTellTx != null)
            {
                var tellRefName = pendingTellTx.Data.TryGetValue(TransactionDataKeys.TellRefName, out var tellRef) ? tellRef : null;
                result.PendingTellRefName = tellRefName;
                result.PendingTellRef = tellRefName;
                if (pendingTellTx.Data.TryGetValue(TransactionDataKeys.BaseDamage, out var tellDamageStr) &&
                    BattleTransactionHelper.TryParseFloat(tellDamageStr, out var tellDamage))
                {
                    result.PendingTellBaseDamage = tellDamage;
                }
                if (pendingTellTx.Data.TryGetValue(TransactionDataKeys.ReactionWindowMs, out var windowStr) &&
                    int.TryParse(windowStr, out var windowMs))
                {
                    result.PendingTellReactionWindowMs = windowMs;
                    result.PendingReactionWindowMs = windowMs;
                }
                result.PendingTellIssuedAtUtc = pendingTellTx.LocalTimestamp;

                result.IsAwaitingReaction = true;
                result.PendingTellText = ResolvePendingTellText(pendingTellTx, tellRefName);
            }

            return result;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[GetBattleState] ERROR: {ex.Message}\n{ex.StackTrace}");
            return new BattleStateResult
            {
                IsActive = false,
                HasEnded = false,
                ErrorMessage = $"Failed to reconstruct battle state: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// The narrative tell text for a pending telegraphed attack. Mid-battle tells
    /// (ExecuteBattleTurn) persist TellText directly, so it is used when present.
    /// StartBattle's opening tell persists only the tell ref (not the text), so fall
    /// back to resolving the text from the world's authored AttackTells by ref — the
    /// same list RegisterTellsFromWorld reads.
    /// </summary>
    private string? ResolvePendingTellText(ArcTransaction tellTx, string? tellRefName)
    {
        if (tellTx.Data.TryGetValue(TransactionDataKeys.TellText, out var storedText) &&
            !string.IsNullOrEmpty(storedText))
        {
            return storedText;
        }

        if (string.IsNullOrEmpty(tellRefName))
            return null;

        return _world.Gameplay?.AttackTells?
            .FirstOrDefault(t => t.RefName == tellRefName)?.TellText;
    }
}
