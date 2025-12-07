using Ambient.Domain.DefinitionExtensions;
using Ambient.Domain.Entities;
using MediatR;
using Ambient.Saga.Engine.Contracts.Services;
using Ambient.Saga.Engine.Application.ReadModels;
using Ambient.Saga.Engine.Domain.Rpg.Sagas.TransactionLog;
using Ambient.Saga.Engine.Domain.Rpg.Battle;
using Ambient.Saga.Engine.Application.Commands.Saga;
using Ambient.Saga.Engine.Application.Results.Saga;
using Ambient.Saga.Engine.Contracts.Cqrs;

namespace Ambient.Saga.Engine.Application.Handlers.Saga;

/// <summary>
/// Handler for ExecuteBattleTurnCommand.
/// Replays battle state from transactions, executes one player turn + enemy response, creates new transactions.
/// Similar to SelectDialogueChoiceHandler pattern.
/// </summary>
internal sealed class ExecuteBattleTurnHandler : IRequestHandler<ExecuteBattleTurnCommand, SagaCommandResult>
{
    private readonly ISagaInstanceRepository _instanceRepository;
    private readonly ISagaReadModelRepository _readModelRepository;
    private readonly IAvatarUpdateService _avatarUpdateService;
    private readonly World _world;

    public ExecuteBattleTurnHandler(
        ISagaInstanceRepository instanceRepository,
        ISagaReadModelRepository readModelRepository,
        IAvatarUpdateService avatarUpdateService,
        World world)
    {
        _instanceRepository = instanceRepository;
        _readModelRepository = readModelRepository;
        _avatarUpdateService = avatarUpdateService;
        _world = world;
    }

