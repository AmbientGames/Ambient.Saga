using MediatR;
using Ambient.Saga.Engine.Contracts.Cqrs;
using Ambient.Saga.Engine.Domain.Rpg.Sagas;
using Ambient.Saga.Engine.Application.Queries.Saga;
using Ambient.Domain.Contracts;

namespace Ambient.Saga.Engine.Application.Handlers.Saga;

/// <summary>
/// Handler for GetTriggersInRangeQuery.
/// Returns all triggers at a position with proximity info.
/// </summary>
internal sealed class GetTriggersInRangeHandler : IRequestHandler<GetTriggersInRangeQuery, List<SagaTriggerProximityInfo>>
{
    private readonly ISagaInstanceRepository _instanceRepository;
    private readonly IWorld _world;

    public GetTriggersInRangeHandler(
        ISagaInstanceRepository instanceRepository,
        IWorld world)
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
