using Ambient.Domain;
using Ambient.Rpg.Engine.Domain.Dialogue.Evaluation;
using Ambient.Rpg.Engine.Domain.Dialogue.Events;
using Ambient.Rpg.Engine.Domain.Dialogue.Execution;

namespace Ambient.Rpg.Engine.Domain.Dialogue;

/// <summary>
/// Main orchestrator for dialogue system.
/// Manages dialogue tree traversal, condition evaluation, action execution, and choice presentation.
/// </summary>
public class DialogueEngine
{
    private readonly IDialogueStateProvider _stateProvider;
    private readonly DialogueConditionEvaluator _conditionEvaluator;
    private readonly DialogueActionExecutor _actionExecutor;
    private readonly ArcDialogueContext? _arcContext;

    private DialogueTree? _currentTree;
    private DialogueNode? _currentNode;

    /// <summary>
    /// Creates a dialogue engine.
    /// </summary>
    /// <param name="stateProvider">Provider for reading/modifying avatar and world state</param>
    /// <param name="arcContext">Optional Arc context for transaction creation. If null, no transactions are created.</param>
    public DialogueEngine(IDialogueStateProvider stateProvider, ArcDialogueContext? arcContext = null)
    {
        _stateProvider = stateProvider ?? throw new ArgumentNullException(nameof(stateProvider));
        _arcContext = arcContext;
        _conditionEvaluator = new DialogueConditionEvaluator(stateProvider);
        _actionExecutor = new DialogueActionExecutor(stateProvider, arcContext); // Pass Arc context to action executor
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

        // Create DialogueStarted transaction if Arc context provided
        if (_arcContext != null)
        {
            var transaction = DialogueTransactionHelper.CreateDialogueStartedTransaction(
                _arcContext.AvatarId,
                _arcContext.CharacterRef,
                tree.RefName,
                _arcContext.ArcInstance.InstanceId
            );
            _arcContext.ArcInstance.AddTransaction(transaction);
        }

        // Record visit
        _stateProvider.RecordNodeVisit(tree.RefName, tree.StartNodeId);

        // Navigate to start node; if the entry chain dead-ends (all conditions
        // failed with no fallback) seal the session immediately so it never
        // dangles with CanContinue=true (M16)
        return NavigateOrSeal(tree.StartNodeId);
    }

    /// <summary>
    /// Starts a dialogue at a specific node instead of the tree's StartNodeId.
    /// Battle dialogue triggers author per-moment entry points (battle_enraged,
    /// battle_desperate, ...) into their battle trees.
    /// </summary>
    public DialogueNode? StartDialogueAt(DialogueTree tree, string startNodeId)
    {
        if (tree == null)
            throw new ArgumentNullException(nameof(tree));
        if (string.IsNullOrEmpty(startNodeId))
            return StartDialogue(tree);

        _currentTree = tree;
        _actionExecutor.ClearEvents();

        // Create DialogueStarted transaction if Arc context provided — the start
        // node rides on it so a resumed session re-enters at the same place
        if (_arcContext != null)
        {
            var transaction = DialogueTransactionHelper.CreateDialogueStartedTransaction(
                _arcContext.AvatarId,
                _arcContext.CharacterRef,
                tree.RefName,
                _arcContext.ArcInstance.InstanceId
            );
            transaction.Data[TransactionDataKeys.NodeId] = startNodeId;
            _arcContext.ArcInstance.AddTransaction(transaction);
        }

        // Record visit
        _stateProvider.RecordNodeVisit(tree.RefName, startNodeId);

        return NavigateOrSeal(startNodeId);
    }

    /// <summary>
    /// Re-enters an already-started session at its entry node WITHOUT emitting a
    /// new DialogueStarted transaction. The session handlers use this when the log
    /// already holds the DialogueStarted from StartDialogueHandler — going through
    /// StartDialogue/StartDialogueAt here duplicated DialogueStarted in every
    /// session (log noise; session scoping was unaffected because it keys on the
    /// LAST DialogueStarted).
    /// </summary>
    /// <param name="tree">The session's dialogue tree</param>
    /// <param name="startNodeId">Optional entry-node override; null/empty uses the tree's StartNodeId</param>
    public DialogueNode? ResumeDialogue(DialogueTree tree, string? startNodeId = null)
    {
        if (tree == null)
            throw new ArgumentNullException(nameof(tree));

        _currentTree = tree;
        _actionExecutor.ClearEvents();

        var entryNodeId = string.IsNullOrEmpty(startNodeId) ? tree.StartNodeId : startNodeId;
        _stateProvider.RecordNodeVisit(tree.RefName, entryNodeId);

        return NavigateOrSeal(entryNodeId);
    }