    public async Task<SagaCommandResult> Handle(ExecuteBattleTurnCommand command, CancellationToken ct)
    {
        System.Diagnostics.Debug.WriteLine($"[ExecuteBattleTurn] Processing turn for battle {command.BattleInstanceId}");

        try
        {
            // Get Saga instance
            var instance = await _instanceRepository.GetOrCreateInstanceAsync(command.AvatarId, command.SagaArcRef, ct);

            // Find BattleStarted transaction
            var battleStartedTx = instance.Transactions
                .FirstOrDefault(t => t.TransactionId == command.BattleInstanceId && t.Type == SagaTransactionType.BattleStarted);

            if (battleStartedTx == null)
            {
                return SagaCommandResult.Failure(instance.InstanceId, $"Battle {command.BattleInstanceId} not found");
            }

            // Check if battle already ended
            var battleEndedTx = instance.Transactions
                .FirstOrDefault(t => t.Type == SagaTransactionType.BattleEnded &&
                                    t.Data.TryGetValue("BattleTransactionId", out var battleId) &&
                                    battleId == command.BattleInstanceId.ToString());

            if (battleEndedTx != null)
            {
                System.Diagnostics.Debug.WriteLine("[ExecuteBattleTurn] Battle already ended");
                return SagaCommandResult.Failure(instance.InstanceId, "Battle has already ended");
            }

            // Reconstruct combatants from BattleStarted + all BattleTurnExecuted transactions
            var (playerCombatant, enemyCombatant, randomSeed, playerAffinityRefs, enemyCharacterInstanceId) =
                ReconstructBattleState(battleStartedTx, instance);

            // Get enemy AI (need to recreate with same seed for determinism)
            var enemyCharacterRef = battleStartedTx.Data["EnemyCharacterRef"];
            var enemyCharacter = _world.GetCharacterByRefName(enemyCharacterRef);
            if (enemyCharacter == null)
            {
                return SagaCommandResult.Failure(instance.InstanceId, $"Enemy character '{enemyCharacterRef}' not found");
            }

            // CRITICAL: Use same random seed as original battle for deterministic replay
            var enemyMind = new CombatAI(_world, randomSeed);

            // Reconstruct battle engine
            var battleEngine = new BattleEngine(playerCombatant, enemyCombatant, enemyMind, _world, randomSeed);
            battleEngine.SetPlayerAffinities(playerAffinityRefs);

            // Start battle and replay all turns to reach current state
            battleEngine.StartBattle();
            var executedTurns = instance.Transactions
                .Where(t => t.Type == SagaTransactionType.BattleTurnExecuted &&
                           t.Data.TryGetValue("BattleTransactionId", out var battleId) &&
                           battleId == command.BattleInstanceId.ToString())
                .OrderBy(t => t.SequenceNumber)
                .ToList();

            System.Diagnostics.Debug.WriteLine($"[ExecuteBattleTurn] Replaying {executedTurns.Count} previous turns");

            // Validate transaction sequence integrity
            if (executedTurns.Count > 0)
            {
                for (var i = 0; i < executedTurns.Count; i++)
                {
                    var turnTx = executedTurns[i];
                    var expectedTurnNumber = i + 1;
                    var actualTurnNumber = int.Parse(turnTx.Data["TurnNumber"]);

                    if (actualTurnNumber != expectedTurnNumber)
                    {
                        return SagaCommandResult.Failure(
                            instance.InstanceId,
                            $"Transaction sequence corrupted: expected turn {expectedTurnNumber}, found turn {actualTurnNumber}");
                    }
                }
            }

            // Skip the opening enemy turn (already executed in StartBattle)
            // Replay subsequent turns to reach current state
            for (var i = 1; i < executedTurns.Count; i++)
            {
                var turnTx = executedTurns[i];
                var isPlayerTurn = bool.Parse(turnTx.Data["IsPlayerTurn"]);

                if (isPlayerTurn)
                {
                    // Reconstruct player action from transaction
                    var actionType = Enum.Parse<ActionType>(turnTx.Data["DecisionType"]);
                    var itemRef = turnTx.Data.TryGetValue("ItemRefName", out var item) ? item : null;
                    var action = new CombatAction { ActionType = actionType, Parameter = itemRef };

                    battleEngine.ExecutePlayerDecision(action);
                }
                else
                {
                    battleEngine.ExecuteEnemyTurn();
                }
            }

            // Now execute the new player turn
            System.Diagnostics.Debug.WriteLine($"[ExecuteBattleTurn] Executing player action: {command.PlayerAction.ActionType}");
            var playerEvent = battleEngine.ExecutePlayerDecision(command.PlayerAction);

            var transactionsBefore = instance.Transactions.Count;
            var newTransactions = new List<SagaTransaction>();

            // Create transaction for player's turn
            var turnNumber = executedTurns.Count + 1;
            var playerAfterAction = battleEngine.GetPlayer();
            var playerTurnTx = BattleTransactionHelper.CreateBattleTurnExecutedTransaction(
                command.AvatarId.ToString(),
                command.BattleInstanceId,
                turnNumber,
                playerEvent.ActorName,
                true,  // Is player turn
                command.PlayerAction.ActionType,
                command.PlayerAction.Parameter,
                playerEvent.Damage,
                playerEvent.Healing,
                playerEvent.TargetName,
                playerEvent.TargetHealthAfter,
                playerEvent.ActorEnergyAfter,
                playerAfterAction,
                _world,
                instance.InstanceId);

            instance.AddTransaction(playerTurnTx);
            newTransactions.Add(playerTurnTx);

            System.Diagnostics.Debug.WriteLine($"[ExecuteBattleTurn] Player turn: {command.PlayerAction.ActionType}, dealt {playerEvent.Damage:F2} damage");

            // Check if battle ended after player's turn
            if (battleEngine.State == BattleState.Victory ||
                battleEngine.State == BattleState.Defeat ||
                battleEngine.State == BattleState.Fled)
            {
                System.Diagnostics.Debug.WriteLine($"[ExecuteBattleTurn] Battle ended: {battleEngine.State}");
                await CreateBattleEndTransactions(
                    command,
                    instance,
                    battleEngine,
                    turnNumber,
                    enemyCharacterInstanceId,
                    enemyCharacter,
                    newTransactions);
            }
            else
            {
                // Execute companion turns (if any)
                while (battleEngine.State == BattleState.CompanionTurn)
                {
                    var companion = battleEngine.CurrentCompanion;
                    if (companion == null) break;

                    System.Diagnostics.Debug.WriteLine($"[ExecuteBattleTurn] Executing companion turn: {companion.DisplayName}");
                    var companionEvent = battleEngine.ExecuteCompanionTurn();

                    // Create transaction for companion's turn
                    turnNumber++;
                    var companionTurnTx = BattleTransactionHelper.CreateBattleTurnExecutedTransaction(
                        command.AvatarId.ToString(),
                        command.BattleInstanceId,
                        turnNumber,
                        companionEvent.ActorName,
                        false,  // Not player turn (companion is allied but AI-controlled)
                        companionEvent.DecisionType,
                        companionEvent.ItemRefName,
                        companionEvent.Damage,
                        companionEvent.Healing,
                        companionEvent.TargetName,
                        companionEvent.TargetHealthAfter,
                        companionEvent.ActorEnergyAfter,
                        companion,
                        _world,
                        instance.InstanceId);

                    instance.AddTransaction(companionTurnTx);
                    newTransactions.Add(companionTurnTx);

                    System.Diagnostics.Debug.WriteLine($"[ExecuteBattleTurn] Companion turn: {companionEvent.DecisionType}, dealt {companionEvent.Damage:F2} damage");

                    // Check if battle ended after companion's turn
                    if (battleEngine.State == BattleState.Victory ||
                        battleEngine.State == BattleState.Defeat)
                    {
                        System.Diagnostics.Debug.WriteLine($"[ExecuteBattleTurn] Battle ended during companion turn: {battleEngine.State}");
                        await CreateBattleEndTransactions(
                            command,
                            instance,
                            battleEngine,
                            turnNumber,
                            enemyCharacterInstanceId,
                            enemyCharacter,
                            newTransactions);
                        break;
                    }
                }

                // Execute enemy's response turn (if battle hasn't ended)
                if (battleEngine.State == BattleState.EnemyTurn)
                {
                    System.Diagnostics.Debug.WriteLine("[ExecuteBattleTurn] Executing enemy response");
                    var enemyEvent = battleEngine.ExecuteEnemyTurn();

                    turnNumber++;
                    var enemyAfterAction = battleEngine.GetEnemy();
                    var enemyTurnTx = BattleTransactionHelper.CreateBattleTurnExecutedTransaction(
                        command.AvatarId.ToString(),
                        command.BattleInstanceId,
                        turnNumber,
                        enemyEvent.ActorName,
                        false,  // Not player turn
                        enemyEvent.DecisionType,
                        enemyEvent.ItemRefName,
                        enemyEvent.Damage,
                        enemyEvent.Healing,
                        enemyEvent.TargetName,
                        enemyEvent.TargetHealthAfter,
                        enemyEvent.ActorEnergyAfter,
                        enemyAfterAction,
                        _world,
                        instance.InstanceId);

                    instance.AddTransaction(enemyTurnTx);
                    newTransactions.Add(enemyTurnTx);

                    System.Diagnostics.Debug.WriteLine($"[ExecuteBattleTurn] Enemy turn: {enemyEvent.DecisionType}, dealt {enemyEvent.Damage:F2} damage");

                    // Check if battle ended after enemy's turn
                    if (battleEngine.State == BattleState.Victory ||
                        battleEngine.State == BattleState.Defeat ||
                        battleEngine.State == BattleState.Fled)
                    {
                        System.Diagnostics.Debug.WriteLine($"[ExecuteBattleTurn] Battle ended: {battleEngine.State}");
                        await CreateBattleEndTransactions(
                            command,
                            instance,
                            battleEngine,
                            turnNumber,
                            enemyCharacterInstanceId,
                            enemyCharacter,
                            newTransactions);
                    }
                }
            }

            // Persist and commit
            var sequenceNumbers = await _instanceRepository.AddTransactionsAsync(instance.InstanceId, newTransactions, ct);
            var committed = await _instanceRepository.CommitTransactionsAsync(
                instance.InstanceId,
                newTransactions.Select(t => t.TransactionId).ToList(),
                ct);

            if (!committed)
            {
                return SagaCommandResult.Failure(instance.InstanceId, "Concurrency conflict - transactions rolled back");
            }

            // Invalidate cache
            await _readModelRepository.InvalidateCacheAsync(command.AvatarId, command.SagaArcRef, ct);

            System.Diagnostics.Debug.WriteLine($"[ExecuteBattleTurn] Created {newTransactions.Count} transactions");

            // Update avatar if battle ended
            AvatarEntity? updatedAvatar = null;
            if (battleEngine.State != BattleState.PlayerTurn && battleEngine.State != BattleState.EnemyTurn)
            {
                updatedAvatar = await _avatarUpdateService.UpdateAvatarForBattleAsync(
                    command.Avatar,
                    instance,
                    command.BattleInstanceId,
                    ct);

                await _avatarUpdateService.PersistAvatarAsync(updatedAvatar, ct);
            }

            return SagaCommandResult.Success(
                instance.InstanceId,
                newTransactions.Select(t => t.TransactionId).ToList(),
                sequenceNumbers.First(),
                data: null,
                updatedAvatar);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ExecuteBattleTurn] ERROR: {ex.Message}\n{ex.StackTrace}");
            return SagaCommandResult.Failure(Guid.Empty, $"Error executing battle turn: {ex.Message}");
        }
    }

    private (Combatant player, Combatant enemy, int randomSeed, List<string> playerAffinityRefs, Guid enemyCharacterInstanceId)
        ReconstructBattleState(SagaTransaction battleStartedTx, SagaInstance instance)
    {
        // Parse initial state from BattleStarted transaction
        var playerCombatant = new Combatant
        {
            RefName = battleStartedTx.Data["PlayerCombatantId"],
            DisplayName = "Player",  // Will be overridden by UI
            Health = float.Parse(battleStartedTx.Data["PlayerHealth"]),
            Energy = float.Parse(battleStartedTx.Data["PlayerEnergy"]),
            Strength = float.Parse(battleStartedTx.Data["PlayerStrength"]),
            Defense = float.Parse(battleStartedTx.Data["PlayerDefense"]),
            Speed = float.Parse(battleStartedTx.Data["PlayerSpeed"]),
            Magic = float.Parse(battleStartedTx.Data["PlayerMagic"]),
            AffinityRef = battleStartedTx.Data.TryGetValue("PlayerAffinity", out var pAff) ? pAff : null,
            CombatProfile = new Dictionary<string, string>()
        };

        var enemyCombatant = new Combatant
        {
            RefName = battleStartedTx.Data["EnemyCharacterRef"],
            DisplayName = "Enemy",  // Will be overridden by UI
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
            foreach (var slot in playerSlots.Split(','))
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
            foreach (var slot in enemySlots.Split(','))
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

            // Update health/energy from turn results
            var targetHealthAfter = float.Parse(turnTx.Data["TargetHealthAfter"]);
            var actorEnergyAfter = float.Parse(turnTx.Data["ActorEnergyAfter"]);

            // Actor's energy is updated
            combatant.Energy = actorEnergyAfter;

            // Target's health is updated
            var target = isPlayerTurn ? enemyCombatant : playerCombatant;
            target.Health = targetHealthAfter;

            // Update equipment/affinity from snapshots
            if (turnTx.Data.TryGetValue("LoadoutSlotSnapshot", out var loadoutSnapshot))
            {
                combatant.CombatProfile.Clear();
                foreach (var slot in loadoutSnapshot.Split(','))
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
            ? affinities.Split(',').ToList()
            : new List<string>();
        var enemyCharacterInstanceId = Guid.Parse(battleStartedTx.Data["EnemyCombatantId"]);

        return (playerCombatant, enemyCombatant, randomSeed, playerAffinityRefs, enemyCharacterInstanceId);
    }

    private async Task CreateBattleEndTransactions(
        ExecuteBattleTurnCommand command,
        SagaInstance instance,
        BattleEngine battleEngine,
        int totalTurns,
        Guid enemyCharacterInstanceId,
        Character enemyCharacter,
        List<SagaTransaction> newTransactions)
    {
        var playerVictory = battleEngine.State == BattleState.Victory;
        var victorName = playerVictory ? battleEngine.GetPlayer().DisplayName : battleEngine.GetEnemy().DisplayName;
        var defeatedName = playerVictory ? battleEngine.GetEnemy().DisplayName : battleEngine.GetPlayer().DisplayName;

        // Create BattleEnded transaction
        var battleEndedTx = BattleTransactionHelper.CreateBattleEndedTransaction(
            command.AvatarId.ToString(),
            command.BattleInstanceId,
            totalTurns,
            playerVictory,
            victorName,
            defeatedName,
            instance.InstanceId);

        instance.AddTransaction(battleEndedTx);
        newTransactions.Add(battleEndedTx);

        // If player won, create CharacterDefeated transaction
        if (playerVictory)
        {
            var data = new Dictionary<string, string>
            {
                ["CharacterInstanceId"] = enemyCharacterInstanceId.ToString(),
                ["CharacterRef"] = enemyCharacter.RefName,
                ["VictorAvatarId"] = command.AvatarId.ToString(),
                ["DefeatMethod"] = "Battle",
                ["BattleTransactionId"] = command.BattleInstanceId.ToString()
            };

            // Add character tags for quest objective tracking
            if (enemyCharacter.Tags?.Tag != null && enemyCharacter.Tags.Tag.Count > 0)
            {
                data["CharacterTag"] = string.Join(",", enemyCharacter.Tags.Tag);
            }

            var characterDefeatedTx = new SagaTransaction
            {
                TransactionId = Guid.NewGuid(),
                Type = SagaTransactionType.CharacterDefeated,
                AvatarId = command.AvatarId.ToString(),
                Status = TransactionStatus.Pending,
                LocalTimestamp = DateTime.UtcNow,
                Data = data
            };

            instance.AddTransaction(characterDefeatedTx);
            newTransactions.Add(characterDefeatedTx);
        }
    }
}
