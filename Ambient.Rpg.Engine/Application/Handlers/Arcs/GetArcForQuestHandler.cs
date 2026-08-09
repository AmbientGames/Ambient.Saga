using Ambient.Domain;
using Ambient.Domain.Contracts;
using Ambient.Rpg.Engine.Application.Queries.Arcs;
using MediatR;

namespace Ambient.Rpg.Engine.Application.Handlers.Arcs;

/// <summary>
/// Handler for GetArcForQuestQuery.
/// Searches through all arcs to find which one contains the specified quest.
/// </summary>
internal sealed class GetArcForQuestHandler : IRequestHandler<GetArcForQuestQuery, string?>
{
    private readonly IMediator _mediator;
    private readonly IWorld _world;

    public GetArcForQuestHandler(IMediator mediator, IWorld world)
    {
        _mediator = mediator;
        _world = world;
    }

    public async Task<string?> Handle(GetArcForQuestQuery query, CancellationToken ct)
    {
        // Query all arcs to find which one has this quest
        foreach (var arc in _world.Gameplay?.Saga ?? Array.Empty<Arc>())
        {
            try
            {
                var arcState = await _mediator.Send(new GetArcStateQuery
                {
                    AvatarId = query.AvatarId,
                    ArcRef = arc.RefName
                }, ct);

                // Check if quest is in active or completed quests
                if (arcState?.ActiveQuests.ContainsKey(query.QuestRef) == true ||
                    arcState?.CompletedQuests.Contains(query.QuestRef) == true)
                {
                    return arc.RefName;
                }
            }
            catch
            {
                // Skip this arc if there's an error
                continue;
            }
        }

        // Quest not found in any arc
        return null;
    }
}
