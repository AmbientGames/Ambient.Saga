using MediatR;
using Ambient.Rpg.Engine.Contracts.Cqrs;
using Ambient.Rpg.Engine.Domain.Arcs;
using Ambient.Rpg.Engine.Application.Queries.Arcs;
using Ambient.Domain.Contracts;

namespace Ambient.Rpg.Engine.Application.Handlers.Arcs;

/// <summary>
/// Handler for GetTriggersInRangeQuery.
/// Returns all triggers at a position with proximity info.
/// </summary>
internal sealed class GetTriggersInRangeHandler : IRequestHandler<GetTriggersInRangeQuery, List<ArcTriggerProximityInfo>>
{
    private readonly IArcInstanceRepository _instanceRepository;
    private readonly IWorld _world;

    public GetTriggersInRangeHandler(
        IArcInstanceRepository instanceRepository,
        IWorld world)
    {
        _instanceRepository = instanceRepository;
        _world = world;
    }

    public async Task<List<ArcTriggerProximityInfo>> Handle(GetTriggersInRangeQuery query, CancellationToken ct)
    {
        try
        {
            // Get Arc template and triggers
            if (!_world.ArcLookup.TryGetValue(query.ArcRef, out var arcTemplate))
            {
                return new List<ArcTriggerProximityInfo>();
            }

            if (!_world.ArcTriggersLookup.TryGetValue(query.ArcRef, out var expandedTriggers))
            {
                return new List<ArcTriggerProximityInfo>();
            }

            // Get Arc instance
            var instance = await _instanceRepository.GetOrCreateInstanceAsync(query.AvatarId, query.ArcRef, ct);

            // Use domain service to get triggers in range
            var service = new ArcInteractionService(arcTemplate, expandedTriggers, _world);
            var triggersInRange = service.GetTriggersAtPosition(instance, query.AvatarX, query.AvatarZ);

            return triggersInRange;
        }
        catch (Exception)
        {
            return new List<ArcTriggerProximityInfo>();
        }
    }
}
