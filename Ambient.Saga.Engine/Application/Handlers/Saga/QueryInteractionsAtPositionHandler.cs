using Ambient.Domain.Contracts;
using Ambient.Saga.Engine.Application.Queries.Saga;
using Ambient.Saga.Engine.Contracts;
using Ambient.Saga.Engine.Domain.Services;
using MediatR;

namespace Ambient.Saga.Engine.Application.Handlers.Saga;

/// <summary>
/// Handler for QueryInteractionsAtPositionQuery.
/// Wraps SagaProximityService.QueryAllInteractionsAtPositionAsync in CQRS pattern.
///
/// This allows views to query interactions at arbitrary positions (map clicks, hover, etc.)
/// through the MediatR pipeline instead of calling domain services directly.
/// </summary>
internal sealed class QueryInteractionsAtPositionHandler : IRequestHandler<QueryInteractionsAtPositionQuery, List<SagaInteraction>>
{
    private readonly IWorld _world;
    private readonly IWorldStateRepository _worldRepository;

    public QueryInteractionsAtPositionHandler(
        IWorld world,
        IWorldStateRepository worldRepository)
    {
        _world = world;
        _worldRepository = worldRepository;
    }

    public async Task<List<SagaInteraction>> Handle(QueryInteractionsAtPositionQuery query, CancellationToken ct)
    {
        if (_world == null)
        {
            return new List<SagaInteraction>();
        }

        // Delegate to domain service
        var interactions = await SagaProximityService.QueryAllInteractionsAtPositionAsync(
            query.ModelX,
            query.ModelZ,
            query.Avatar,
            _world,
            _worldRepository);

        return interactions;
    }
}
