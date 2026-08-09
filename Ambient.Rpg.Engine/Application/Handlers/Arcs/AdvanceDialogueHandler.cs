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
/// Handler for AdvanceDialogueCommand.
/// Uses DialogueEngine to advance to next node when current node has no choices.
/// </summary>
internal sealed class AdvanceDialogueHandler : IRequestHandler<AdvanceDialogueCommand, ArcCommandResult>
{
    private readonly IArcInstanceRepository _instanceRepository;
    private readonly IArcReadModelRepository _readModelRepository;
    private readonly IAvatarProgressRepository _avatarProgressRepository;
    private readonly IAvatarUpdateService _avatarUpdateService;
    private readonly IWorld _world;
    private readonly IMediator _mediator;

    public AdvanceDialogueHandler(
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

    public async Task<ArcCommandResult> Handle(AdvanceDialogueCommand command, CancellationToken ct)
    {
        System.Diagnostics.Debug.WriteLine($"[AdvanceDialogue] Advancing dialogue for character {command.CharacterInstanceId}");

        try
        {
            // Handle dev arc refs (format: "RealArcRef__DEV__uniqueid")
            var arcRefForLookup = command.ArcRef;
            var devSuffix = "__DEV__";
            if (command.ArcRef.Contains(devSuffix))
            {
                arcRefForLookup = command.ArcRef.Substring(0, command.ArcRef.IndexOf(devSuffix));
                System.Diagnostics.Debug.WriteLine($"[AdvanceDialogue] Dev arc detected, using template ref: {arcRefForLookup}");
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

            // Track transactions before processing
            var transactionsBefore = instance.Transactions.Count;

            // Create dialogue engine with Arc context
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
            // dialogue triggers open their own battle trees)
            var sessionTreeRef = lastStarted.Data.GetValueOrDefault(TransactionDataKeys.DialogueTreeRef);
            if (!string.IsNullOrEmpty(sessionTreeRef) && sessionTreeRef != dialogueTree?.RefName &&
                _world.DialogueTreesLookup.TryGetValue(sessionTreeRef, out var sessionTree))
            {
                dialogueTree = sessionTree;
            }

            // Both the default and the session override have now had a chance to supply a tree;
            // fail only if neither did. (The old empty-default check hard-failed battle-only
            // characters before the session override could provide their battle tree.)
            if (dialogueTree == null)
            {
                return ArcCommandResult.Failure(instance.InstanceId, "Character has no dialogue tree");
            }

            // Restore state from the transaction log - jump directly to the last visited node.
            var entryNodeEvents = new List<DialogueSystemEvent>();
            var lastVisit = sessionTransactions.LastOrDefault(t => t.Type == ArcTransactionType.DialogueNodeVisited);
            if (lastVisit != null)
            {
                var lastVisitedNodeId = lastVisit.Data[TransactionDataKeys.DialogueNodeId];
                engine.RestoreToNode(dialogueTree, lastVisitedNodeId);
            }
            else
            {
                // First command of the session: the entry node's actions execute and
                // commit here (the state query is read-only and never runs them)
                var startNodeOverride = lastStarted.Data.GetValueOrDefault(TransactionDataKeys.NodeId);
                if (!string.IsNullOrEmpty(startNodeOverride))
                    engine.StartDialogueAt(dialogueTree, startNodeOverride);
                else
                    engine.StartDialogue(dialogueTree);

                // Preserve entry-node events — AdvanceDialogue clears the event queue
                entryNodeEvents = engine.PendingEvents.ToList();
            }

            // Now advance the dialogue
            var currentNode = engine.CurrentNode;
            if (currentNode == null)
            {
                return ArcCommandResult.Failure(instance.InstanceId, "No active dialogue node");
            }

            if (currentNode.Choice != null && currentNode.Choice.Length > 0)
            {
                return ArcCommandResult.Failure(instance.InstanceId, "Cannot advance dialogue - choices are present");
            }

            var nextNode = engine.AdvanceDialogue();

            System.Diagnostics.Debug.WriteLine($"[AdvanceDialogue] Advanced to node: {nextNode?.NodeId ?? "END"}");

            // Capture pending events BEFORE any EndDialogue call — EndDialogue clears the queue.
            // Entry-node events (captured before AdvanceDialogue cleared them) come first.
            var pendingEvents = entryNodeEvents.Concat(engine.PendingEvents).ToList();

            // If the new current node is terminal, end the dialogue session.
            if (engine.IsCurrentNodeTerminal)
            {
                System.Diagnostics.Debug.WriteLine("[AdvanceDialogue] Reached terminal node; ending dialogue session");
                engine.EndDialogue();
            }
            if (pendingEvents.Count > 0)
            {
                System.Diagnostics.Debug.WriteLine($"[AdvanceDialogue] {pendingEvents.Count} pending events");
            }

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
                    System.Diagnostics.Debug.WriteLine($"[AdvanceDialogue] {error}");
                }
            }

            // Get newly created transactions
            var newTransactions = instance.Transactions.Skip(transactionsBefore).ToList();

            System.Diagnostics.Debug.WriteLine($"[AdvanceDialogue] Created {newTransactions.Count} transactions");

            // Add pending events to result data
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
                    // Ledger committed but the avatar save failed: reverse the visit
                    // records so the first-visit reward block does not orphan the reward
                    // (TransactionReversed is skipped by the reward ledger checks).
                    var reversals = newTransactions
                        .Where(t => t.Type == ArcTransactionType.DialogueNodeVisited)
                        .Select(visitTx => new ArcTransaction
                        {
                            TransactionId = Guid.NewGuid(),
                            Type = ArcTransactionType.TransactionReversed,
                            AvatarId = command.AvatarId.ToString(),
                            Status = TransactionStatus.Pending,
                            LocalTimestamp = DateTime.UtcNow,
                            Data = new Dictionary<string, string>
                            {
                                [TransactionDataKeys.ReversedTransactionId] = visitTx.TransactionId.ToString(),
                                [TransactionDataKeys.Reason] = $"Avatar persistence failed: {persistEx.Message}",
                                [TransactionDataKeys.OriginalType] = visitTx.Type.ToString()
                            }
                        })
                        .ToList();

                    if (reversals.Count > 0)
                    {
                        foreach (var reversal in reversals)
                        {
                            instance.AddTransaction(reversal);
                        }
                        await _instanceRepository.AddAndCommitTransactionsAsync(instance.InstanceId, reversals, ct);
                        await _readModelRepository.InvalidateCacheAsync(command.AvatarId, command.ArcRef, ct);
                    }

                    return ArcCommandResult.Failure(
                        instance.InstanceId,
                        $"Dialogue committed but avatar update failed: {persistEx.Message}");
                }
            }

            return ArcCommandResult.Success(
                instance.InstanceId,
                newTransactions.Select(t => t.TransactionId).ToList(),
                sequenceNumbers.LastOrDefault(),
                resultData);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AdvanceDialogue] ERROR: {ex.Message}");
            return ArcCommandResult.Failure(Guid.Empty, $"Error advancing dialogue: {ex.Message}");
        }
    }
}
