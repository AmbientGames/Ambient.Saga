using Ambient.Domain;
using Ambient.SagaEngine.Domain.Rpg.Dialogue.Evaluation;
using Ambient.SagaEngine.Domain.Rpg.Dialogue.Events;
using Ambient.SagaEngine.Domain.Rpg.Dialogue.Execution;

namespace Ambient.SagaEngine.Domain.Rpg.Dialogue;

/// <summary>
/// Main orchestrator for dialogue system.
/// Manages dialogue tree traversal, condition evaluation, action execution, and choice presentation.
/// </summary>
public class DialogueEngine
{
    private readonly IDialogueStateProvider _stateProvider;
    private readonly DialogueConditionEvaluator _conditionEvaluator;
    private readonly DialogueActionExecutor _actionExecutor;
    private readonly SagaDialogueContext? _sagaContext;

    private DialogueTree? _currentTree;
    private DialogueNode? _currentNode;

    /// <summary>
    /// Creates a dialogue engine.
    /// </summary>
    /// <param name="stateProvider">Provider for reading/modifying avatar and world state</param>
    /// <param name="sagaContext">Optional Saga context for transaction creation. If null, no transactions are created.</param>
    public DialogueEngine(IDialogueStateProvider stateProvider, SagaDialogueContext? sagaContext = null)
    {
        _stateProvider = stateProvider ?? throw new ArgumentNullException(nameof(stateProvider));
        _sagaContext = sagaContext;
        _conditionEvaluator = new DialogueConditionEvaluator(stateProvider);
        _actionExecutor = new DialogueActionExecutor(stateProvider, sagaContext); // Pass Saga context to action executor
    }

    /// <summary>
    /// Currently active dialogue tree.
    /// </summary>
    public DialogueTree? CurrentTree => _currentTree;

    /// <summary>
    /// Currently active dialogue node.
    /// </summary>
    public DialogueNode? CurrentNode => _currentNode;

    /// <summary>
    /// Events raised by the most recent action execution.
    /// </summary>
    public IReadOnlyList<DialogueSystemEvent> PendingEvents => _actionExecutor.RaisedEvents;

    /// <summary>
    /// Starts a new dialogue from the specified tree.
    /// </summary>
    /// <returns>The first valid node, or null if dialogue cannot start</returns>
    public DialogueNode? StartDialogue(DialogueTree tree)
    {
        if (tree == null)
            throw new ArgumentNullException(nameof(tree));

        _currentTree = tree;
        _actionExecutor.ClearEvents();

        // Create DialogueStarted transaction if Saga context provided
        if (_sagaContext != null)
        {
            var transaction = DialogueTransactionHelper.CreateDialogueStartedTransaction(
                _sagaContext.AvatarId,
                _sagaContext.CharacterRef,
                tree.RefName,
                _sagaContext.SagaInstance.InstanceId
            );
            _sagaContext.SagaInstance.AddTransaction(transaction);
        }

        // Record visit
        _stateProvider.RecordNodeVisit(tree.RefName, tree.StartNodeId);

        // Navigate to start node
        return NavigateToNode(tree.StartNodeId);
    }

    /// <summary>
    /// Selects a player choice and navigates to the target node.
    /// </summary>
    /// <param name="choice">The choice selected by the player</param>
    /// <returns>The next node, or null if dialogue ends</returns>
    public DialogueNode? SelectChoice(DialogueChoice choice)
    {
        if (_currentTree == null)
            throw new InvalidOperationException("No dialogue is active");

        if (choice == null)
            throw new ArgumentNullException(nameof(choice));

        // Deduct cost if present
        if (choice.Cost > 0)
        {
            if (_stateProvider.GetCredits() < choice.Cost)
                throw new InvalidOperationException($"Insufficient credits for choice (requires {choice.Cost})");

            _stateProvider.TransferCurrency(-choice.Cost);
        }

        _actionExecutor.ClearEvents();

        // Record visit to target node
        _stateProvider.RecordNodeVisit(_currentTree.RefName, choice.NextNodeId);

        return NavigateToNode(choice.NextNodeId);
    }

