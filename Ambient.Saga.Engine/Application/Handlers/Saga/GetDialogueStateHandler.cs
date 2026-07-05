using Ambient.Domain.Contracts;
using Ambient.Saga.Engine.Application.Queries.Saga;
using Ambient.Saga.Engine.Application.Results.Saga;
using Ambient.Saga.Engine.Contracts.Cqrs;
using Ambient.Saga.Engine.Contracts.Persistence;
using Ambient.Saga.Engine.Domain.Rpg.Dialogue;
using Ambient.Saga.Engine.Domain.Rpg.Dialogue.Evaluation;
using Ambient.Saga.Engine.Domain.Rpg.Sagas.TransactionLog;
using MediatR;
using Ambient.Saga.Engine.Domain;

namespace Ambient.Saga.Engine.Application.Handlers.Saga;

/// <summary>
/// Handler for GetDialogueStateQuery.
/// Replays dialogue transactions to determine current node, text, and available choices.
/// </summary>
internal sealed class GetDialogueStateHandler : IRequestHandler<GetDialogueStateQuery, DialogueStateResult>
{
    private readonly ISagaInstanceRepository _instanceRepository;
    private readonly IAvatarProgressRepository _avatarProgressRepository;
    private readonly IWorld _world;

    public GetDialogueStateHandler(
        ISagaInstanceRepository instanceRepository,
        IAvatarProgressRepository avatarProgressRepository,
        IWorld world)
    {
        _instanceRepository = instanceRepository;
        _avatarProgressRepository = avatarProgressRepository;
        _world = world;
    }

