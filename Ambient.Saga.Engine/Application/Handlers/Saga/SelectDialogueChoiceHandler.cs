using Ambient.Domain.Contracts;
using Ambient.Domain.Entities;
using Ambient.Saga.Engine.Application.Commands.Saga;
using Ambient.Saga.Engine.Application.ReadModels;
using Ambient.Saga.Engine.Application.Results.Saga;
using Ambient.Saga.Engine.Contracts.Cqrs;
using Ambient.Saga.Engine.Contracts.Persistence;
using Ambient.Saga.Engine.Domain.Rpg.Dialogue;
using Ambient.Saga.Engine.Domain.Rpg.Dialogue.Events;
using Ambient.Saga.Engine.Domain.Rpg.Sagas.TransactionLog;
using MediatR;
using Ambient.Saga.Engine.Domain;

namespace Ambient.Saga.Engine.Application.Handlers.Saga;

/// <summary>
/// Handler for SelectDialogueChoiceCommand.
/// Uses DialogueEngine to process choice, which creates DialogueNodeVisited and action transactions.
/// </summary>
internal sealed class SelectDialogueChoiceHandler : IRequestHandler<SelectDialogueChoiceCommand, SagaCommandResult>
{
    private readonly ISagaInstanceRepository _instanceRepository;
    private readonly ISagaReadModelRepository _readModelRepository;
    private readonly IAvatarProgressRepository _avatarProgressRepository;
    private readonly IWorld _world;
    private readonly IMediator _mediator;

    public SelectDialogueChoiceHandler(
        ISagaInstanceRepository instanceRepository,
        ISagaReadModelRepository readModelRepository,
        IAvatarProgressRepository avatarProgressRepository,
        IWorld world,
        IMediator mediator)
    {
        _instanceRepository = instanceRepository;
        _readModelRepository = readModelRepository;
        _avatarProgressRepository = avatarProgressRepository;
        _world = world;
        _mediator = mediator;
    }