    /// <summary>
    /// Auto-advances to the next node (when current node has no choices).
    /// </summary>
    /// <returns>The next node, or null if dialogue ends</returns>
    public DialogueNode? AdvanceDialogue()
    {
        if (_currentTree == null || _currentNode == null)
            throw new InvalidOperationException("No dialogue is active");

        if (_currentNode.Choice != null && _currentNode.Choice.Length > 0)
            throw new InvalidOperationException("Cannot auto-advance when choices are present");

        if (string.IsNullOrEmpty(_currentNode.NextNodeId))
            return null; // Dialogue ends

        _actionExecutor.ClearEvents();

        // Record visit to next node
        _stateProvider.RecordNodeVisit(_currentTree.RefName, _currentNode.NextNodeId);

        return NavigateToNode(_currentNode.NextNodeId);
    }

    /// <summary>
    /// Gets valid choices for the current node (filters by cost affordability).
    /// </summary>
    public DialogueChoice[] GetValidChoices()
    {
        if (_currentNode == null || _currentNode.Choice == null)
            return Array.Empty<DialogueChoice>();

        var playerCredits = _stateProvider.GetCredits();

        return _currentNode.Choice
            .Where(c => c.Cost <= playerCredits)
            .ToArray();
    }

    /// <summary>
    /// Ends the current dialogue session.
    /// </summary>
    public void EndDialogue()
    {
        // Create DialogueCompleted transaction if dialogue was active
        if (_sagaContext != null && _currentTree != null)
        {
            var transaction = DialogueTransactionHelper.CreateDialogueCompletedTransaction(
                _sagaContext.AvatarId,
                _sagaContext.CharacterRef,
                _currentTree.RefName,
                _sagaContext.SagaInstance.InstanceId
            );
            _sagaContext.SagaInstance.AddTransaction(transaction);
        }

        _currentTree = null;
        _currentNode = null;
        _actionExecutor.ClearEvents();
    }

    private DialogueNode? NavigateToNode(string nodeId)
    {
        if (_currentTree == null)
            return null;

        var targetNode = FindNode(_currentTree, nodeId);
        if (targetNode == null)
            throw new InvalidOperationException($"Node not found: {nodeId}");

        // Evaluate conditions
        var conditionsPassed = _conditionEvaluator.EvaluateAll(
            targetNode.Condition ?? Array.Empty<DialogueCondition>(),
            targetNode.ConditionLogic
        );

        if (!conditionsPassed)
        {
            // Conditions failed - try to auto-advance or end dialogue
            if (!string.IsNullOrEmpty(targetNode.NextNodeId))
            {
                _stateProvider.RecordNodeVisit(_currentTree.RefName, targetNode.NextNodeId);
                return NavigateToNode(targetNode.NextNodeId);
            }
            return null; // Dialogue ends
        }

        // Conditions passed - make this the current node
        _currentNode = targetNode;

        // Create DialogueNodeVisited transaction BEFORE executing actions
        // This records the INTENT to award items/traits/tokens
        // The Saga state machine will ensure rewards are only given on first visit
        if (_sagaContext != null && _currentTree != null)
        {
            var transaction = DialogueTransactionHelper.CreateDialogueNodeVisitedTransaction(
                _sagaContext.AvatarId,
                _sagaContext.CharacterRef,
                _currentTree.RefName,
                nodeId,
                targetNode,
                _sagaContext.SagaInstance.InstanceId
            );
            _sagaContext.SagaInstance.AddTransaction(transaction);
        }

        // Execute actions
        // Pass character ref for idempotency checking (use empty string if no Saga context)
        var characterRef = _sagaContext?.CharacterRef ?? string.Empty;
        _actionExecutor.ExecuteAll(
            targetNode.Action ?? Array.Empty<DialogueAction>(),
            _currentTree.RefName,
            nodeId,
            characterRef
        );

        return _currentNode;
    }

    private DialogueNode? FindNode(DialogueTree tree, string nodeId)
    {
        return tree.Node?.FirstOrDefault(n => n.NodeId == nodeId);
    }
}
