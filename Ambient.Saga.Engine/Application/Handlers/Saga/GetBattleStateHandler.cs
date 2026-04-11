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
            if (battleHasEnded && battleEndedTx.Data.TryGetValue(TransactionDataKeys.AvatarVictory, out var victoryStr))
            {
                avatarVictory = bool.Parse(victoryStr);
            }

            // Reconstruct combatants from transactions
            var (avatarCombatant, enemyCombatant, randomSeed, avatarAffinityRefs, enemyCharacterInstanceId) =
                ReconstructCombatants(battleStartedTx, instance);

            // Attach avatar's current capabilities (for equipment change modal)
            // This comes from the live avatar, not the transaction log
            if (query.Avatar?.Capabilities != null)
            {
                avatarCombatant.Capabilities = query.Avatar.Capabilities;
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
            if (battleHasEnded)
            {
                battleState = avatarVictory == true ? BattleState.Victory :
                             avatarVictory == false ? BattleState.Defeat :
                             BattleState.Fled;
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
                    var wasAvatarTurn = bool.Parse(lastTurn.Data[TransactionDataKeys.IsAvatarTurn]);
                    battleState = wasAvatarTurn ? BattleState.EnemyTurn : BattleState.AvatarTurn;
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

                // Check if this is a reaction transaction
                var isReaction = turnTx.Data.TryGetValue(TransactionDataKeys.ActionType, out var actionTypeStr) && actionTypeStr == "Reaction";

                if (isReaction)
                {
                    // Reaction log entry
                    var reactionType = turnTx.Data[TransactionDataKeys.ReactionType];
                    var damage = float.Parse(turnTx.Data[TransactionDataKeys.DamageDealt]);
                    var counterDamage = turnTx.Data.TryGetValue(TransactionDataKeys.CounterDamage, out var counterStr)
                        ? float.Parse(counterStr)
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
                    var damage = float.Parse(turnTx.Data[TransactionDataKeys.DamageDealt]);
                    var healing = float.Parse(turnTx.Data[TransactionDataKeys.HealingDone]);

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

            return new BattleStateResult
            {
                IsActive = !battleHasEnded,
                BattleState = battleState,
                BattleInstanceId = query.BattleInstanceId,
                TurnNumber = turnNumber,
                AvatarCombatant = avatarCombatant,
                EnemyCombatant = enemyCombatant,
                BattleLog = battleLog,
                AvatarVictory = avatarVictory,
                HasEnded = battleHasEnded,
                AvatarAffinityRefs = avatarAffinityRefs,
                EnemyCharacterInstanceId = enemyCharacterInstanceId
            };
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

    private (Combatant avatar, Combatant enemy, int randomSeed, List<string> playerAffinityRefs, Guid enemyCharacterInstanceId)
        ReconstructCombatants(SagaTransaction battleStartedTx, SagaInstance instance)
    {
        // Parse initial state from BattleStarted transaction
        var avatarCombatant = new Combatant
        {
            RefName = battleStartedTx.Data[TransactionDataKeys.AvatarCombatantId],
            DisplayName = "Avatar",
            Health = float.Parse(battleStartedTx.Data[TransactionDataKeys.AvatarHealth]),
            Stamina = float.Parse(battleStartedTx.Data[TransactionDataKeys.AvatarEnergy]),
            Strength = float.Parse(battleStartedTx.Data[TransactionDataKeys.AvatarStrength]),
            Defense = float.Parse(battleStartedTx.Data[TransactionDataKeys.AvatarDefense]),
            Speed = float.Parse(battleStartedTx.Data[TransactionDataKeys.AvatarSpeed]),
            Magic = float.Parse(battleStartedTx.Data[TransactionDataKeys.AvatarMagic]),
            AffinityRef = battleStartedTx.Data.TryGetValue(TransactionDataKeys.AvatarAffinity, out var pAff) ? pAff : null,
            CombatProfile = new Dictionary<string, string>()
        };

        var enemyCharacterRef = battleStartedTx.Data[TransactionDataKeys.EnemyCharacterRef];
        var enemyCharacter = _world.GetCharacterByRefName(enemyCharacterRef);
        var enemyCombatant = new Combatant
        {
            RefName = enemyCharacterRef,
            DisplayName = enemyCharacter?.DisplayName ?? "Enemy",
            Health = float.Parse(battleStartedTx.Data[TransactionDataKeys.EnemyHealth]),
            Stamina = float.Parse(battleStartedTx.Data[TransactionDataKeys.EnemyEnergy]),
            Strength = float.Parse(battleStartedTx.Data[TransactionDataKeys.EnemyStrength]),
            Defense = float.Parse(battleStartedTx.Data[TransactionDataKeys.EnemyDefense]),
            Speed = float.Parse(battleStartedTx.Data[TransactionDataKeys.EnemySpeed]),
            Magic = float.Parse(battleStartedTx.Data[TransactionDataKeys.EnemyMagic]),
            AffinityRef = battleStartedTx.Data.TryGetValue(TransactionDataKeys.EnemyAffinity, out var eAff) ? eAff : null,
            CombatProfile = new Dictionary<string, string>()
        };

        // Parse equipment and equipped slots
        if (battleStartedTx.Data.TryGetValue(TransactionDataKeys.AvatarEquippedSlots, out var avatarSlots))
        {
            foreach (var slot in avatarSlots.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = slot.Split(':');
                if (parts.Length >= 2)
                {
                    avatarCombatant.CombatProfile[parts[0]] = parts[1];
                }
            }
        }

        if (battleStartedTx.Data.TryGetValue(TransactionDataKeys.EnemyEquippedSlots, out var enemySlots))
        {
            foreach (var slot in enemySlots.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = slot.Split(':');
                if (parts.Length >= 2)
                {
                    enemyCombatant.CombatProfile[parts[0]] = parts[1];
                }
            }
        }

        // Apply all turn transactions to update combatant states
        var turnTransactions = instance.Transactions
            .Where(t => t.Type == SagaTransactionType.BattleTurnExecuted &&
                       t.Data.TryGetValue(TransactionDataKeys.BattleTransactionId, out var battleId) &&
                       battleId == battleStartedTx.TransactionId.ToString())
            .OrderBy(t => t.SequenceNumber)
            .ToList();

        foreach (var turnTx in turnTransactions)
        {
            var isAvatarTurn = bool.Parse(turnTx.Data[TransactionDataKeys.IsAvatarTurn]);

            // Check if this is a reaction transaction (special handling)
            var isReaction = turnTx.Data.TryGetValue(TransactionDataKeys.ActionType, out var actionType) && actionType == "Reaction";

            if (isReaction)
            {
                // Reaction: avatar defends against enemy attack
                // DamageDealt is damage TO avatar, CounterDamage is damage TO enemy
                var targetHealthAfter = float.Parse(turnTx.Data[TransactionDataKeys.TargetHealthAfter]);
                var actorEnergyAfter = float.Parse(turnTx.Data[TransactionDataKeys.ActorEnergyAfter]);

                avatarCombatant.Health = targetHealthAfter;
                avatarCombatant.Stamina = actorEnergyAfter;

                // Apply counter damage to enemy if present
                if (turnTx.Data.TryGetValue(TransactionDataKeys.EnemyHealthAfter, out var enemyHealthStr))
                {
                    enemyCombatant.Health = float.Parse(enemyHealthStr);
                }
            }
            else
            {
                // Normal turn: actor attacks target
                var combatant = isAvatarTurn ? avatarCombatant : enemyCombatant;
                var target = isAvatarTurn ? enemyCombatant : avatarCombatant;

                // Update health/energy from turn results
                var targetHealthAfter = float.Parse(turnTx.Data[TransactionDataKeys.TargetHealthAfter]);
                var actorEnergyAfter = float.Parse(turnTx.Data[TransactionDataKeys.ActorEnergyAfter]);

                combatant.Stamina = actorEnergyAfter;
                target.Health = targetHealthAfter;

                // Update equipment/affinity from snapshots
                if (turnTx.Data.TryGetValue(TransactionDataKeys.LoadoutSlotSnapshot, out var loadoutSnapshot))
                {
                    combatant.CombatProfile.Clear();
                    foreach (var slot in loadoutSnapshot.Split(',', StringSplitOptions.RemoveEmptyEntries))
                    {
                        var parts = slot.Split(':');
                        if (parts.Length >= 2)
                        {
                            combatant.CombatProfile[parts[0]] = parts[1];
                        }
                    }
                }

                if (turnTx.Data.TryGetValue(TransactionDataKeys.AffinitySnapshot, out var affinity))
                {
                    combatant.AffinityRef = affinity;
                }
            }
        }

        var randomSeed = int.Parse(battleStartedTx.Data[TransactionDataKeys.RandomSeed]);
        var avatarAffinityRefs = battleStartedTx.Data.TryGetValue(TransactionDataKeys.AvatarAffinities, out var affinities)
            ? affinities.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList()
            : new List<string>();
        var enemyCharacterInstanceId = Guid.Parse(battleStartedTx.Data[TransactionDataKeys.EnemyCombatantId]);

        return (avatarCombatant, enemyCombatant, randomSeed, avatarAffinityRefs, enemyCharacterInstanceId);
    }
}
