using MediatR;
using Ambient.Rpg.Engine.Application.ReadModels;
using Ambient.Rpg.Engine.Domain.Arcs.TransactionLog;
using Ambient.Rpg.Engine.Application.Results.Arcs;
using Ambient.Rpg.Engine.Application.Commands.Arcs;
using Ambient.Rpg.Engine.Contracts.Cqrs;
using Ambient.Rpg.Engine.Domain.Battle;
using Ambient.Domain;
using Ambient.Domain.Contracts;
using Ambient.Rpg.Engine.Domain;

namespace Ambient.Rpg.Engine.Application.Handlers.Arcs;

/// <summary>
/// Handler for StartBattleCommand.
/// Creates BattleStarted transaction and executes enemy's opening turn.
/// (Enemy always moves first in this battle system)
/// </summary>
internal sealed class StartBattleHandler : IRequestHandler<StartBattleCommand, ArcCommandResult>
{
    private readonly IArcInstanceRepository _instanceRepository;
    private readonly IArcReadModelRepository _readModelRepository;
    private readonly IWorld _world;

    public StartBattleHandler(
        IArcInstanceRepository instanceRepository,
        IArcReadModelRepository readModelRepository,
        IWorld world)
    {
        _instanceRepository = instanceRepository;
        _readModelRepository = readModelRepository;
        _world = world;
    }