    /// <summary>
    /// Selects a avatar choice and navigates to the target node.
    /// </summary>
    /// <param name="choice">The choice selected by the player</param>
    /// <returns>The next node, or null if dialogue ends</returns>
    public DialogueNode? SelectChoice(DialogueChoice choice)
    {
        if (_currentTree == null)
            throw new InvalidOperationException("No dialogue is active");

        if (choice == null)
            throw new ArgumentNullException(nameof(choice));

        _actionExecutor.ClearEvents();

        // Record visit to target node
        _stateProvider.RecordNodeVisit(_currentTree.RefName, choice.NextNodeId);

        // Resolve the effective destination BEFORE charging the choice cost: the
        // target's conditions may fail and fall through its NextNodeId chain, and
        // a player must never pay for a node they didn't reach. The old order
        // (charge, then navigate) also made a dead-ended navigation re-chargeable
        // per click because nothing was committed (M17).
        var destination = ResolveDestination(choice.NextNodeId);

        if (destination == null)
        {
            // Dead end: no charge, and the conversation cannot continue — seal
            // the session so it never dangles with CanContinue=true (M16)
            EndDialogue();
            return null;
        }

        // Deduct cost if specified in XML — only now that navigation is known to succeed
        if (choice.CostSpecified && choice.Cost > 0)
        {
            if (_stateProvider.GetCredits() < choice.Cost)
                throw new InvalidOperationException($"Insufficient credits for choice (requires {choice.Cost})");

            _stateProvider.TransferCurrency(-choice.Cost);
        }

        return EnterNode(destination);
    }

    /// <summary>
    /// Auto-advances to the next node (when current node has no choices).
    /// </summary>
    /// <returns>The next node, or null if dialogue ends (the session is sealed)</returns>
    public DialogueNode? AdvanceDialogue()
    {
        if (_currentTree == null || _currentNode == null)
            throw new InvalidOperationException("No dialogue is active");

        if (_currentNode.Choice != null && _currentNode.Choice.Length > 0)
            throw new InvalidOperationException("Cannot auto-advance when choices are present");

        if (string.IsNullOrEmpty(_currentNode.NextNodeId))
        {
            // Dialogue ends — seal so the session cannot dangle
            EndDialogue();
            return null;
        }

        _actionExecutor.ClearEvents();

        // Record visit to next node
        _stateProvider.RecordNodeVisit(_currentTree.RefName, _currentNode.NextNodeId);

        return NavigateOrSeal(_currentNode.NextNodeId);
    }

    /// <summary>
    /// Gets valid choices for the current node (filters by cost affordability).
    /// </summary>
    public DialogueChoice[] GetValidChoices()
    {
        if (_currentNode == null || _currentNode.Choice == null)
            return Array.Empty<DialogueChoice>();

        var avatarCredits = _stateProvider.GetCredits();

        return _currentNode.Choice
            .Where(c => c.Cost <= avatarCredits)
            .ToArray();
    }

    /// <summary>
    /// Restores engine state to a previously visited node without re-evaluating conditions
    /// or re-executing actions. Used to rehydrate state from the transaction log.
    /// </summary>
    public void RestoreToNode(DialogueTree tree, string nodeId)
    {
        if (tree == null)
            throw new ArgumentNullException(nameof(tree));

        var node = FindNode(tree, nodeId);
        if (node == null)
            throw new InvalidOperationException($"Node not found: {nodeId}");

        _currentTree = tree;
        _currentNode = node;
        _actionExecutor.ClearEvents();
    }

    /// <summary>
    /// True if the current node is terminal (no choices and no NextNodeId).
    /// </summary>
    public bool IsCurrentNodeTerminal
    {
        get
        {
            if (_currentNode == null) return false;
            var hasChoices = _currentNode.Choice != null && _currentNode.Choice.Length > 0;
            var hasNextNode = !string.IsNullOrEmpty(_currentNode.NextNodeId);
            return !hasChoices && !hasNextNode;
        }
    }

