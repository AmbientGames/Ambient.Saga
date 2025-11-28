using Ambient.Domain.DefinitionExtensions;
using MediatR;
using Ambient.SagaEngine.Domain.Rpg.Sagas.TransactionLog;
using Ambient.SagaEngine.Application.Results.Saga;
using Ambient.SagaEngine.Domain.Rpg.Battle;
using Ambient.SagaEngine.Contracts.Cqrs;
using Ambient.SagaEngine.Application.Queries.Saga;

namespace Ambient.SagaEngine.Application.Handlers.Saga;

/// <summary>
/// Handler for GetBattleStateQuery.
/// Replays battle transactions to determine current combatant states, turn number, and battle status.
/// </summary>
internal sealed class GetBattleStateHandler : IRequestHandler<GetBattleStateQuery, BattleStateResult>
{
    private readonly ISagaInstanceRepository _instanceRepository;
    private readonly World _world;

    public GetBattleStateHandler(
        ISagaInstanceRepository instanceRepository,
        World world)
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
                                    t.Data.TryGetValue("BattleTransactionId", out var battleId) &&
                                    battleId == query.BattleInstanceId.ToString());

            var battleHasEnded = battleEndedTx != null;
            bool? playerVictory = null;
            if (battleHasEnded && battleEndedTx.Data.TryGetValue("PlayerVictory", out var victoryStr))
            {
                playerVictory = bool.Parse(victoryStr);
            }

            // Reconstruct combatants from transactions
            var (playerCombatant, enemyCombatant, randomSeed, playerAffinityRefs, enemyCharacterInstanceId) =
                ReconstructCombatants(battleStartedTx, instance);

            // Get all turn transactions
            var turnTransactions = instance.Transactions
                .Where(t => t.Type == SagaTransactionType.BattleTurnExecuted &&
                           t.Data.TryGetValue("BattleTransactionId", out var battleId) &&
                           battleId == query.BattleInstanceId.ToString())
                .OrderBy(t => t.SequenceNumber)
                .ToList();

            var turnNumber = turnTransactions.Count;

            // Determine current battle state
            BattleState battleState;
            if (battleHasEnded)
            {
                battleState = playerVictory == true ? BattleState.Victory :
                             playerVictory == false ? BattleState.Defeat :
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
                    var wasPlayerTurn = bool.Parse(lastTurn.Data["IsPlayerTurn"]);
                    battleState = wasPlayerTurn ? BattleState.EnemyTurn : BattleState.PlayerTurn;
                }
            }

            // Build battle log from transactions
            var battleLog = new List<string>
            {
                "=== BATTLE START ===",
                $"{playerCombatant.DisplayName} vs {enemyCombatant.DisplayName}!"
            };

            foreach (var turnTx in turnTransactions)
            {
                var isPlayerTurn = bool.Parse(turnTx.Data["IsPlayerTurn"]);
                var actor = isPlayerTurn ? playerCombatant.DisplayName : enemyCombatant.DisplayName;
                var target = isPlayerTurn ? enemyCombatant.DisplayName : playerCombatant.DisplayName;
                var actionType = Enum.Parse<ActionType>(turnTx.Data["DecisionType"]);
                var damage = float.Parse(turnTx.Data["DamageDealt"]);
                var healing = float.Parse(turnTx.Data["HealingDone"]);

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

            if (battleHasEnded)
            {
                battleLog.Add(playerVictory == true ? "\n=== AVATAR WINS! ===" :
                             playerVictory == false ? "\n=== OPPONENT WINS! ===" :
                             "\n=== FLED FROM BATTLE ===");
            }

            System.Diagnostics.Debug.WriteLine($"[GetBattleState] Battle state: {battleState}, Turn: {turnNumber}, Ended: {battleHasEnded}");

            return new BattleStateResult
            {
                IsActive = !battleHasEnded,
                BattleState = battleState,
                BattleInstanceId = query.BattleInstanceId,
                TurnNumber = turnNumber,
                PlayerCombatant = playerCombatant,
                EnemyCombatant = enemyCombatant,
                BattleLog = battleLog,
                PlayerVictory = playerVictory,
                HasEnded = battleHasEnded,
                PlayerAffinityRefs = playerAffinityRefs,
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

    private (Combatant player, Combatant enemy, int randomSeed, List<string> playerAffinityRefs, Guid enemyCharacterInstanceId)
        ReconstructCombatants(SagaTransaction battleStartedTx, SagaInstance instance)
    {
        // Parse initial state from BattleStarted transaction
        var playerCombatant = new Combatant
        {
            RefName = battleStartedTx.Data["PlayerCombatantId"],
            DisplayName = "Player",
            Health = float.Parse(battleStartedTx.Data["PlayerHealth"]),
            Energy = float.Parse(battleStartedTx.Data["PlayerEnergy"]),
            Strength = float.Parse(battleStartedTx.Data["PlayerStrength"]),
            Defense = float.Parse(battleStartedTx.Data["PlayerDefense"]),
            Speed = float.Parse(battleStartedTx.Data["PlayerSpeed"]),
            Magic = float.Parse(battleStartedTx.Data["PlayerMagic"]),
            AffinityRef = battleStartedTx.Data.TryGetValue("PlayerAffinity", out var pAff) ? pAff : null,
            CombatProfile = new Dictionary<string, string>()
        };

        var enemyCharacterRef = battleStartedTx.Data["EnemyCharacterRef"];
        var enemyCharacter = _world.GetCharacterByRefName(enemyCharacterRef);
        var enemyCombatant = new Combatant
        {
            RefName = enemyCharacterRef,
            DisplayName = enemyCharacter?.DisplayName ?? "Enemy",
            Health = float.Parse(battleStartedTx.Data["EnemyHealth"]),
            Energy = float.Parse(battleStartedTx.Data["EnemyEnergy"]),
            Strength = float.Parse(battleStartedTx.Data["EnemyStrength"]),
            Defense = float.Parse(battleStartedTx.Data["EnemyDefense"]),
            Speed = float.Parse(battleStartedTx.Data["EnemySpeed"]),
            Magic = float.Parse(battleStartedTx.Data["EnemyMagic"]),
            AffinityRef = battleStartedTx.Data.TryGetValue("EnemyAffinity", out var eAff) ? eAff : null,
            CombatProfile = new Dictionary<string, string>()
        };

        // Parse equipment and equipped slots
        if (battleStartedTx.Data.TryGetValue("PlayerEquippedSlots", out var playerSlots))
        {
            foreach (var slot in playerSlots.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = slot.Split(':');
                if (parts.Length >= 2)
                {
                    playerCombatant.CombatProfile[parts[0]] = parts[1];
                }
            }
        }

        if (battleStartedTx.Data.TryGetValue("EnemyEquippedSlots", out var enemySlots))
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
                       t.Data.TryGetValue("BattleTransactionId", out var battleId) &&
                       battleId == battleStartedTx.TransactionId.ToString())
            .OrderBy(t => t.SequenceNumber)
            .ToList();

        foreach (var turnTx in turnTransactions)
        {
            var isPlayerTurn = bool.Parse(turnTx.Data["IsPlayerTurn"]);
            var combatant = isPlayerTurn ? playerCombatant : enemyCombatant;
            var target = isPlayerTurn ? enemyCombatant : playerCombatant;

            // Update health/energy from turn results
            var targetHealthAfter = float.Parse(turnTx.Data["TargetHealthAfter"]);
            var actorEnergyAfter = float.Parse(turnTx.Data["ActorEnergyAfter"]);

            combatant.Energy = actorEnergyAfter;
            target.Health = targetHealthAfter;

            // Update equipment/affinity from snapshots
            if (turnTx.Data.TryGetValue("LoadoutSlotSnapshot", out var loadoutSnapshot))
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

            if (turnTx.Data.TryGetValue("AffinitySnapshot", out var affinity))
            {
                combatant.AffinityRef = affinity;
            }
        }

        var randomSeed = int.Parse(battleStartedTx.Data["RandomSeed"]);
        var playerAffinityRefs = battleStartedTx.Data.TryGetValue("PlayerAffinities", out var affinities)
            ? affinities.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList()
            : new List<string>();
        var enemyCharacterInstanceId = Guid.Parse(battleStartedTx.Data["EnemyCombatantId"]);

        return (playerCombatant, enemyCombatant, randomSeed, playerAffinityRefs, enemyCharacterInstanceId);
    }
}
