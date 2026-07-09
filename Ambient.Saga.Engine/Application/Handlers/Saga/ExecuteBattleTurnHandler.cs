using Ambient.Domain.Entities;
using MediatR;
using Ambient.Saga.Engine.Contracts.Services;
using Ambient.Saga.Engine.Application.ReadModels;
using Ambient.Saga.Engine.Domain.Rpg.Sagas.TransactionLog;
using Ambient.Saga.Engine.Domain.Rpg.Battle;
using Ambient.Saga.Engine.Domain.Rpg.Dialogue;
using Ambient.Saga.Engine.Application.Commands.Saga;
using Ambient.Saga.Engine.Application.Results.Saga;
using Ambient.Saga.Engine.Contracts.Cqrs;
using Ambient.Domain;
using Ambient.Domain.Contracts;
using Ambient.Domain.Extensions;
using Ambient.Saga.Engine.Domain;

namespace Ambient.Saga.Engine.Application.Handlers.Saga;

/// <summary>
/// Handler for ExecuteBattleTurnCommand.
/// Replays battle state from transactions, executes one avatar turn + enemy response, creates new transactions.
/// Similar to SelectDialogueChoiceHandler pattern.
/// </summary>
internal sealed class ExecuteBattleTurnHandler : IRequestHandler<ExecuteBattleTurnCommand, SagaCommandResult>
{
    private readonly ISagaInstanceRepository _instanceRepository;
    private readonly ISagaReadModelRepository _readModelRepository;
    private readonly IAvatarUpdateService _avatarUpdateService;
    private readonly IWorld _world;