    /// <summary>
    /// Ends the current dialogue session.
    /// </summary>
    public void EndDialogue()
    {
        // Create DialogueCompleted transaction if dialogue was active
        if (_arcContext != null && _currentTree != null)
        {
            var transaction = DialogueTransactionHelper.CreateDialogueCompletedTransaction(
                _arcContext.AvatarId,
                _arcContext.CharacterRef,
                _currentTree.RefName,
                _arcContext.ArcInstance.InstanceId
            );
            _arcContext.ArcInstance.AddTransaction(transaction);
        }

        _currentTree = null;
        _currentNode = null;
        _actionExecutor.ClearEvents();
    }

    /// <summary>
    /// Resolves the effective destination of a navigation to <paramref name="startNodeId"/>:
    /// walks the failed-condition fallback chain (each failed node's NextNodeId) until a
    /// node's conditions pass. Returns null when the chain dead-ends or cycles.
    /// Records a session-local visit for every fallback node walked past (the FIRST node's
    /// visit is recorded by the caller, matching the historical order: a node's visit is
    /// recorded before its conditions are evaluated). Read-only otherwise — no actions
    /// run and no transactions are staged.
    /// </summary>
    private DialogueNode? ResolveDestination(string startNodeId)
    {
        if (_currentTree == null)
            return null;

        var nodeId = startNodeId;
        var visited = new HashSet<string>();
        var isFirst = true;

        while (!string.IsNullOrEmpty(nodeId) && visited.Add(nodeId))
        {
            if (!isFirst)
            {
                _stateProvider.RecordNodeVisit(_currentTree.RefName, nodeId);
            }
            isFirst = false;

            var node = FindNode(_currentTree, nodeId);
            if (node == null)
                throw new InvalidOperationException($"Node not found: {nodeId}");

            var conditionsPassed = _conditionEvaluator.EvaluateAll(
                node.Condition ?? Array.Empty<DialogueCondition>(),
                node.ConditionLogic
            );

            if (conditionsPassed)
                return node;

            nodeId = node.NextNodeId;
        }

        // Dead end (no fallback) or a cycle of failing nodes (previously infinite recursion)
        return null;
    }

    /// <summary>
    /// Makes a resolved node current and runs its side effects.
    /// </summary>
    private DialogueNode EnterNode(DialogueNode targetNode)
    {
        _currentNode = targetNode;

        // Execute actions FIRST. Reward transactions are staged inside the executor, not
        // attached to the ArcInstance yet. If any action throws, the executor drops its
        // staged buffer and the visit transaction below is never recorded — so a retry is
        // free to re-attempt without hitting the idempotency block.
        var characterRef = _arcContext?.CharacterRef ?? string.Empty;
        _actionExecutor.ExecuteAll(
            targetNode.Action ?? Array.Empty<DialogueAction>(),
            _currentTree!.RefName,
            targetNode.NodeId,
            characterRef
        );

        // Actions succeeded — commit the visit record and any staged reward transactions
        // to the ArcInstance as a single group. The visit is added first so it receives
        // the lowest sequence number in the group, preserving the historical ordering
        // (visit precedes its rewards in the log).
        if (_arcContext != null)
        {
            var transaction = DialogueTransactionHelper.CreateDialogueNodeVisitedTransaction(
                _arcContext.AvatarId,
                _arcContext.CharacterRef,
                _currentTree.RefName,
                targetNode.NodeId,
                targetNode,
                _arcContext.ArcInstance.InstanceId
            );
            _arcContext.ArcInstance.AddTransaction(transaction);
        }
        _actionExecutor.FlushStaged();

        return targetNode;
    }

    /// <summary>
    /// Resolves and enters a node; when the failed-condition chain dead-ends, seals
    /// the session (DialogueCompleted) instead of leaving it dangling forever with
    /// CanContinue=true (M16 — the dead-end Continue loop).
    /// </summary>
    private DialogueNode? NavigateOrSeal(string nodeId)
    {
        var destination = ResolveDestination(nodeId);
        if (destination == null)
        {
            EndDialogue();
            return null;
        }

        return EnterNode(destination);
    }

    private DialogueNode? FindNode(DialogueTree tree, string nodeId)
    {
        return tree.Node?.FirstOrDefault(n => n.NodeId == nodeId);
    }
}
