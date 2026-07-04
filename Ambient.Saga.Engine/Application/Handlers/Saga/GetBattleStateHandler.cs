using MediatR;
using Ambient.Saga.Engine.Domain.Rpg.Sagas.TransactionLog;
using Ambient.Saga.Engine.Application.Results.Saga;
using Ambient.Saga.Engine.Domain.Rpg.Battle;
using Ambient.Saga.Engine.Contracts.Cqrs;
using Ambient.Saga.Engine.Application.Queries.Saga;
using Ambient.Domain.Contracts;
using Ambient.Saga.Engine.Domain;

namespace Ambient.Saga.Engine.Application.Handlers.Saga;

/// <summary>
/// Handler for GetBattleStateQuery.
/// Replays battle transactions to determine current combatant states, turn number, and battle status.
/// </summary>
internal sealed class GetBattleStateHandler : IRequestHandler<GetBattleStateQuery, BattleStateResult>
{
    private readonly ISagaInstanceRepository _instanceRepository;
    private readonly IWorld _world;

    public GetBattleStateHandler(
        ISagaInstanceRepository instanceRepository,
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
            // Get Saga instance
            var instance = await _instanceRepository.GetOrCreateInstanceAsync(query.AvatarId, query.SagaRef, ct);

            // Find BattleStarted transaction
            var battleStartedTx = instance.Transactions
                .FirstOrDefault(t => t.TransactionId == query.BattleInstanceId && t.Type == SagaTransactionType.BattleStarted);

            if (battleStartedTx == null)
            {
                System.Diagnostics.Debug.WriteLine($"[GetBattleState] Battle {query.BattleInstanceId} not found");
                return new BattleStateResult { IsActive = false, HasEnded = false };
            }

            // Check if battle ended
            var battleEndedTx = instance.Transactions
                .FirstOrDefault(t => t.Type == SagaTransactionType.BattleEnded &&
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
                .Where(t => t.Type == SagaTransactionType.BattleTurnExecuted &&
                           t.Data.TryGetValue(TransactionDataKeys.BattleTransactionId, out var battleId) &&
                           battleId == query.BattleInstanceId.ToString())
                .OrderBy(t => t.SequenceNumber)
                .ToList();

            var turnNumber = turnTransactions.Count;

            // Determine current battle state
            BattleState battleState;
            SagaTransaction? pendingTellTx = null;
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
                        var wasAvatarTurn = bool.Parse(lastTurn.Data[TransactionDataKeys.IsAvatarTurn]);
                        battleState = wasAvatarTurn ? BattleState.EnemyTurn : BattleState.AvatarTurn;
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

            // Surface the pending tell so a UI resuming mid-reaction (reload, reconnect)
            // can re-show the telegraph instead of wedging
            if (pendingTellTx != null)
            {
                result.PendingTellRefName = pendingTellTx.Data.TryGetValue(TransactionDataKeys.TellRefName, out var tellRef) ? tellRef : null;
                if (pendingTellTx.Data.TryGetValue(TransactionDataKeys.BaseDamage, out var tellDamageStr) &&
                    BattleTransactionHelper.TryParseFloat(tellDamageStr, out var tellDamage))
                {
                    result.PendingTellBaseDamage = tellDamage;
                }
                if (pendingTellTx.Data.TryGetValue(TransactionDataKeys.ReactionWindowMs, out var windowStr) &&
                    int.TryParse(windowStr, out var windowMs))
                {
                    result.PendingTellReactionWindowMs = windowMs;
                }
                result.PendingTellIssuedAtUtc = pendingTellTx.LocalTimestamp;
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

}