    public async Task<DialogueStateResult> Handle(GetDialogueStateQuery query, CancellationToken ct)
    {
        System.Diagnostics.Debug.WriteLine($"[GetDialogueState] Querying dialogue state for character {query.CharacterInstanceId}");

        try
        {
            // Handle dev saga refs (format: "RealSagaRef__DEV__uniqueid")
            var sagaRefForLookup = query.SagaRef;
            var devSuffix = "__DEV__";
            if (query.SagaRef.Contains(devSuffix))
            {
                sagaRefForLookup = query.SagaRef.Substring(0, query.SagaRef.IndexOf(devSuffix));
                System.Diagnostics.Debug.WriteLine($"[GetDialogueState] Dev saga detected, using template ref: {sagaRefForLookup}");
            }

            // Get Saga template (use stripped ref for lookup)
            if (!_world.SagaArcLookup.TryGetValue(sagaRefForLookup, out var sagaTemplate))
            {
                return new DialogueStateResult { IsActive = false, HasEnded = false };
            }

            // Get Saga instance (use full ref with DEV suffix for unique instance)
            var instance = await _instanceRepository.GetOrCreateInstanceAsync(query.AvatarId, query.SagaRef, ct);

            // Replay state to get character ref from instance ID (use stripped ref for triggers)
            if (!_world.SagaTriggersLookup.TryGetValue(sagaRefForLookup, out var expandedTriggers))
            {
                return new DialogueStateResult { IsActive = false, HasEnded = false };
            }

            var stateMachine = new SagaStateMachine(sagaTemplate, expandedTriggers, _world);
            var state = stateMachine.ReplayToNow(instance);

            // Find the character to get their CharacterRef
            var characterState = state.Characters.Values.FirstOrDefault(c => c.CharacterInstanceId == query.CharacterInstanceId);
            if (characterState == null)
            {
                System.Diagnostics.Debug.WriteLine($"[GetDialogueState] Character {query.CharacterInstanceId} not found in Saga state");
                return new DialogueStateResult { IsActive = false, HasEnded = false };
            }

            var characterRef = characterState.CharacterRef;
            System.Diagnostics.Debug.WriteLine($"[GetDialogueState] Character ref: {characterRef}");

            // Find dialogue transactions for this character using CharacterRef (not CharacterInstanceId)
            var dialogueTransactions = instance.Transactions
                .Where(t => t.Data.TryGetValue(TransactionDataKeys.CharacterRef, out var charRef) &&
                           charRef == characterRef)
                .OrderBy(t => t.SequenceNumber)
                .ToList();

            System.Diagnostics.Debug.WriteLine($"[GetDialogueState] Found {dialogueTransactions.Count} dialogue transactions");

            // Scope to the current session: the most recent DialogueStarted is the current session.
            // A session ends when a DialogueCompleted follows. We want state for the session containing
            // (or immediately following) the last DialogueStarted.
            var lastStarted = dialogueTransactions.LastOrDefault(t => t.Type == SagaTransactionType.DialogueStarted);
            if (lastStarted == null)
            {
                System.Diagnostics.Debug.WriteLine("[GetDialogueState] No dialogue has been started");
                return new DialogueStateResult { IsActive = false, HasEnded = false };
            }

            var sessionTransactions = dialogueTransactions
                .Where(t => t.SequenceNumber >= lastStarted.SequenceNumber)
                .ToList();

            var sessionCompleted = sessionTransactions.FirstOrDefault(t => t.Type == SagaTransactionType.DialogueCompleted);
            var sessionEnded = sessionCompleted != null;

            // Get dialogue tree from DialogueStarted transaction
            var dialogueTreeRef = lastStarted.Data[TransactionDataKeys.DialogueTreeRef];
            var dialogueTree = _world.Gameplay.DialogueTrees?.FirstOrDefault(dt => dt.RefName == dialogueTreeRef);
            if (dialogueTree == null)
            {
                System.Diagnostics.Debug.WriteLine($"[GetDialogueState] ERROR: Dialogue tree '{dialogueTreeRef}' not found");
                return new DialogueStateResult { IsActive = false, HasEnded = false };
            }

            // Create dialogue engine with Saga context (characterRef was already extracted
            // earlier). The provider needs the SAME characterRef the command path passes:
            // without it, conditions like IsInParty with an empty RefName ("the NPC I'm
            // talking to") evaluate false here but true when the choice executes — hiding
            // valid choices (e.g. "Leave my party") from the UI.
            var sagaContext = new SagaDialogueContext(instance, characterRef, query.AvatarId.ToString());
            // Pass the instance so NodeVisited / AvatarVisitCount conditions read the
            // committed dialogue history — the query must filter choices exactly the
            // way the command path will evaluate them.
            var stateProvider = new DirectDialogueStateProvider(_world, query.Avatar, _avatarProgressRepository, query.AvatarId.ToString(), characterRef, instance);
            var engine = new DialogueEngine(stateProvider, sagaContext);

            // Restore state from the transaction log without re-executing actions or re-evaluating conditions.
            // The last DialogueNodeVisited in the current session tells us exactly where the player is.
            var lastVisit = sessionTransactions.LastOrDefault(t => t.Type == SagaTransactionType.DialogueNodeVisited);

            if (lastVisit != null)
            {
                var lastVisitedNodeId = lastVisit.Data[TransactionDataKeys.DialogueNodeId];
                engine.RestoreToNode(dialogueTree, lastVisitedNodeId);
                System.Diagnostics.Debug.WriteLine($"[GetDialogueState] Restored to node: {lastVisitedNodeId}");
            }
            else
            {
                // No visits yet - show the session's start node (battle dialogue
                // triggers author per-moment entry points) or the tree default.
                // RestoreToNode, NOT StartDialogue: this is a read-only query, and
                // StartDialogue executes the first node's actions against the live
                // avatar and stages transactions that are then thrown away — the
                // first command (Select/Advance) is where the entry node actually
                // executes and commits. Failed-condition auto-advance is replicated
                // here read-only, matching NavigateToNode.
                var startNodeOverride = lastStarted.Data.GetValueOrDefault(TransactionDataKeys.NodeId);
                var startNodeId = !string.IsNullOrEmpty(startNodeOverride) ? startNodeOverride : dialogueTree.StartNodeId;

                var startConditionEvaluator = new DialogueConditionEvaluator(stateProvider);
                var displayNode = dialogueTree.Node?.FirstOrDefault(n => n.NodeId == startNodeId);
                while (displayNode != null &&
                       displayNode.Condition is { Length: > 0 } &&
                       !startConditionEvaluator.EvaluateAll(displayNode.Condition, displayNode.ConditionLogic))
                {
                    displayNode = !string.IsNullOrEmpty(displayNode.NextNodeId)
                        ? dialogueTree.Node?.FirstOrDefault(n => n.NodeId == displayNode.NextNodeId)
                        : null;
                }

                if (displayNode == null)
                {
                    System.Diagnostics.Debug.WriteLine("[GetDialogueState] No prior visits and no displayable start node");
                    return new DialogueStateResult { IsActive = false, HasEnded = true };
                }

                engine.RestoreToNode(dialogueTree, displayNode.NodeId);
                System.Diagnostics.Debug.WriteLine($"[GetDialogueState] No prior visits; showing start node: {displayNode.NodeId}");
            }

            // Get current node state
            var currentNode = engine.CurrentNode;
            if (currentNode == null)
            {
                System.Diagnostics.Debug.WriteLine("[GetDialogueState] No current node");
                return new DialogueStateResult { IsActive = false, HasEnded = true };
            }

            System.Diagnostics.Debug.WriteLine($"[GetDialogueState] Current node: {currentNode.NodeId}");

            // Build dialogue text
            var dialogueText = new List<string>();
            if (currentNode.Text != null)
            {
                dialogueText.AddRange(currentNode.Text);
            }

            // Build available choices (filtered by target node conditions)
            var choices = new List<DialogueChoiceOption>();
            if (currentNode.Choice != null)
            {
                var avatarCredits = stateProvider.GetCredits();
                var conditionEvaluator = new DialogueConditionEvaluator(stateProvider);

                foreach (var choice in currentNode.Choice)
                {
                    // Check if target node's conditions pass - if not, skip this choice entirely
                    var targetNode = dialogueTree.Node?.FirstOrDefault(n => n.NodeId == choice.NextNodeId);
                    if (targetNode != null && targetNode.Condition != null && targetNode.Condition.Length > 0)
                    {
                        var targetConditionsPass = conditionEvaluator.EvaluateAll(
                            targetNode.Condition,
                            targetNode.ConditionLogic);

                        if (!targetConditionsPass)
                        {
                            System.Diagnostics.Debug.WriteLine($"[GetDialogueState] Filtering out choice '{choice.Text}' -> {choice.NextNodeId} (conditions not met)");
                            continue; // Skip this choice - target node conditions not met
                        }
                    }

                    // Check if choice is available (can afford cost if specified)
                    var hasCost = choice.CostSpecified && choice.Cost > 0;
                    var isAvailable = !hasCost || choice.Cost <= avatarCredits;

                    choices.Add(new DialogueChoiceOption
                    {
                        ChoiceId = choice.NextNodeId ?? string.Empty,
                        Text = choice.Text ?? string.Empty,
                        IsAvailable = isAvailable,
                        BlockedReason = isAvailable ? null : $"Requires {choice.Cost} credits",
                        Cost = hasCost ? choice.Cost : 0
                    });
                }
            }

            // Determine dialogue state
            var hasChoices = currentNode.Choice != null && currentNode.Choice.Length > 0;
            var hasNextNode = !string.IsNullOrEmpty(currentNode.NextNodeId);
            var isTerminalNode = !hasChoices && !hasNextNode;

            System.Diagnostics.Debug.WriteLine($"[GetDialogueState] Node has {dialogueText.Count} text lines and {choices.Count} choices, hasNextNode={hasNextNode}, isTerminal={isTerminalNode}");

            return new DialogueStateResult
            {
                IsActive = !sessionEnded,
                CurrentNodeId = currentNode.NodeId ?? string.Empty,
                DialogueText = dialogueText,
                Choices = choices,
                CanContinue = !sessionEnded && !hasChoices && hasNextNode,
                HasEnded = sessionEnded || isTerminalNode
            };
        }
        catch (Exception ex)
        {
            // Degrading to an inactive conversation masks content bugs (a malformed condition
            // once killed a shipped dialogue tree here) — keep the full exception in the log.
            System.Diagnostics.Debug.WriteLine($"[GetDialogueState] ERROR: {ex}");
            return new DialogueStateResult { IsActive = false, HasEnded = false };
        }
    }
}
