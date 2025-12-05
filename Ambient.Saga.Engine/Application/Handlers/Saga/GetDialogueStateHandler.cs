using Ambient.Domain.DefinitionExtensions;
using Ambient.Saga.Engine.Application.Queries.Saga;
using Ambient.Saga.Engine.Application.Results.Saga;
using Ambient.Saga.Engine.Contracts.Cqrs;
using Ambient.Saga.Engine.Domain.Rpg.Dialogue;
using Ambient.Saga.Engine.Domain.Rpg.Sagas.TransactionLog;
using MediatR;

namespace Ambient.Saga.Engine.Application.Handlers.Saga;

/// <summary>
/// Handler for GetDialogueStateQuery.
/// Replays dialogue transactions to determine current node, text, and available choices.
/// </summary>
internal sealed class GetDialogueStateHandler : IRequestHandler<GetDialogueStateQuery, DialogueStateResult>
{
    private readonly ISagaInstanceRepository _instanceRepository;
    private readonly World _world;

    public GetDialogueStateHandler(
        ISagaInstanceRepository instanceRepository,
        World world)
    {
        _instanceRepository = instanceRepository;
        _world = world;
    }

    public async Task<DialogueStateResult> Handle(GetDialogueStateQuery query, CancellationToken ct)
    {
        System.Diagnostics.Debug.WriteLine($"[GetDialogueState] Querying dialogue state for character {query.CharacterInstanceId}");

        try
        {
            // Get Saga template
            if (!_world.SagaArcLookup.TryGetValue(query.SagaRef, out var sagaTemplate))
            {
                return new DialogueStateResult { IsActive = false, HasEnded = false };
            }

            // Get Saga instance
            var instance = await _instanceRepository.GetOrCreateInstanceAsync(query.AvatarId, query.SagaRef, ct);

            // Replay state to get character ref from instance ID
            if (!_world.SagaTriggersLookup.TryGetValue(query.SagaRef, out var expandedTriggers))
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
                .Where(t => t.Data.TryGetValue("CharacterRef", out var charRef) &&
                           charRef == characterRef)
                .OrderBy(t => t.SequenceNumber)
                .ToList();

            System.Diagnostics.Debug.WriteLine($"[GetDialogueState] Found {dialogueTransactions.Count} dialogue transactions");

            // Check if dialogue is active
            var dialogueStarted = dialogueTransactions.FirstOrDefault(t => t.Type == SagaTransactionType.DialogueStarted);
            var dialogueCompleted = dialogueTransactions.FirstOrDefault(t => t.Type == SagaTransactionType.DialogueCompleted);

            if (dialogueStarted == null)
            {
                System.Diagnostics.Debug.WriteLine("[GetDialogueState] No active dialogue");
                return new DialogueStateResult { IsActive = false, HasEnded = false };
            }

            if (dialogueCompleted != null)
            {
                System.Diagnostics.Debug.WriteLine("[GetDialogueState] Dialogue has ended");
                return new DialogueStateResult { IsActive = false, HasEnded = true };
            }

            // Get dialogue tree from DialogueStarted transaction
            var dialogueTreeRef = dialogueStarted.Data["DialogueTreeRef"];
            var dialogueTree = _world.Gameplay.DialogueTrees?.FirstOrDefault(dt => dt.RefName == dialogueTreeRef);
            if (dialogueTree == null)
            {
                System.Diagnostics.Debug.WriteLine($"[GetDialogueState] ERROR: Dialogue tree '{dialogueTreeRef}' not found");
                return new DialogueStateResult { IsActive = false, HasEnded = false };
            }

            // Create dialogue engine with Saga context (characterRef was already extracted earlier)
            var sagaContext = new SagaDialogueContext(instance, characterRef, query.AvatarId.ToString());
            var stateProvider = new DirectDialogueStateProvider(_world, query.Avatar);
            var engine = new DialogueEngine(stateProvider, sagaContext);

            // Start dialogue (idempotent)
            engine.StartDialogue(dialogueTree);

            // Navigate to current node by replaying DialogueNodeVisited transactions
            var visitedNodes = dialogueTransactions
                .Where(t => t.Type == SagaTransactionType.DialogueNodeVisited)
                .OrderBy(t => t.SequenceNumber)
                .ToList();

            System.Diagnostics.Debug.WriteLine($"[GetDialogueState] Replaying {visitedNodes.Count} visited nodes");

            foreach (var visitedTx in visitedNodes)
            {
                var nodeId = visitedTx.Data["DialogueNodeId"];
                System.Diagnostics.Debug.WriteLine($"[GetDialogueState] Navigating to node: {nodeId}");

                // Find the choice that led to this node
                if (engine.CurrentNode != null)
                {
                    var choice = engine.CurrentNode.Choice?.FirstOrDefault(c => c.NextNodeId == nodeId);
                    if (choice != null)
                    {
                        engine.SelectChoice(choice);
                    }
                }
            }

            // Get current node state
            var currentNode = engine.CurrentNode;
            if (currentNode == null)
            {
                System.Diagnostics.Debug.WriteLine("[GetDialogueState] No current node (dialogue may have ended)");
                return new DialogueStateResult
                {
                    IsActive = false,
                    HasEnded = true
                };
            }

            System.Diagnostics.Debug.WriteLine($"[GetDialogueState] Current node: {currentNode.NodeId}");

            // Build dialogue text
            var dialogueText = new List<string>();
            if (currentNode.Text != null)
            {
                dialogueText.AddRange(currentNode.Text);
            }

            // Build available choices
            var choices = new List<DialogueChoiceOption>();
            if (currentNode.Choice != null)
            {
                var playerCredits = stateProvider.GetCredits();

                foreach (var choice in currentNode.Choice)
                {
                    // Check if choice is available (can afford cost if specified)
                    var hasCost = choice.CostSpecified && choice.Cost > 0;
                    var isAvailable = !hasCost || choice.Cost <= playerCredits;

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

            System.Diagnostics.Debug.WriteLine($"[GetDialogueState] Node has {dialogueText.Count} text lines and {choices.Count} choices");

            return new DialogueStateResult
            {
                IsActive = true,
                CurrentNodeId = currentNode.NodeId ?? string.Empty,
                DialogueText = dialogueText,
                Choices = choices,
                CanContinue = currentNode.Choice == null || currentNode.Choice.Length == 0,
                HasEnded = false
            };
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[GetDialogueState] ERROR: {ex.Message}");
            return new DialogueStateResult { IsActive = false, HasEnded = false };
        }
    }
}
