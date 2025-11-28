using Ambient.Domain;
using Ambient.Domain.DefinitionExtensions;
using Ambient.SagaEngine.Application.Queries.Saga;
using Ambient.SagaEngine.Application.ReadModels;
using Ambient.SagaEngine.Contracts.Cqrs;
using Ambient.SagaEngine.Domain.Rpg.Sagas.TransactionLog;
using MediatR;

namespace Ambient.SagaEngine.Application.Handlers.Saga;

/// <summary>
/// Handler for GetDialogueOptionsQuery.
/// Returns available dialogue options based on current dialogue state and character traits.
/// </summary>
internal sealed class GetDialogueOptionsHandler : IRequestHandler<GetDialogueOptionsQuery, List<DialogueOptionDto>>
{
    private readonly ISagaInstanceRepository _instanceRepository;
    private readonly ISagaReadModelRepository _readModelRepository;
    private readonly World _world;

    public GetDialogueOptionsHandler(
        ISagaInstanceRepository instanceRepository,
        ISagaReadModelRepository readModelRepository,
        World world)
    {
        _instanceRepository = instanceRepository;
        _readModelRepository = readModelRepository;
        _world = world;
    }

    public async Task<List<DialogueOptionDto>> Handle(GetDialogueOptionsQuery query, CancellationToken ct)
    {
        try
        {
            // Get Saga template and triggers
            if (!_world.SagaArcLookup.TryGetValue(query.SagaRef, out var sagaTemplate))
            {
                return new List<DialogueOptionDto>();
            }

            if (!_world.SagaTriggersLookup.TryGetValue(query.SagaRef, out var expandedTriggers))
            {
                return new List<DialogueOptionDto>();
            }

            // Get Saga instance
            var instance = await _instanceRepository.GetOrCreateInstanceAsync(query.AvatarId, query.SagaRef, ct);

            // Try to get cached state
            var cachedSequence = await _readModelRepository.GetCachedSequenceNumberAsync(query.AvatarId, query.SagaRef, ct);
            var currentSequence = instance.GetCommittedTransactions().LastOrDefault()?.SequenceNumber ?? 0;

            SagaState state;
            if (cachedSequence == currentSequence && cachedSequence > 0)
            {
                var cachedState = await _readModelRepository.GetCachedStateAsync(query.AvatarId, query.SagaRef, ct);
                state = cachedState ?? ReplayState(sagaTemplate, expandedTriggers, instance);
            }
            else
            {
                state = ReplayState(sagaTemplate, expandedTriggers, instance);
                if (currentSequence > 0)
                {
                    await _readModelRepository.UpdateCachedStateAsync(query.AvatarId, query.SagaRef, state, currentSequence, ct);
                }
            }

            // Get dialogue history for this character
            var dialogueHistory = instance.GetCommittedTransactions()
                .Where(t => (t.Type == SagaTransactionType.DialogueStarted || t.Type == SagaTransactionType.DialogueNodeVisited) &&
                           t.Data.ContainsKey("CharacterRef") &&
                           t.Data["CharacterRef"] == query.CharacterRef &&
                           t.Data.ContainsKey("DialogueTreeRef") &&
                           t.Data["DialogueTreeRef"] == query.DialogueTreeRef)
                .OrderBy(t => t.SequenceNumber)
                .ToList();

            // Get current dialogue node
            var currentNodeId = query.CurrentNodeId ?? "start"; // Default starting node
            if (dialogueHistory.Any())
            {
                var lastVisit = dialogueHistory.Last();
                if (lastVisit.Data.TryGetValue("NodeId", out var nodeId))
                {
                    currentNodeId = nodeId;
                }
            }

            // Get visited nodes
            var visitedNodes = dialogueHistory
                .Where(t => t.Data.ContainsKey("NodeId"))
                .Select(t => t.Data["NodeId"])
                .ToHashSet();

            // For now, return placeholder options based on node ID
            // In a real implementation, this would query the dialogue tree from World
            var options = new List<DialogueOptionDto>
            {
                new DialogueOptionDto
                {
                    NodeId = "node_1",
                    DisplayText = $"Continue conversation from node: {currentNodeId}",
                    HasBeenVisited = visitedNodes.Contains("node_1"),
                    IsAvailable = true
                },
                new DialogueOptionDto
                {
                    NodeId = "node_2",
                    DisplayText = "Ask about their background",
                    HasBeenVisited = visitedNodes.Contains("node_2"),
                    IsAvailable = true
                },
                new DialogueOptionDto
                {
                    NodeId = "node_3",
                    DisplayText = "Discuss current situation",
                    HasBeenVisited = visitedNodes.Contains("node_3"),
                    IsAvailable = true
                },
                new DialogueOptionDto
                {
                    NodeId = "end",
                    DisplayText = "End conversation",
                    HasBeenVisited = false,
                    IsAvailable = true
                }
            };

            return options;
        }
        catch (Exception)
        {
            return new List<DialogueOptionDto>();
        }
    }

    private SagaState ReplayState(
        SagaArc sagaTemplate,
        List<SagaTrigger> expandedTriggers,
        SagaInstance instance)
    {
        var stateMachine = new SagaStateMachine(sagaTemplate, expandedTriggers, _world);
        return stateMachine.ReplayToNow(instance);
    }
}