    public async Task<SagaCommandResult> Handle(SelectDialogueChoiceCommand command, CancellationToken ct)
    {
        System.Diagnostics.Debug.WriteLine($"[SelectDialogueChoice] Processing choice '{command.ChoiceId}' for character {command.CharacterInstanceId}");

        try
        {
            // Handle dev saga refs (format: "RealSagaRef__DEV__uniqueid")
            var sagaRefForLookup = command.SagaArcRef;
            var devSuffix = "__DEV__";
            if (command.SagaArcRef.Contains(devSuffix))
            {
                sagaRefForLookup = command.SagaArcRef.Substring(0, command.SagaArcRef.IndexOf(devSuffix));
                System.Diagnostics.Debug.WriteLine($"[SelectDialogueChoice] Dev saga detected, using template ref: {sagaRefForLookup}");
            }

            // Get Saga template (use stripped ref for lookup)
            if (!_world.SagaArcLookup.TryGetValue(sagaRefForLookup, out var sagaTemplate))
            {
                return SagaCommandResult.Failure(Guid.Empty, $"Saga '{sagaRefForLookup}' not found");
            }

            // Get Saga instance (use full ref with DEV suffix for unique instance)
            var instance = await _instanceRepository.GetOrCreateInstanceAsync(command.AvatarId, command.SagaArcRef, ct);

            // Replay state to get current dialogue (use stripped ref for triggers)
            if (!_world.SagaTriggersLookup.TryGetValue(sagaRefForLookup, out var expandedTriggers))
            {
                return SagaCommandResult.Failure(instance.InstanceId, $"Triggers not found for Saga '{sagaRefForLookup}'");
            }

            var stateMachine = new SagaStateMachine(sagaTemplate, expandedTriggers, _world);
            var state = stateMachine.ReplayToNow(instance);

            // Find the character
            var characterState = state.Characters.Values.FirstOrDefault(c => c.CharacterInstanceId == command.CharacterInstanceId);
            if (characterState == null)
            {
                return SagaCommandResult.Failure(instance.InstanceId, $"Character {command.CharacterInstanceId} not found");
            }

            // Get character template for dialogue tree
            if (!_world.CharactersLookup.TryGetValue(characterState.CharacterRef, out var characterTemplate))
            {
                return SagaCommandResult.Failure(instance.InstanceId, $"Character template '{characterState.CharacterRef}' not found");
            }

            var dialogueTreeRef = characterTemplate.Interactable?.DialogueTreeRef;
            if (string.IsNullOrEmpty(dialogueTreeRef))
            {
                return SagaCommandResult.Failure(instance.InstanceId, "Character has no dialogue tree");
            }

            // Get dialogue tree
            var dialogueTree = _world.Gameplay.DialogueTrees?.FirstOrDefault(dt => dt.RefName == dialogueTreeRef);
            if (dialogueTree == null)
            {
                return SagaCommandResult.Failure(instance.InstanceId, $"Dialogue tree '{dialogueTreeRef}' not found");
            }

            // Track transactions before processing choice
            var transactionsBefore = instance.Transactions.Count;

            // Create dialogue engine with Saga context (will create transactions)
            var sagaContext = new SagaDialogueContext(instance, characterState.CharacterRef, command.AvatarId.ToString());
            var stateProvider = new DirectDialogueStateProvider(_world, command.Avatar, _avatarProgressRepository, command.AvatarId.ToString(), characterState.CharacterRef);
            var engine = new DialogueEngine(stateProvider, sagaContext);

            // Scope to the current session: transactions at or after the last DialogueStarted for this character.
            var characterTransactions = instance.Transactions
                .Where(t => t.Data.TryGetValue(TransactionDataKeys.CharacterRef, out var charRef) &&
                           charRef == characterState.CharacterRef)
                .OrderBy(t => t.SequenceNumber)
                .ToList();

            var lastStarted = characterTransactions.LastOrDefault(t => t.Type == SagaTransactionType.DialogueStarted);
            if (lastStarted == null)
            {
                return SagaCommandResult.Failure(instance.InstanceId, "No active dialogue session");
            }

            var sessionTransactions = characterTransactions
                .Where(t => t.SequenceNumber >= lastStarted.SequenceNumber)
                .ToList();

            if (sessionTransactions.Any(t => t.Type == SagaTransactionType.DialogueCompleted))
            {
                return SagaCommandResult.Failure(instance.InstanceId, "Dialogue session has already ended");
            }

            // The session's tree can differ from the character's default (battle
            // dialogue triggers open their own battle trees) — trust the session's
            // DialogueStarted transaction over the Interactable default
            var sessionTreeRef = lastStarted.Data.GetValueOrDefault(TransactionDataKeys.DialogueTreeRef);
            if (!string.IsNullOrEmpty(sessionTreeRef) && sessionTreeRef != dialogueTree.RefName &&
                _world.DialogueTreesLookup.TryGetValue(sessionTreeRef, out var sessionTree))
            {
                dialogueTree = sessionTree;
            }

            // Restore state from the transaction log - jump directly to the last visited node.
            // This avoids re-running actions and re-evaluating conditions that may have changed.
            var entryNodeEvents = new List<DialogueSystemEvent>();
            var lastVisit = sessionTransactions.LastOrDefault(t => t.Type == SagaTransactionType.DialogueNodeVisited);
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
                var startNodeOverride = lastStarted.Data.GetValueOrDefault(TransactionDataKeys.NodeId);
                if (!string.IsNullOrEmpty(startNodeOverride))
                    engine.StartDialogueAt(dialogueTree, startNodeOverride);
                else
                    engine.StartDialogue(dialogueTree);
                System.Diagnostics.Debug.WriteLine($"[SelectDialogueChoice] No prior visits; navigated from start to: {engine.CurrentNode?.NodeId ?? "null"}");

                // Preserve entry-node events (ChangeStance, StartCombat, ...) —
                // SelectChoice clears the event queue before navigating
                entryNodeEvents = engine.PendingEvents.ToList();
            }

            // Find the choice in the current node
            var currentNode = engine.CurrentNode;
            if (currentNode == null)
            {
                return SagaCommandResult.Failure(instance.InstanceId, "No active dialogue node");
            }

            var choice = currentNode.Choice?.FirstOrDefault(c => c.NextNodeId == command.ChoiceId);
            if (choice == null)
            {
                return SagaCommandResult.Failure(instance.InstanceId, $"Choice '{command.ChoiceId}' not found in current node");
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
                                SagaArcRef = acceptEvt.SagaRef,
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
                                SagaArcRef = completeEvt.SagaRef,
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
                                SagaArcRef = abandonEvt.SagaRef,
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
                return SagaCommandResult.Success(instance.InstanceId, new List<Guid>(), instance.Transactions.Count, resultData);
            }

            // Persist and commit
            var (sequenceNumbers, committed) = await _instanceRepository.AddAndCommitTransactionsAsync(instance.InstanceId, newTransactions, ct);

            if (!committed)
            {
                return SagaCommandResult.Failure(instance.InstanceId, "Concurrency conflict - transactions rolled back");
            }

            // Invalidate cache
            await _readModelRepository.InvalidateCacheAsync(command.AvatarId, command.SagaArcRef, ct);

            return SagaCommandResult.Success(
                instance.InstanceId,
                newTransactions.Select(t => t.TransactionId).ToList(),
                sequenceNumbers.Last(),
                resultData);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SelectDialogueChoice] ERROR: {ex.Message}");
            return SagaCommandResult.Failure(Guid.Empty, $"Error selecting dialogue choice: {ex.Message}");
        }
    }
}