    public async Task<ArcCommandResult> Handle(StartBattleCommand command, CancellationToken ct)
    {
        System.Diagnostics.Debug.WriteLine($"[StartBattle] Starting battle for avatar {command.AvatarId} vs character {command.EnemyCharacterInstanceId}");

        try
        {
            // Handle dev arc refs (format: "RealArcRef__DEV__uniqueid")
            var arcRefForLookup = command.ArcRef;
            var devSuffix = "__DEV__";
            if (command.ArcRef.Contains(devSuffix))
            {
                arcRefForLookup = command.ArcRef.Substring(0, command.ArcRef.IndexOf(devSuffix));
            }

            // Verify Arc template exists (use stripped ref for lookup)
            if (!_world.ArcLookup.ContainsKey(arcRefForLookup))
            {
                return ArcCommandResult.Failure(Guid.Empty, $"Arc '{arcRefForLookup}' not found");
            }

            // Get Arc instance
            var instance = await _instanceRepository.GetOrCreateInstanceAsync(command.AvatarId, command.ArcRef, ct);

            // Check if an ACTIVE battle already exists for this character. Battles that
            // already have a BattleEnded (victory, defeat, or fled) must not be returned:
            // re-engaging after fleeing used to hand back the ended battle id, making the
            // enemy permanently unfightable ("Battle has already ended" on every turn).
            var endedBattleIds = instance.Transactions
                .Where(t => t.Type == ArcTransactionType.BattleEnded &&
                            t.Data.TryGetValue(TransactionDataKeys.BattleTransactionId, out _))
                .Select(t => t.Data[TransactionDataKeys.BattleTransactionId])
                .ToHashSet();

            var existingBattle = instance.Transactions
                .Where(t => t.Type == ArcTransactionType.BattleStarted &&
                            !endedBattleIds.Contains(t.TransactionId.ToString()))
                .FirstOrDefault(t =>
                    t.Data.TryGetValue(TransactionDataKeys.EnemyCombatantId, out var enemyId) &&
                    enemyId == command.EnemyCharacterInstanceId.ToString());

            if (existingBattle != null)
            {
                System.Diagnostics.Debug.WriteLine("[StartBattle] Battle already started - returning existing battle ID");
                // Return the BattleInstanceId in Data exactly like the new-battle path below.
                // Without it BattleModal.InitializeBattleAsync treats the resume as a failed
                // start, never refreshes state, and shows "Preparing for battle..." forever —
                // permanently bricking any character whose battle was left un-ended (quit mid-fight).
                var resumeData = new Dictionary<string, object>
                {
                    [TransactionDataKeys.BattleInstanceId] = existingBattle.TransactionId
                };

                // If the battle was suspended mid-telegraph (the window was closed during the
                // reaction window without reacting), re-present the pending tell in the SAME
                // shape the fresh-start path returns below (AwaitingReaction + tell metadata).
                // BattleModal.TryStartReactionFromResult consumes this to rebuild the client
                // reaction engine, so re-engaging restores the reaction prompt instead of
                // wedging on "Enemy's turn...".
                AppendPendingTellData(instance, existingBattle.TransactionId, resumeData);

                return ArcCommandResult.Success(
                    instance.InstanceId,
                    new List<Guid> { existingBattle.TransactionId },
                    existingBattle.SequenceNumber,
                    resumeData);
            }

            // The enemy keeps damage taken in previous battles (e.g. the player fled and
            // came back): fold in its health at the end of its most recent ended battle,
            // read from the absolute per-turn snapshots. The client-supplied combatant
            // is typically built fresh from the template at full health.
            var previousHealth = GetEnemyHealthFromLastEndedBattle(instance, command.EnemyCharacterInstanceId);
            if (previousHealth.HasValue && previousHealth.Value < command.EnemyCombatant.Health)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[StartBattle] Carrying enemy damage from previous battle: health {command.EnemyCombatant.Health:F3} -> {previousHealth.Value:F3}");
                command.EnemyCombatant.Health = previousHealth.Value;
            }

            // Create battle engine with deterministic seed and companions
            var battleEngine = new BattleEngine(
                command.AvatarCombatant,
                command.EnemyCombatant,
                command.EnemyMind,
                _world,
                command.RandomSeed,
                companions: command.CompanionCombatants);

            battleEngine.SetAvatarAffinities(command.AvatarAffinityRefs);
            battleEngine.RegisterTellsFromWorld(_world);

            if (command.CompanionCombatants?.Count > 0)
            {
                System.Diagnostics.Debug.WriteLine($"[StartBattle] Party includes {command.CompanionCombatants.Count} companions: {string.Join(", ", command.CompanionCombatants.Select(c => c.DisplayName))}");
            }

            // Create BattleStarted transaction
            var battleStartedTransaction = BattleTransactionHelper.CreateBattleStartedTransaction(
                command.AvatarId.ToString(),
                command.ArcRef,
                Guid.NewGuid(),  // Avatar combatant ID
                command.EnemyCharacterInstanceId,
                command.EnemyCombatant.RefName,
                command.RandomSeed,
                command.AvatarCombatant,
                command.EnemyCombatant,
                command.AvatarAffinityRefs,
                instance.InstanceId,
                companions: command.CompanionCombatants);

            instance.AddTransaction(battleStartedTransaction);

            // Start battle (enemy moves first)
            battleEngine.StartBattle();

            var transactions = new List<ArcTransaction> { battleStartedTransaction };
            var resultData = new Dictionary<string, object>
            {
                [TransactionDataKeys.BattleInstanceId] = battleStartedTransaction.TransactionId
            };

            if (battleEngine.State == BattleState.AwaitingReaction && battleEngine.PendingAttack != null)
            {
                // Enemy's opening attack produced a tell — reaction phase active.
                // Persist the tell as a turn transaction so the pending attack survives
                // command boundaries and save/reload (see ExecuteBattleTurnHandler).
                var pending = battleEngine.PendingAttack;
                resultData[TransactionDataKeys.AwaitingReaction] = true;
                resultData[TransactionDataKeys.TellRefName] = pending.Tell.RefName;
                resultData[TransactionDataKeys.TellText] = pending.Tell.TellText;
                resultData[TransactionDataKeys.ReactionWindowMs] = pending.Tell.ReactionWindowMs;
                resultData[TransactionDataKeys.BaseDamage] = pending.BaseDamage;
                resultData[TransactionDataKeys.OptimalDefense] = pending.Tell.OptimalDefense.ToString();

                var tellTx = BattleTransactionHelper.CreateBattleTurnExecutedTransaction(
                    command.AvatarId.ToString(),
                    battleStartedTransaction.TransactionId,
                    1,  // Turn 1
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
                transactions.Add(tellTx);

                System.Diagnostics.Debug.WriteLine($"[StartBattle] Enemy opened with tell: {pending.Tell.TellText}");
            }
            else
            {
                // Enemy attacked directly (no tells available) — record the turn
                var enemyAction = battleEngine.ActionHistory.FirstOrDefault();
                if (enemyAction != null)
                {
                    var enemyAfterAction = battleEngine.GetEnemy();
                    var enemyTurnTransaction = BattleTransactionHelper.CreateBattleTurnExecutedTransaction(
                        command.AvatarId.ToString(),
                        battleStartedTransaction.TransactionId,
                        1,  // Turn 1
                        enemyAction.ActorName,
                        false,  // Not avatar turn
                        enemyAction.DecisionType,
                        enemyAction.ItemRefName,
                        enemyAction.Damage,
                        enemyAction.Healing,
                        enemyAction.TargetName,
                        enemyAction.TargetHealthAfter,
                        enemyAction.ActorEnergyAfter,
                        enemyAfterAction,
                        _world,
                        instance.InstanceId,
                        avatarState: battleEngine.GetAvatar(),
                        enemyState: battleEngine.GetEnemy(),
                        companionStates: battleEngine.GetCompanions());

                    instance.AddTransaction(enemyTurnTransaction);
                    transactions.Add(enemyTurnTransaction);

                    System.Diagnostics.Debug.WriteLine($"[StartBattle] Enemy opened with {enemyAction.DecisionType}, dealt {enemyAction.Damage:F2} damage");
                }
            }

            // Commit all transactions
            var (sequenceNumbers, committed) = await _instanceRepository.AddAndCommitTransactionsAsync(
                instance.InstanceId,
                transactions,
                ct);

            if (!committed)
            {
                return ArcCommandResult.Failure(instance.InstanceId, "Concurrency conflict - transaction rolled back");
            }

            // Invalidate cache
            await _readModelRepository.InvalidateCacheAsync(command.AvatarId, command.ArcRef, ct);

            System.Diagnostics.Debug.WriteLine($"[StartBattle] Battle started successfully with ID {battleStartedTransaction.TransactionId}");

            return ArcCommandResult.Success(
                instance.InstanceId,
                transactions.Select(t => t.TransactionId).ToList(),
                sequenceNumbers.First(),
                resultData);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[StartBattle] ERROR: {ex.Message}");
            return ArcCommandResult.Failure(Guid.Empty, $"Error starting battle: {ex.Message}");
        }
    }

