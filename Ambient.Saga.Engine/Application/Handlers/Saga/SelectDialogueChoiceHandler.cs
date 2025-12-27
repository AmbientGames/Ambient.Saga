using Ambient.Domain.Contracts;
using Ambient.Saga.Engine.Application.Commands.Saga;
using Ambient.Saga.Engine.Application.ReadModels;
using Ambient.Saga.Engine.Application.Results.Saga;
using Ambient.Saga.Engine.Contracts.Cqrs;
using Ambient.Saga.Engine.Domain.Rpg.Dialogue;
using Ambient.Saga.Engine.Domain.Rpg.Sagas.TransactionLog;
using MediatR;

namespace Ambient.Saga.Engine.Application.Handlers.Saga;

/// <summary>
/// Handler for SelectDialogueChoiceCommand.
/// Uses DialogueEngine to process choice, which creates DialogueNodeVisited and action transactions.
/// </summary>
internal sealed class SelectDialogueChoiceHandler : IRequestHandler<SelectDialogueChoiceCommand, SagaCommandResult>
{
    private readonly ISagaInstanceRepository _instanceRepository;
    private readonly ISagaReadModelRepository _readModelRepository;
    private readonly IWorld _world;

    public SelectDialogueChoiceHandler(
        ISagaInstanceRepository instanceRepository,
        ISagaReadModelRepository readModelRepository,
        IWorld world)
    {
        _instanceRepository = instanceRepository;
        _readModelRepository = readModelRepository;
        _world = world;
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
            var stateProvider = new DirectDialogueStateProvider(_world, command.Avatar);
            var engine = new DialogueEngine(stateProvider, sagaContext);

            // Start dialogue if not already started (idempotent)
            engine.StartDialogue(dialogueTree);

            // Navigate to current node by replaying DialogueNodeVisited transactions
            var visitedNodes = instance.Transactions
                .Where(t => t.Type == SagaTransactionType.DialogueNodeVisited &&
                           t.Data.TryGetValue("CharacterRef", out var charRef) &&
                           charRef == characterState.CharacterRef)
                .OrderBy(t => t.SequenceNumber)
                .ToList();

            System.Diagnostics.Debug.WriteLine($"[SelectDialogueChoice] Replaying {visitedNodes.Count} visited nodes to restore state");

            foreach (var visitedTx in visitedNodes)
            {
                if (!visitedTx.Data.TryGetValue("DialogueNodeId", out var nodeId))
                    continue;

                System.Diagnostics.Debug.WriteLine($"[SelectDialogueChoice] Navigating to previously visited node: {nodeId}");

                // Find the choice that led to this node
                if (engine.CurrentNode != null)
                {
                    var navChoice = engine.CurrentNode.Choice?.FirstOrDefault(c => c.NextNodeId == nodeId);
                    if (navChoice != null)
                    {
                        engine.SelectChoice(navChoice);
                    }
                }
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
            var pendingEvents = engine.PendingEvents.ToList();
            if (pendingEvents.Count > 0)
            {
                System.Diagnostics.Debug.WriteLine($"[SelectDialogueChoice] {pendingEvents.Count} pending events:");
                foreach (var evt in pendingEvents)
                {
                    System.Diagnostics.Debug.WriteLine($"  - {evt.GetType().Name}");
                }
            }

            // Get newly created transactions
            var newTransactions = instance.Transactions.Skip(transactionsBefore).ToList();

            System.Diagnostics.Debug.WriteLine($"[SelectDialogueChoice] Created {newTransactions.Count} transactions");
            foreach (var tx in newTransactions)
            {
                System.Diagnostics.Debug.WriteLine($"  - {tx.Type}: {string.Join(", ", tx.Data.Select(kvp => $"{kvp.Key}={kvp.Value}"))}");
            }

            if (newTransactions.Count == 0)
            {
                return SagaCommandResult.Success(instance.InstanceId, new List<Guid>(), instance.Transactions.Count);
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

            // Add pending events to result data so caller can process them
            var resultData = new Dictionary<string, object>();
            if (pendingEvents.Count > 0)
            {
                resultData["PendingEvents"] = pendingEvents;
            }

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
