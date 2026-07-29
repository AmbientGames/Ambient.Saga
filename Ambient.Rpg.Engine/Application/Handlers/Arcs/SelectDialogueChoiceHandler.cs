using Ambient.Domain.Contracts;
using Ambient.Domain.Entities;
using Ambient.Rpg.Engine.Application.Commands.Arcs;
using Ambient.Rpg.Engine.Application.ReadModels;
using Ambient.Rpg.Engine.Application.Results.Arcs;
using Ambient.Rpg.Engine.Contracts.Cqrs;
using Ambient.Rpg.Engine.Contracts.Persistence;
using Ambient.Rpg.Engine.Contracts.Services;
using Ambient.Rpg.Engine.Domain.Dialogue;
using Ambient.Rpg.Engine.Domain.Dialogue.Events;
using Ambient.Rpg.Engine.Domain.Arcs.TransactionLog;
using MediatR;
using Ambient.Rpg.Engine.Domain;

namespace Ambient.Rpg.Engine.Application.Handlers.Arcs;

/// <summary>
/// Handler for SelectDialogueChoiceCommand.
/// Uses DialogueEngine to process choice, which creates DialogueNodeVisited and action transactions.
/// </summary>
internal sealed class SelectDialogueChoiceHandler : IRequestHandler<SelectDialogueChoiceCommand, ArcCommandResult>
{
    private readonly IArcInstanceRepository _instanceRepository;
    private readonly IArcReadModelRepository _readModelRepository;
    private readonly IAvatarProgressRepository _avatarProgressRepository;
    private readonly IAvatarUpdateService _avatarUpdateService;
    private readonly IWorld _world;
    private readonly IMediator _mediator;

    public SelectDialogueChoiceHandler(
        IArcInstanceRepository instanceRepository,
        IArcReadModelRepository readModelRepository,
        IAvatarProgressRepository avatarProgressRepository,
        IAvatarUpdateService avatarUpdateService,
        IWorld world,
        IMediator mediator)
    {
        _instanceRepository = instanceRepository;
        _readModelRepository = readModelRepository;
        _avatarProgressRepository = avatarProgressRepository;
        _avatarUpdateService = avatarUpdateService;
        _world = world;
        _mediator = mediator;
    }

