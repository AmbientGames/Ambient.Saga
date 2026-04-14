using Ambient.Domain.Contracts;
using Ambient.Domain.Entities;
using Ambient.Saga.Engine.Application.Commands.Saga;
using Ambient.Saga.Engine.Application.ReadModels;
using Ambient.Saga.Engine.Application.Results.Saga;
using Ambient.Saga.Engine.Contracts.Cqrs;
using Ambient.Saga.Engine.Domain.Rpg.Dialogue;
using Ambient.Saga.Engine.Domain.Rpg.Dialogue.Events;
using Ambient.Saga.Engine.Domain.Rpg.Sagas.TransactionLog;
using MediatR;
using Ambient.Saga.Engine.Domain;

namespace Ambient.Saga.Engine.Application.Handlers.Saga;

/// <summary>
/// Handler for AdvanceDialogueCommand.
/// Uses DialogueEngine to advance to next node when current node has no choices.
/// </summary>
internal sealed class AdvanceDialogueHandler : IRequestHandler<AdvanceDialogueCommand, SagaCommandResult>
{
    private readonly ISagaInstanceRepository _instanceRepository;
    private readonly ISagaReadModelRepository _readModelRepository;
    private readonly IWorld _world;
    private readonly IMediator _mediator;

    public AdvanceDialogueHandler(
        ISagaInstanceRepository instanceRepository,
        ISagaReadModelRepository readModelRepository,
        IWorld world,
        IMediator mediator)
    {
        _instanceRepository = instanceRepository;
        _readModelRepository = readModelRepository;
        _world = world;
        _mediator = mediator;
    }

