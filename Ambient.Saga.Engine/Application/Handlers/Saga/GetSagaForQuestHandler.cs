using Ambient.Domain;
using Ambient.Domain.DefinitionExtensions;
using Ambient.Saga.Engine.Application.Queries.Saga;
using MediatR;

namespace Ambient.Saga.Engine.Application.Handlers.Saga;

/// <summary>
/// Handler for GetSagaForQuestQuery.
/// Searches through all sagas to find which one contains the specified quest.
/// </summary>
internal sealed class GetSagaForQuestHandler : IRequestHandler<GetSagaForQuestQuery, string?>
{
    private readonly IMediator _mediator;
    private readonly IWorld _world;

    public GetSagaForQuestHandler(IMediator mediator, IWorld world)
    {
        _mediator = mediator;
        _world = world;
    }

    public async Task<string?> Handle(GetSagaForQuestQuery query, CancellationToken ct)
    {
        // Query all sagas to find which one has this quest
        foreach (var saga in _world.Gameplay?.SagaArcs ?? Array.Empty<SagaArc>())
        {
            try
            {
                var sagaState = await _mediator.Send(new GetSagaStateQuery
                {
                    AvatarId = query.AvatarId,
                    SagaRef = saga.RefName
                }, ct);

                // Check if quest is in active or completed quests
                if (sagaState?.ActiveQuests.ContainsKey(query.QuestRef) == true ||
                    sagaState?.CompletedQuests.Contains(query.QuestRef) == true)
                {
                    return saga.RefName;
                }
            }
            catch
            {
                // Skip this saga if there's an error
                continue;
            }
        }

        // Quest not found in any saga
        return null;
    }
}