    public ExecuteBattleTurnHandler(
        ISagaInstanceRepository instanceRepository,
        ISagaReadModelRepository readModelRepository,
        IAvatarUpdateService avatarUpdateService,
        IWorld world)
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
                                    t.Data.TryGetValue(TransactionDataKeys.BattleTransactionId, out var battleId) &&
                                    battleId == command.BattleInstanceId.ToString());

            if (battleEndedTx != null)
            {
                System.Diagnostics.Debug.WriteLine("[ExecuteBattleTurn] Battle already ended");
                return SagaCommandResult.Failure(instance.InstanceId, "Battle has already ended");
            }

            // Reconstruct combatants from BattleStarted + all BattleTurnExecuted transactions
            var (avatarCombatant, enemyCombatant, randomSeed, avatarAffinityRefs, enemyCharacterInstanceId, companions) =
                BattleStateReconstructor.Reconstruct(battleStartedTx, instance, _world);

            // Get enemy AI (need to recreate with same seed for determinism)
            var enemyCharacterRef = battleStartedTx.Data[TransactionDataKeys.EnemyCharacterRef];
            var enemyCharacter = _world.GetCharacterByRefName(enemyCharacterRef);
            if (enemyCharacter == null)
            {
                return SagaCommandResult.Failure(instance.InstanceId, $"Enemy character '{enemyCharacterRef}' not found");
            }

            // Attach live capabilities — the transaction log carries stats/slots only.
            // Mirrors the live battle-start path (BattleSetup) and GetBattleStateHandler:
            // without these, consumable use bails, and weapon/spell condition and
            // durability loss silently operate on nothing.
            if (command.Avatar?.Capabilities != null)
            {
                avatarCombatant.Capabilities = command.Avatar.Capabilities;

                // Fold the recorded mid-battle wear back in: the attached capabilities
                // carry the persisted (pre-battle) conditions, so without this every
                // command re-degraded from the initial value — a 20-turn battle wore
                // the weapon by exactly one hit
                ApplyRecordedEquipmentConditions(instance, command.BattleInstanceId, avatarCombatant);
            }
            if (command.Avatar != null)
            {
                // Live entity data the log doesn't carry — effective stats must match
                // the opening turn's
                avatarCombatant.ArchetypeBias = command.Avatar.ArchetypeBias;
            }
            if (enemyCharacter.Capabilities != null)
            {
                // Deep-clone: enemyCharacter is the SHARED world template. Battle drains its
                // consumables and wears its equipment/weapon durability; without a copy those
                // losses persist on the template across every battle and respawn of this
                // character until the world reloads (R4-18).
                enemyCombatant.Capabilities = enemyCharacter.Capabilities.DeepClone();
            }

            // Deterministic per-turn RNG: derive this command's seed from the battle seed
            // and the number of already-executed turns. A fresh Random(battleSeed) every
            // command replayed the SAME roll sequence each turn (identical damage/crits
            // all battle); prior turns are folded from the log, never re-simulated, so
            // the stream position must be derived, not advanced.
            var executedTurnCount = instance.Transactions
                .Count(t => t.Type == SagaTransactionType.BattleTurnExecuted &&
                            t.Data.TryGetValue(TransactionDataKeys.BattleTransactionId, out var seedBattleId) &&
                            seedBattleId == command.BattleInstanceId.ToString());
            var turnSeed = unchecked(randomSeed + 104729 * (executedTurnCount + 1));

            var enemyMind = new CombatAI(_world, turnSeed);

            // Reconstruct battle engine (companions included — they were rebuilt from the
            // BattleStarted roster and per-turn snapshots, so party combat survives the
            // command boundary and save/reload)
            // Note: tells are NOT registered during replay — they're only used for the live enemy turn
            var battleEngine = new BattleEngine(avatarCombatant, enemyCombatant, enemyMind, _world, turnSeed,
                companions: companions);
            battleEngine.SetAvatarAffinities(avatarAffinityRefs);

            // Prior turn results were already folded into the combatants by
            // ReconstructBattleState — resume mid-battle instead of re-simulating.
            // (Re-simulating on top of the folded state double-applied every hit, and
            // the live opening turn used an unseeded AI so decisions could diverge.)
            var executedTurns = instance.Transactions
                .Where(t => t.Type == SagaTransactionType.BattleTurnExecuted &&
                           t.Data.TryGetValue(TransactionDataKeys.BattleTransactionId, out var battleId) &&
                           battleId == command.BattleInstanceId.ToString())
                .OrderBy(t => t.SequenceNumber)
                .ToList();

            System.Diagnostics.Debug.WriteLine($"[ExecuteBattleTurn] Resuming after {executedTurns.Count} recorded turns");

            // Validate transaction sequence integrity
            if (executedTurns.Count > 0)
            {
                for (var i = 0; i < executedTurns.Count; i++)
                {
                    var turnTx = executedTurns[i];
                    var expectedTurnNumber = i + 1;
                    var actualTurnNumber = int.Parse(turnTx.Data[TransactionDataKeys.TurnNumber]);

                    if (actualTurnNumber != expectedTurnNumber)
                    {
                        return SagaCommandResult.Failure(
                            instance.InstanceId,
                            $"Transaction sequence corrupted: expected turn {expectedTurnNumber}, found turn {actualTurnNumber}");
                    }
                }
            }

            battleEngine.ResumeBattle(executedTurns.Count + 1);

            var newTransactions = new List<SagaTransaction>();
            var nextTurnNumber = executedTurns.Count + 1;

            // An unresolved telegraphed enemy attack blocks normal turns: the player must
            // react (SubmitReaction), or — if the reaction window lapsed (client crashed,
            // walked away) — the server resolves it here as an un-reacted hit. Skipping
            // the reaction used to make the enemy attack silently evaporate (free dodge).
            var pendingTellTx = ReactionTransactionFactory.FindUnresolvedTell(instance, command.BattleInstanceId);
            if (pendingTellTx != null)
            {
                if (!ReactionTransactionFactory.IsExpired(pendingTellTx, DateTime.UtcNow))
                {
                    return SagaCommandResult.Failure(instance.InstanceId,
                        "An enemy attack is awaiting your reaction");
                }

                battleEngine.RegisterTellsFromWorld(_world);
                var tellRefName = pendingTellTx.Data[TransactionDataKeys.TellRefName];
                BattleTransactionHelper.TryParseFloat(pendingTellTx.Data[TransactionDataKeys.BaseDamage], out var tellBaseDamage);

                if (battleEngine.BeginAttackWithTell(battleEngine.GetEnemy(), battleEngine.GetAvatar(), tellRefName, tellBaseDamage))
                {
                    var timeoutResult = battleEngine.ResolveReaction(AvatarDefenseType.None);
                    if (timeoutResult != null)
                    {
                        System.Diagnostics.Debug.WriteLine("[ExecuteBattleTurn] Auto-resolved abandoned tell as un-reacted hit");
                        var timeoutTx = ReactionTransactionFactory.Create(
                            command.AvatarId, command.BattleInstanceId, nextTurnNumber,
                            timeoutResult, tellRefName, tellBaseDamage, battleEngine, instance.InstanceId);
                        timeoutTx.Data[TransactionDataKeys.TimedOut] = bool.TrueString;
                        instance.AddTransaction(timeoutTx);
                        newTransactions.Add(timeoutTx);
                        nextTurnNumber++;

                        // The un-reacted hit may have killed the avatar
                        if (battleEngine.State == BattleState.Defeat)
                        {
                            await CreateBattleEndTransactions(command, instance, battleEngine,
                                nextTurnNumber - 1, enemyCharacterInstanceId, enemyCharacter, newTransactions);
                            return await CommitAndFinish(command, instance, battleEngine, newTransactions, ct);
                        }
                    }
                }
                // Tell ref missing from world (content changed): BeginAttackWithTell already
                // logged; fall through and let the avatar act — the attack is forfeited
            }

            // Tick the avatar's status effects at the start of their turn — DoTs damage,
            // durations decrement, expired effects clear. Without this server-side call
            // enemy poison never hurt the avatar and debuffs on the avatar never expired.
            battleEngine.ProcessAvatarTurnStart();

            CombatEvent avatarEvent;
            if (battleEngine.State != BattleState.AvatarTurn)
            {
                // The avatar died to the DoT tick before acting. Synthesize a fully
                // stamped turn event so persistence and reconstruction stay coherent.
                var avatarNow = battleEngine.GetAvatar();
                avatarEvent = new CombatEvent
                {
                    ActionType = BattleActionType.Attack, // Placeholder — no action taken
                    ActorName = avatarNow.DisplayName,
                    TargetName = avatarNow.DisplayName,
                    Success = false,
                    Message = $"{avatarNow.DisplayName} succumbed to status effects!",
                    TurnNumber = nextTurnNumber,
                    IsAvatarTurn = true,
                    DecisionType = command.AvatarAction.ActionType,
                    TargetHealthAfter = avatarNow.Health,
                    ActorEnergyAfter = avatarNow.Stamina
                };
            }
            else
            {
                // Now execute the new avatar turn
                System.Diagnostics.Debug.WriteLine($"[ExecuteBattleTurn] Executing avatar action: {command.AvatarAction.ActionType}");
                avatarEvent = battleEngine.ExecuteAvatarDecision(command.AvatarAction);
            }

            // Create transaction for avatar's turn
            var turnNumber = nextTurnNumber;
            var avatarAfterAction = battleEngine.GetAvatar();
            var avatarTurnTx = BattleTransactionHelper.CreateBattleTurnExecutedTransaction(
                command.AvatarId.ToString(),
                command.BattleInstanceId,
                turnNumber,
                avatarEvent.ActorName,
                true,  // Is avatar turn
                command.AvatarAction.ActionType,
                command.AvatarAction.Parameter,
                avatarEvent.Damage,
                avatarEvent.Healing,
                avatarEvent.TargetName,
                avatarEvent.TargetHealthAfter,
                avatarEvent.ActorEnergyAfter,
                avatarAfterAction,
                _world,
                instance.InstanceId,
                avatarState: battleEngine.GetAvatar(),
                enemyState: battleEngine.GetEnemy(),
                companionStates: battleEngine.GetCompanions());

            instance.AddTransaction(avatarTurnTx);
            newTransactions.Add(avatarTurnTx);

            System.Diagnostics.Debug.WriteLine($"[ExecuteBattleTurn] Avatar turn: {command.AvatarAction.ActionType}, dealt {avatarEvent.Damage:F2} damage");

            // Handle trait assignment from combat event (e.g., Disengaged trait on successful flee)
            if (!string.IsNullOrEmpty(avatarEvent.TraitToAssign) && !string.IsNullOrEmpty(avatarEvent.TraitTargetCharacterRef))
            {
                var traitTx = DialogueTransactionHelper.CreateTraitAssignedTransaction(
                    command.AvatarId.ToString(),
                    avatarEvent.TraitTargetCharacterRef,
                    avatarEvent.TraitToAssign,
                    null, // No value for Disengaged trait
                    instance.InstanceId);

                instance.AddTransaction(traitTx);
                newTransactions.Add(traitTx);

                System.Diagnostics.Debug.WriteLine($"[ExecuteBattleTurn] Assigned trait '{avatarEvent.TraitToAssign}' to '{avatarEvent.TraitTargetCharacterRef}'");
            }

            // Check if battle ended after avatar's turn
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
                        false,  // Not avatar turn (companion is allied but AI-controlled)
                        companionEvent.DecisionType,
                        companionEvent.ItemRefName,
                        companionEvent.Damage,
                        companionEvent.Healing,
                        companionEvent.TargetName,
                        companionEvent.TargetHealthAfter,
                        companionEvent.ActorEnergyAfter,
                        companion,
                        _world,
                        instance.InstanceId,
                        avatarState: battleEngine.GetAvatar(),
                        enemyState: battleEngine.GetEnemy(),
                        companionStates: battleEngine.GetCompanions());

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
                    // Register tells for the live enemy turn (not during replay)
                    battleEngine.RegisterTellsFromWorld(_world);

                    System.Diagnostics.Debug.WriteLine("[ExecuteBattleTurn] Executing enemy response");
                    var enemyEvent = battleEngine.ExecuteEnemyTurn();

                    if (battleEngine.State == BattleState.AwaitingReaction && battleEngine.PendingAttack != null)
                    {
                        // Enemy attack produced a tell — persist it as a turn transaction so the
                        // pending attack SURVIVES the command boundary (and save/reload). It used
                        // to live only in this discarded engine instance: quitting mid-tell (or
                        // just sending another turn command) made the enemy attack evaporate.
                        var pending = battleEngine.PendingAttack;
                        System.Diagnostics.Debug.WriteLine($"[ExecuteBattleTurn] Enemy attack tell: {pending.Tell.TellText}");

                        turnNumber++;
                        var tellTx = BattleTransactionHelper.CreateBattleTurnExecutedTransaction(
                            command.AvatarId.ToString(),
                            command.BattleInstanceId,
                            turnNumber,
                            battleEngine.GetEnemy().DisplayName,
                            false,  // Enemy's turn
                            ActionType.Attack,
                            null,
                            0f,     // No damage yet — resolves via SubmitReaction
                            0f,
                            battleEngine.GetAvatar().DisplayName,
                            battleEngine.GetAvatar().Health,
                            battleEngine.GetEnemy().Stamina,
                            battleEngine.GetEnemy(),
                            _world,
                            instance.InstanceId,
                            avatarState: battleEngine.GetAvatar(),
                            enemyState: battleEngine.GetEnemy(),
                            companionStates: battleEngine.GetCompanions());
                        tellTx.Data[TransactionDataKeys.ActionType] = "Tell";
                        tellTx.Data[TransactionDataKeys.TellRefName] = pending.Tell.RefName;
                        tellTx.Data[TransactionDataKeys.BaseDamage] = BattleTransactionHelper.FormatFloat(pending.BaseDamage);
                        tellTx.Data[TransactionDataKeys.ReactionWindowMs] = pending.Tell.ReactionWindowMs.ToString();
                        instance.AddTransaction(tellTx);
                        newTransactions.Add(tellTx);

                        var (tellSeqNumbers, tellCommitted) = await _instanceRepository.AddAndCommitTransactionsAsync(instance.InstanceId, newTransactions, ct);

                        if (!tellCommitted)
                            return SagaCommandResult.Failure(instance.InstanceId, "Concurrency conflict - transactions rolled back");

                        await _readModelRepository.InvalidateCacheAsync(command.AvatarId, command.SagaArcRef, ct);

                        return SagaCommandResult.Success(
                            instance.InstanceId,
                            newTransactions.Select(t => t.TransactionId).ToList(),
                            tellSeqNumbers.First(),
                            new Dictionary<string, object>
                            {
                                [TransactionDataKeys.AwaitingReaction] = true,
                                [TransactionDataKeys.TellRefName] = pending.Tell.RefName,
                                [TransactionDataKeys.TellText] = pending.Tell.TellText,
                                [TransactionDataKeys.ReactionWindowMs] = pending.Tell.ReactionWindowMs,
                                [TransactionDataKeys.BaseDamage] = pending.BaseDamage,
                                [TransactionDataKeys.OptimalDefense] = pending.Tell.OptimalDefense.ToString()
                            });
                    }

                    turnNumber++;
                    var enemyAfterAction = battleEngine.GetEnemy();
                    var enemyTurnTx = BattleTransactionHelper.CreateBattleTurnExecutedTransaction(
                        command.AvatarId.ToString(),
                        command.BattleInstanceId,
                        turnNumber,
                        enemyEvent.ActorName,
                        false,  // Not avatar turn
                        enemyEvent.DecisionType,
                        enemyEvent.ItemRefName,
                        enemyEvent.Damage,
                        enemyEvent.Healing,
                        enemyEvent.TargetName,
                        enemyEvent.TargetHealthAfter,
                        enemyEvent.ActorEnergyAfter,
                        enemyAfterAction,
                        _world,
                        instance.InstanceId,
                        avatarState: battleEngine.GetAvatar(),
                        enemyState: battleEngine.GetEnemy(),
                        companionStates: battleEngine.GetCompanions());

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

            return await CommitAndFinish(command, instance, battleEngine, newTransactions, ct);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ExecuteBattleTurn] ERROR: {ex.Message}\n{ex.StackTrace}");
            return SagaCommandResult.Failure(Guid.Empty, $"Error executing battle turn: {ex.Message}");
        }
    }

    /// <summary>
    /// Commits the accumulated transactions, invalidates the read model, and — when the
    /// battle concluded — folds the recorded outcome into the persisted avatar.
    /// </summary>
    private async Task<SagaCommandResult> CommitAndFinish(
        ExecuteBattleTurnCommand command,
        SagaInstance instance,
        BattleEngine battleEngine,
        List<SagaTransaction> newTransactions,
        CancellationToken ct)
    {
        var (sequenceNumbers, committed) = await _instanceRepository.AddAndCommitTransactionsAsync(instance.InstanceId, newTransactions, ct);

        if (!committed)
        {
            return SagaCommandResult.Failure(instance.InstanceId, "Concurrency conflict - transactions rolled back");
        }

        await _readModelRepository.InvalidateCacheAsync(command.AvatarId, command.SagaArcRef, ct);

        System.Diagnostics.Debug.WriteLine($"[ExecuteBattleTurn] Created {newTransactions.Count} transactions");

        // Update avatar if battle ended
        AvatarEntity? updatedAvatar = null;
        if (battleEngine.State != BattleState.AvatarTurn && battleEngine.State != BattleState.EnemyTurn)
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

    /// <summary>
    /// Applies the most recent recorded avatar loadout-slot conditions
    /// ("Slot:Ref:Condition") onto the given combatant's capabilities.
    /// </summary>
    private static void ApplyRecordedEquipmentConditions(SagaInstance instance, Guid battleInstanceId, Combatant avatarCombatant)
    {
        var lastAvatarSnapshot = instance.Transactions
            .Where(t => t.Type == SagaTransactionType.BattleTurnExecuted &&
                        t.Data.TryGetValue(TransactionDataKeys.BattleTransactionId, out var battleId) &&
                        battleId == battleInstanceId.ToString() &&
                        t.Data.TryGetValue(TransactionDataKeys.IsAvatarTurn, out var isAvatarStr) &&
                        bool.TryParse(isAvatarStr, out var isAvatar) && isAvatar &&
                        t.Data.ContainsKey(TransactionDataKeys.LoadoutSlotSnapshot))
            .OrderBy(t => t.SequenceNumber)
            .LastOrDefault();

        if (lastAvatarSnapshot == null || avatarCombatant.Capabilities?.Equipment == null)
            return;

        foreach (var slot in lastAvatarSnapshot.Data[TransactionDataKeys.LoadoutSlotSnapshot]
                     .Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = slot.Split(':');
            if (parts.Length != 3 || !BattleTransactionHelper.TryParseFloat(parts[2], out var condition))
                continue;

            var equipmentEntry = avatarCombatant.Capabilities.Equipment
                .FirstOrDefault(e => e.EquipmentRef == parts[1]);
            if (equipmentEntry != null)
            {
                equipmentEntry.Condition = condition;
            }
        }
    }

    private Task CreateBattleEndTransactions(
        ExecuteBattleTurnCommand command,
        SagaInstance instance,
        BattleEngine battleEngine,
        int totalTurns,
        Guid enemyCharacterInstanceId,
        Character enemyCharacter,
        List<SagaTransaction> newTransactions)
    {
        BattleEndTransactionFactory.Create(
            command.AvatarId,
            command.Avatar,
            command.BattleInstanceId,
            instance,
            battleEngine,
            totalTurns,
            enemyCharacterInstanceId,
            enemyCharacter,
            newTransactions);
        return Task.CompletedTask;
    }
}