    public async Task<SagaCommandResult> Handle(AdvanceDialogueCommand command, CancellationToken ct)
    {
        System.Diagnostics.Debug.WriteLine($"[AdvanceDialogue] Advancing dialogue for character {command.CharacterInstanceId}");

        try
        {
            // Handle dev saga refs (format: "RealSagaRef__DEV__uniqueid")
            var sagaRefForLookup = command.SagaArcRef;
            var devSuffix = "__DEV__";
            if (command.SagaArcRef.Contains(devSuffix))
            {
                sagaRefForLookup = command.SagaArcRef.Substring(0, command.SagaArcRef.IndexOf(devSuffix));
                System.Diagnostics.Debug.WriteLine($"[AdvanceDialogue] Dev saga detected, using template ref: {sagaRefForLookup}");
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

            // Track transactions before processing
            var transactionsBefore = instance.Transactions.Count;

            // Create dialogue engine with Saga context
            var sagaContext = new SagaDialogueContext(instance, characterState.CharacterRef, command.AvatarId.ToString());
            var stateProvider = new DirectDialogueStateProvider(_world, command.Avatar);
            var engine = new DialogueEngine(stateProvider, sagaContext);

            // Start dialogue (idempotent)
            engine.StartDialogue(dialogueTree);

            // Replay to current node by following visited nodes
            var visitedNodes = instance.Transactions
                .Where(t => t.Type == SagaTransactionType.DialogueNodeVisited &&
                           t.Data.TryGetValue(TransactionDataKeys.CharacterRef, out var charRef) &&
                           charRef == characterState.CharacterRef)
                .OrderBy(t => t.SequenceNumber)
                .ToList();

            foreach (var visitedTx in visitedNodes)
            {
                var nodeId = visitedTx.Data[TransactionDataKeys.DialogueNodeId];
                if (engine.CurrentNode != null)
                {
                    var choice = engine.CurrentNode.Choice?.FirstOrDefault(c => c.NextNodeId == nodeId);
                    if (choice != null)
                    {
                        engine.SelectChoice(choice);
                    }
                    else if (engine.CurrentNode.NextNodeId == nodeId)
                    {
                        // It was an auto-advance, not a choice
                        engine.AdvanceDialogue();
                    }
                }
            }

            // Now advance the dialogue
            var currentNode = engine.CurrentNode;
            if (currentNode == null)
            {
                return SagaCommandResult.Failure(instance.InstanceId, "No active dialogue node");
            }

            if (currentNode.Choice != null && currentNode.Choice.Length > 0)
            {
                return SagaCommandResult.Failure(instance.InstanceId, "Cannot advance dialogue - choices are present");
            }

            var nextNode = engine.AdvanceDialogue();

            System.Diagnostics.Debug.WriteLine($"[AdvanceDialogue] Advanced to node: {nextNode?.NodeId ?? "END"}");

            // Check for pending system events
            var pendingEvents = engine.PendingEvents.ToList();
            if (pendingEvents.Count > 0)
            {
                System.Diagnostics.Debug.WriteLine($"[AdvanceDialogue] {pendingEvents.Count} pending events");
            }

            // Dispatch quest events directly (business logic, not UI transitions)
            bool gameComplete = false;
            string? completionQuestRef = null;
            if (pendingEvents.Exists(e => e is AcceptQuestEvent or CompleteQuestEvent or AbandonQuestEvent)
                && command.Avatar is AvatarEntity avatarEntity)
            {
                for (int i = pendingEvents.Count - 1; i >= 0; i--)
                {
                    var evt = pendingEvents[i];
                    switch (evt)
                    {
                        case AcceptQuestEvent acceptEvt:
                            await _mediator.Send(new AcceptQuestCommand
                            {
                                AvatarId = command.AvatarId,
                                SagaArcRef = acceptEvt.SagaRef,
                                QuestRef = acceptEvt.QuestRef,
                                QuestGiverRef = acceptEvt.QuestGiverRef,
                                Avatar = avatarEntity
                            }, ct);
                            pendingEvents.RemoveAt(i);
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
                            if (questResult.Data.ContainsKey(TransactionDataKeys.GameComplete))
                            {
                                gameComplete = true;
                                completionQuestRef = questResult.Data.TryGetValue(TransactionDataKeys.CompletionQuestRef, out var qref) ? qref as string : null;
                            }
                            pendingEvents.RemoveAt(i);
                            break;

                        case AbandonQuestEvent abandonEvt:
                            await _mediator.Send(new AbandonQuestCommand
                            {
                                AvatarId = command.AvatarId,
                                SagaArcRef = abandonEvt.SagaRef,
                                QuestRef = abandonEvt.QuestRef,
                                Avatar = avatarEntity
                            }, ct);
                            pendingEvents.RemoveAt(i);
                            break;
                    }
                }
            }

            // Get newly created transactions
            var newTransactions = instance.Transactions.Skip(transactionsBefore).ToList();

            System.Diagnostics.Debug.WriteLine($"[AdvanceDialogue] Created {newTransactions.Count} transactions");

            if (newTransactions.Count == 0)
            {
                return SagaCommandResult.Success(instance.InstanceId, new List<Guid>(), instance.Transactions.Count);
            }

            // Persist and commit
            var (sequenceNumbers, committed) = await _instanceRepository.AddAndCommitTransactionsAsync(instance.InstanceId, newTransactions, ct);

            if (!committed)
            {
                return SagaCommandResult.Failure(instance.InstanceId, "Concurrency conflict - transactions rolled back");
            }

            // Invalidate cache
            await _readModelRepository.InvalidateCacheAsync(command.AvatarId, command.SagaArcRef, ct);

            // Fan out any QuestTokenAwarded transactions to every other saga instance whose triggers
            // require the token. Auto-advance dialogue nodes can award tokens just like choice-driven ones.
            var awardedTokenRefs = newTransactions
                .Where(t => t.Type == SagaTransactionType.QuestTokenAwarded)
                .Select(t => t.GetData<string>(TransactionDataKeys.QuestTokenRef))
                .Where(r => !string.IsNullOrEmpty(r))
                .Distinct()
                .ToList();

            if (awardedTokenRefs.Count > 0)
            {
                await QuestTokenFanOut.FanOutAsync(
                    command.AvatarId, command.SagaArcRef, awardedTokenRefs,
                    _instanceRepository, _readModelRepository, _world, ct);
            }

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

            return SagaCommandResult.Success(
                instance.InstanceId,
                newTransactions.Select(t => t.TransactionId).ToList(),
                sequenceNumbers.LastOrDefault(),
                resultData);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AdvanceDialogue] ERROR: {ex.Message}");
            return SagaCommandResult.Failure(Guid.Empty, $"Error advancing dialogue: {ex.Message}");
        }
    }
}