    public async Task<ArcCommandResult> Handle(SelectDialogueChoiceCommand command, CancellationToken ct)
    {
        System.Diagnostics.Debug.WriteLine($"[SelectDialogueChoice] Processing choice '{command.ChoiceId}' for character {command.CharacterInstanceId}");

        try
        {
            // Handle dev arc refs (format: "RealArcRef__DEV__uniqueid")
            var arcRefForLookup = command.ArcRef;
            var devSuffix = "__DEV__";
            if (command.ArcRef.Contains(devSuffix))
            {
                arcRefForLookup = command.ArcRef.Substring(0, command.ArcRef.IndexOf(devSuffix));
                System.Diagnostics.Debug.WriteLine($"[SelectDialogueChoice] Dev arc detected, using template ref: {arcRefForLookup}");
            }

            // Get Arc template (use stripped ref for lookup)
            if (!_world.ArcLookup.TryGetValue(arcRefForLookup, out var arcTemplate))
            {
                return ArcCommandResult.Failure(Guid.Empty, $"Arc '{arcRefForLookup}' not found");
            }

            // Get Arc instance (use full ref with DEV suffix for unique instance)
            var instance = await _instanceRepository.GetOrCreateInstanceAsync(command.AvatarId, command.ArcRef, ct);

            // Replay state to get current dialogue (use stripped ref for triggers)
            if (!_world.ArcTriggersLookup.TryGetValue(arcRefForLookup, out var expandedTriggers))
            {
                return ArcCommandResult.Failure(instance.InstanceId, $"Triggers not found for Arc '{arcRefForLookup}'");
            }

            var stateMachine = new ArcStateMachine(arcTemplate, expandedTriggers, _world);
            var state = stateMachine.ReplayToNow(instance);

            // Find the character
            var characterState = state.Characters.Values.FirstOrDefault(c => c.CharacterInstanceId == command.CharacterInstanceId);
            if (characterState == null)
            {
                return ArcCommandResult.Failure(instance.InstanceId, $"Character {command.CharacterInstanceId} not found");
            }

            // Get character template for dialogue tree
            if (!_world.CharactersLookup.TryGetValue(characterState.CharacterRef, out var characterTemplate))
            {
                return ArcCommandResult.Failure(instance.InstanceId, $"Character template '{characterState.CharacterRef}' not found");
            }

            // The character's default DialogueTreeRef may be empty (battle-only bosses have no
            // interactable tree). Don't fail here — the session's DialogueStarted override
            // (resolved below) can supply the tree. Fail only if NEITHER resolves.
            var dialogueTreeRef = characterTemplate.Interactable?.DialogueTreeRef;
            var dialogueTree = string.IsNullOrEmpty(dialogueTreeRef)
                ? null
                : _world.Gameplay.DialogueTrees?.FirstOrDefault(dt => dt.RefName == dialogueTreeRef);

            // Track transactions before processing choice
            var transactionsBefore = instance.Transactions.Count;

            // Create dialogue engine with Arc context (will create transactions)
            var arcContext = new ArcDialogueContext(instance, characterState.CharacterRef, command.AvatarId.ToString());
            var stateProvider = new DirectDialogueStateProvider(_world, command.Avatar, _avatarProgressRepository, command.AvatarId.ToString(), characterState.CharacterRef, instance);
            var engine = new DialogueEngine(stateProvider, arcContext);

            // Scope to the current session: transactions at or after the last DialogueStarted for this character.
            var characterTransactions = instance.Transactions
                .Where(t => t.Data.TryGetValue(TransactionDataKeys.CharacterRef, out var charRef) &&
                           charRef == characterState.CharacterRef)
                .OrderBy(t => t.SequenceNumber)
                .ToList();

            var lastStarted = characterTransactions.LastOrDefault(t => t.Type == ArcTransactionType.DialogueStarted);
            if (lastStarted == null)
            {
                return ArcCommandResult.Failure(instance.InstanceId, "No active dialogue session");
            }

            var sessionTransactions = characterTransactions
                .Where(t => t.SequenceNumber >= lastStarted.SequenceNumber)
                .ToList();

            if (sessionTransactions.Any(t => t.Type == ArcTransactionType.DialogueCompleted))
            {
                return ArcCommandResult.Failure(instance.InstanceId, "Dialogue session has already ended");
            }

            // The session's tree can differ from the character's default (battle
            // dialogue triggers open their own battle trees) — trust the session's
            // DialogueStarted transaction over the Interactable default
            var sessionTreeRef = lastStarted.Data.GetValueOrDefault(TransactionDataKeys.DialogueTreeRef);
            if (!string.IsNullOrEmpty(sessionTreeRef) && sessionTreeRef != dialogueTree?.RefName &&
                _world.DialogueTreesLookup.TryGetValue(sessionTreeRef, out var sessionTree))
            {
                dialogueTree = sessionTree;
            }

            // Both the default and the session override have now had a chance to supply a tree;
            // fail only if neither did. (The old empty-default check hard-failed battle-only
            // characters before the session override could provide their battle tree, soft-locking
            // e.g. EverestTrail's BanditChief_Boss on every choice.)
            if (dialogueTree == null)
            {
                return ArcCommandResult.Failure(instance.InstanceId, "Character has no dialogue tree");
            }

            // Restore state from the transaction log - jump directly to the last visited node.
            // This avoids re-running actions and re-evaluating conditions that may have changed.
            var entryNodeEvents = new List<DialogueSystemEvent>();
            var lastVisit = sessionTransactions.LastOrDefault(t => t.Type == ArcTransactionType.DialogueNodeVisited);
            if (lastVisit != null)
            {
                var lastVisitedNodeId = lastVisit.Data[TransactionDataKeys.DialogueNodeId];
                engine.RestoreToNode(dialogueTree, lastVisitedNodeId);
                System.Diagnostics.Debug.WriteLine($"[SelectDialogueChoice] Restored to node: {lastVisitedNodeId}");
            }
            else
            {
                // No visits yet - navigate from the session's start node (battle
                // triggers author per-moment entry points) or the tree default.
                // This is where the entry node's actions execute and commit — the
                // state query is read-only and never runs them.
                // ResumeDialogue, NOT StartDialogue: the session's DialogueStarted
                // was already committed by StartDialogueHandler, and re-emitting one
                // here duplicated it in every session's log.
                var startNodeOverride = lastStarted.Data.GetValueOrDefault(TransactionDataKeys.NodeId);
                engine.ResumeDialogue(dialogueTree, startNodeOverride);
                System.Diagnostics.Debug.WriteLine($"[SelectDialogueChoice] No prior visits; navigated from start to: {engine.CurrentNode?.NodeId ?? "null"}");

                // Preserve entry-node events (ChangeStance, StartCombat, ...) —
                // SelectChoice clears the event queue before navigating
                entryNodeEvents = engine.PendingEvents.ToList();
            }

            // Find the choice in the current node
            var currentNode = engine.CurrentNode;
            if (currentNode == null)
            {
                // The entry-node chain dead-ended, so ResumeDialogue sealed the
                // session (M16). Commit the seal before failing so the session
                // state converges instead of looping on CanContinue.
                await CommitSessionSealAsync(instance, transactionsBefore, command, ct);
                return ArcCommandResult.Failure(instance.InstanceId, "No active dialogue node");
            }

            var choice = currentNode.Choice?.FirstOrDefault(c => c.NextNodeId == command.ChoiceId);
            if (choice == null)
            {
                return ArcCommandResult.Failure(instance.InstanceId, $"Choice '{command.ChoiceId}' not found in current node");
            }

            // Process choice (creates DialogueNodeVisited and action transactions automatically)
            var nextNode = engine.SelectChoice(choice);

            System.Diagnostics.Debug.WriteLine($"[SelectDialogueChoice] Navigated to node: {nextNode?.NodeId ?? "END"}");

            // Check for pending system events (OpenMerchantTrade, StartBossBattle, etc.)
            // Capture BEFORE any EndDialogue call — EndDialogue clears the event queue.
            // Entry-node events (captured before SelectChoice cleared them) come first.
            var pendingEvents = entryNodeEvents.Concat(engine.PendingEvents).ToList();

            // If the new current node is terminal, end the dialogue session.
            // This creates a DialogueCompleted transaction so the session is sealed and
            // DialogueCompleted-type quest objectives fire.
            var isTerminal = engine.IsCurrentNodeTerminal;
            if (isTerminal)
            {
                System.Diagnostics.Debug.WriteLine("[SelectDialogueChoice] Reached terminal node; ending dialogue session");
                engine.EndDialogue();
            }
            if (pendingEvents.Count > 0)
            {
                System.Diagnostics.Debug.WriteLine($"[SelectDialogueChoice] {pendingEvents.Count} pending events:");
                foreach (var evt in pendingEvents)
                {
                    System.Diagnostics.Debug.WriteLine($"  - {evt.GetType().Name}");
                }
            }

            // Snapshot the transactions the DIALOGUE ENGINE created (visit + staged
            // rewards + optional session seal) BEFORE the nested quest commands run.
            // Nested quest handlers commit their own transactions onto this instance
            // with their own compensation, so the persist-failure compensation below
            // must reverse exactly this set and nothing else (M3).
            var dialogueEngineTransactions = instance.Transactions.Skip(transactionsBefore).ToList();

            // Dispatch quest events directly (business logic, not UI transitions).
            // Authored order matters: a node's [CompleteQuest A, AcceptQuest B] must
            // complete A before B's prerequisite check runs, and a failed nested
            // command must be surfaced, not swallowed.
            bool gameComplete = false;
            string? completionQuestRef = null;
            var questEventErrors = new List<string>();
            if (pendingEvents.Exists(e => e is AcceptQuestEvent or CompleteQuestEvent or AbandonQuestEvent)
                && command.Avatar is AvatarEntity avatarEntity)
            {
                var questEvents = pendingEvents
                    .Where(e => e is AcceptQuestEvent or CompleteQuestEvent or AbandonQuestEvent)
                    .ToList();
                pendingEvents.RemoveAll(e => e is AcceptQuestEvent or CompleteQuestEvent or AbandonQuestEvent);

                foreach (var evt in questEvents)
                {
                    switch (evt)
                    {
                        case AcceptQuestEvent acceptEvt:
                            var acceptResult = await _mediator.Send(new AcceptQuestCommand
                            {
                                AvatarId = command.AvatarId,
                                ArcRef = acceptEvt.ArcRef,
                                QuestRef = acceptEvt.QuestRef,
                                QuestGiverRef = acceptEvt.QuestGiverRef,
                                Avatar = avatarEntity
                            }, ct);
                            if (!acceptResult.Successful)
                                questEventErrors.Add($"AcceptQuest '{acceptEvt.QuestRef}' failed: {acceptResult.ErrorMessage}");
                            break;

                        case CompleteQuestEvent completeEvt:
                            var questResult = await _mediator.Send(new CompleteQuestCommand
                            {
                                AvatarId = command.AvatarId,
                                ArcRef = completeEvt.ArcRef,
                                QuestRef = completeEvt.QuestRef,
                                QuestReceiverRef = characterState.CharacterRef,
                                Avatar = avatarEntity,
                                DialogueDriven = true
                            }, ct);
                            if (!questResult.Successful)
                            {
                                questEventErrors.Add($"CompleteQuest '{completeEvt.QuestRef}' failed: {questResult.ErrorMessage}");
                            }
                            else if (questResult.Data.ContainsKey(TransactionDataKeys.GameComplete))
                            {
                                gameComplete = true;
                                completionQuestRef = questResult.Data.TryGetValue(TransactionDataKeys.CompletionQuestRef, out var qref) ? qref as string : null;
                            }
                            break;

                        case AbandonQuestEvent abandonEvt:
                            var abandonResult = await _mediator.Send(new AbandonQuestCommand
                            {
                                AvatarId = command.AvatarId,
                                ArcRef = abandonEvt.ArcRef,
                                QuestRef = abandonEvt.QuestRef,
                                Avatar = avatarEntity
                            }, ct);
                            if (!abandonResult.Successful)
                                questEventErrors.Add($"AbandonQuest '{abandonEvt.QuestRef}' failed: {abandonResult.ErrorMessage}");
                            break;
                    }
                }

                foreach (var error in questEventErrors)
                {
                    System.Diagnostics.Debug.WriteLine($"[SelectDialogueChoice] {error}");
                }
            }

            // Get newly created transactions
            var newTransactions = instance.Transactions.Skip(transactionsBefore).ToList();

            System.Diagnostics.Debug.WriteLine($"[SelectDialogueChoice] Created {newTransactions.Count} transactions");
            foreach (var tx in newTransactions)
            {
                System.Diagnostics.Debug.WriteLine($"  - {tx.Type}: {string.Join(", ", tx.Data.Select(kvp => $"{kvp.Key}={kvp.Value}"))}");
            }

            // Add pending events to result data so caller can process them
            var resultData = new Dictionary<string, object>();
            if (pendingEvents.Count > 0)
            {
                resultData[TransactionDataKeys.PendingEvents] = pendingEvents;
            }
            if (gameComplete)
            {
                resultData[TransactionDataKeys.GameComplete] = true;
                if (completionQuestRef != null)
                    resultData[TransactionDataKeys.CompletionQuestRef] = completionQuestRef;
            }
            // Nested quest command failures don't fail the dialogue advance (the
            // navigation itself succeeded) but must be visible to the caller
            if (questEventErrors.Count > 0)
            {
                resultData[TransactionDataKeys.QuestEventErrors] = questEventErrors;
            }

            if (newTransactions.Count == 0)
            {
                return ArcCommandResult.Success(instance.InstanceId, new List<Guid>(), instance.Transactions.Count, resultData);
            }

            // Persist and commit
            var (sequenceNumbers, committed) = await _instanceRepository.AddAndCommitTransactionsAsync(instance.InstanceId, newTransactions, ct);

            if (!committed)
            {
                return ArcCommandResult.Failure(instance.InstanceId, "Concurrency conflict - transactions rolled back");
            }

            // Invalidate cache
            await _readModelRepository.InvalidateCacheAsync(command.AvatarId, command.ArcRef, ct);

            // The committed DialogueNodeVisited ledger above blocks any re-grant, so a
            // reward living only on the in-memory avatar would be lost forever on a crash
            // before the host's periodic save (audit B6). Persist now, like TradeItemHandler.
            // AvatarMutated is only set when a node actually changed the avatar (first
            // visit with give/take/currency actions), so condition-only navigation and
            // repeat visits cost no write.
            if (stateProvider.AvatarMutated && command.Avatar is AvatarEntity rewardedAvatar)
            {
                try
                {
                    await _avatarUpdateService.PersistAvatarAsync(rewardedAvatar, ct);
                }
                catch (Exception persistEx)
                {
                    // Ledger committed but the avatar save failed: reverse EVERYTHING
                    // the dialogue engine staged in this batch — the visit records
                    // (so the first-visit reward block does not orphan the reward)
                    // AND the reward-side transactions (ReputationChanged /
                    // CurrencyChanged / TraitAssigned / QuestTokenAwarded / ...).
                    // Reversing only the visits left the reward transactions
                    // committed and projected, so a successful retry re-committed
                    // them and double-awarded reputation (M3).
                    await ReverseDialogueTransactionsAsync(instance, dialogueEngineTransactions, command, persistEx, ct);

                    return ArcCommandResult.Failure(
                        instance.InstanceId,
                        $"Dialogue committed but avatar update failed: {persistEx.Message}");
                }
            }

            return ArcCommandResult.Success(
                instance.InstanceId,
                newTransactions.Select(t => t.TransactionId).ToList(),
                sequenceNumbers.Last(),
                resultData);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SelectDialogueChoice] ERROR: {ex.Message}");
            return ArcCommandResult.Failure(Guid.Empty, $"Error selecting dialogue choice: {ex.Message}");
        }
    }

    /// <summary>
    /// Dialogue-engine transaction types that the persist-failure compensation must
    /// reverse: the visit ledger plus every reward type the action executor stages.
    /// Session markers (DialogueStarted/DialogueCompleted) are deliberately excluded.
    /// </summary>
    private static readonly HashSet<ArcTransactionType> ReversibleDialogueTypes = new()
    {
        ArcTransactionType.DialogueNodeVisited,
        ArcTransactionType.QuestTokenAwarded,
        ArcTransactionType.CurrencyChanged,
        ArcTransactionType.TraitAssigned,
        ArcTransactionType.TraitRemoved,
        ArcTransactionType.ReputationChanged,
        ArcTransactionType.PartyMemberJoined,
        ArcTransactionType.PartyMemberLeft,
        ArcTransactionType.AffinityGranted
    };

    /// <summary>
    /// M3 compensation: emits a TransactionReversed per staged dialogue transaction
    /// (visit AND rewards) and undoes their cross-arc projections (reputation, quest
    /// tokens, traits) so a retry nets a single award instead of a double one.
    /// </summary>
    private async Task ReverseDialogueTransactionsAsync(
        ArcInstance instance,
        List<ArcTransaction> dialogueEngineTransactions,
        SelectDialogueChoiceCommand command,
        Exception persistEx,
        CancellationToken ct)
    {
        var reversedOriginals = dialogueEngineTransactions
            .Where(t => ReversibleDialogueTypes.Contains(t.Type))
            .ToList();

        if (reversedOriginals.Count == 0)
            return;

        var reversals = reversedOriginals
            .Select(originalTx => new ArcTransaction
            {
                TransactionId = Guid.NewGuid(),
                Type = ArcTransactionType.TransactionReversed,
                AvatarId = command.AvatarId.ToString(),
                Status = TransactionStatus.Pending,
                LocalTimestamp = DateTime.UtcNow,
                Data = new Dictionary<string, string>
                {
                    [TransactionDataKeys.ReversedTransactionId] = originalTx.TransactionId.ToString(),
                    [TransactionDataKeys.Reason] = $"Avatar persistence failed: {persistEx.Message}",
                    [TransactionDataKeys.OriginalType] = originalTx.Type.ToString()
                }
            })
            .ToList();

        foreach (var reversal in reversals)
        {
            instance.AddTransaction(reversal);
        }

        var (_, reversalsCommitted) = await _instanceRepository.AddAndCommitTransactionsAsync(instance.InstanceId, reversals, ct);

        // Undo the projections the original commit applied (ReputationChanged is the
        // non-idempotent one — without this a retry re-projects and doubles it)
        if (reversalsCommitted)
        {
            _avatarProgressRepository.ReverseTransactions(command.AvatarId, command.ArcRef, reversedOriginals);
        }

        await _readModelRepository.InvalidateCacheAsync(command.AvatarId, command.ArcRef, ct);
    }

    /// <summary>
    /// Commits the session seal (DialogueCompleted) the engine staged when the entry
    /// chain dead-ended, so a doomed session converges instead of looping (M16).
    /// </summary>
    private async Task CommitSessionSealAsync(
        ArcInstance instance,
        int transactionsBefore,
        SelectDialogueChoiceCommand command,
        CancellationToken ct)
    {
        var sealTransactions = instance.Transactions.Skip(transactionsBefore).ToList();
        if (sealTransactions.Count == 0)
            return;

        await _instanceRepository.AddAndCommitTransactionsAsync(instance.InstanceId, sealTransactions, ct);
        await _readModelRepository.InvalidateCacheAsync(command.AvatarId, command.ArcRef, ct);
    }
}
