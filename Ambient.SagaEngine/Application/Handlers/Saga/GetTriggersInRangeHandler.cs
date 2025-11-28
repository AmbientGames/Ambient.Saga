using Ambient.Domain.DefinitionExtensions;
using MediatR;
using Ambient.SagaEngine.Contracts.Cqrs;
using Ambient.SagaEngine.Domain.Rpg.Sagas;
using Ambient.SagaEngine.Application.Queries.Saga;

namespace Ambient.SagaEngine.Application.Handlers.Saga;

/// <summary>
/// Handler for GetTriggersInRangeQuery.
/// Returns all triggers at a position with proximity info.
/// </summary>
internal sealed class GetTriggersInRangeHandler : IRequestHandler<GetTriggersInRangeQuery, List<SagaTriggerProximityInfo>>
{
    private readonly ISagaInstanceRepository _instanceRepository;
    private readonly World _world;

    public GetTriggersInRangeHandler(
        ISagaInstanceRepository instanceRepository,
        World world)
    {
        _instanceRepository = instanceRepository;
        _world = world;
    }

    public async Task<List<SagaTriggerProximityInfo>> Handle(GetTriggersInRangeQuery query, CancellationToken ct)
    {
        try
        {
            // Get Saga template and triggers
            if (!_world.SagaArcLookup.TryGetValue(query.SagaRef, out var sagaTemplate))
            {
                return new List<SagaTriggerProximityInfo>();
            }

            if (!_world.SagaTriggersLookup.TryGetValue(query.SagaRef, out var expandedTriggers))
            {
                return new List<SagaTriggerProximityInfo>();
            }

            // Get Saga instance
            var instance = await _instanceRepository.GetOrCreateInstanceAsync(query.AvatarId, query.SagaRef, ct);

            // Use domain service to get triggers in range
            var service = new SagaInteractionService(sagaTemplate, expandedTriggers, _world);
            var triggersInRange = service.GetTriggersAtPosition(instance, query.AvatarX, query.AvatarZ);

            return triggersInRange;
        }
        catch (Exception)
        {
            return new List<SagaTriggerProximityInfo>();
        }
    }
}