    /// <summary>
    /// When the resumed battle's last turn is an unresolved enemy telegraph, adds the
    /// awaiting-reaction fields (AwaitingReaction + tell ref/text/window/base damage) to
    /// the resume result — mirroring the fresh-start path's AwaitingReaction block so the
    /// UI's TryStartReactionFromResult can restore the reaction phase on re-engage. No-op
    /// when the battle is not paused on a tell (ordinary resume returns BattleInstanceId
    /// alone and GetBattleState reports the AvatarTurn/EnemyTurn state).
    /// </summary>
    private void AppendPendingTellData(ArcInstance instance, Guid battleInstanceId, Dictionary<string, object> resultData)
    {
        var tellTx = ReactionTransactionFactory.FindUnresolvedTell(instance, battleInstanceId);
        if (tellTx == null)
            return;

        var tellRefName = tellTx.Data.TryGetValue(TransactionDataKeys.TellRefName, out var refName) ? refName : null;
        if (string.IsNullOrEmpty(tellRefName))
            return;

        // Prefer the authored tell (StartBattle's opening tell persists only the ref);
        // fall back to a TellText the ExecuteBattleTurn path may have persisted.
        var tell = _world.Gameplay?.AttackTells?.FirstOrDefault(t => t.RefName == tellRefName);
        var tellText = tell?.TellText
            ?? (tellTx.Data.TryGetValue(TransactionDataKeys.TellText, out var storedText) ? storedText : null);
        if (string.IsNullOrEmpty(tellText))
            return; // no narrative text to react to; GetBattleState still drives the query-side restore

        resultData[TransactionDataKeys.AwaitingReaction] = true;
        resultData[TransactionDataKeys.TellRefName] = tellRefName;
        resultData[TransactionDataKeys.TellText] = tellText;
        if (tellTx.Data.TryGetValue(TransactionDataKeys.ReactionWindowMs, out var windowStr) &&
            int.TryParse(windowStr, out var windowMs))
        {
            resultData[TransactionDataKeys.ReactionWindowMs] = windowMs;
        }
        if (tellTx.Data.TryGetValue(TransactionDataKeys.BaseDamage, out var baseDamageStr) &&
            BattleTransactionHelper.TryParseFloat(baseDamageStr, out var baseDamage))
        {
            resultData[TransactionDataKeys.BaseDamage] = baseDamage;
        }
        if (tell != null)
        {
            resultData[TransactionDataKeys.OptimalDefense] = tell.OptimalDefense.ToString();
        }
    }

    /// <summary>
    /// The enemy's health at the end of its most recent ended battle against this avatar,
    /// or null when it has never been fought (or the battle predates the absolute
    /// per-turn health snapshots).
    /// </summary>
    private static float? GetEnemyHealthFromLastEndedBattle(ArcInstance instance, Guid enemyCharacterInstanceId)
    {
        var lastEndedBattleId = instance.Transactions
            .Where(t => t.Type == ArcTransactionType.BattleStarted &&
                        t.Data.TryGetValue(TransactionDataKeys.EnemyCombatantId, out var enemyId) &&
                        enemyId == enemyCharacterInstanceId.ToString())
            .Where(started => instance.Transactions.Any(ended =>
                        ended.Type == ArcTransactionType.BattleEnded &&
                        ended.Data.TryGetValue(TransactionDataKeys.BattleTransactionId, out var endedBattleId) &&
                        endedBattleId == started.TransactionId.ToString()))
            .OrderBy(t => t.SequenceNumber)
            .LastOrDefault()?.TransactionId;

        if (lastEndedBattleId == null)
            return null;

        var lastTurnWithSnapshot = instance.Transactions
            .Where(t => t.Type == ArcTransactionType.BattleTurnExecuted &&
                        t.Data.TryGetValue(TransactionDataKeys.BattleTransactionId, out var battleId) &&
                        battleId == lastEndedBattleId.ToString() &&
                        t.Data.ContainsKey(TransactionDataKeys.EnemyHealthAfterTurn))
            .OrderBy(t => t.SequenceNumber)
            .LastOrDefault();

        if (lastTurnWithSnapshot == null)
            return null;

        return BattleTransactionHelper.TryParseFloat(
            lastTurnWithSnapshot.Data[TransactionDataKeys.EnemyHealthAfterTurn], out var health)
            ? health
            : null;
    }
}
