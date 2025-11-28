using Ambient.Domain.DefinitionExtensions;
using Ambient.SagaEngine.Application.Queries.Saga;
using Ambient.SagaEngine.Contracts;
using Ambient.SagaEngine.Domain.Services;
using MediatR;

namespace Ambient.SagaEngine.Application.Handlers.Saga;

/// <summary>
/// Handler for QueryInteractionsAtPositionQuery.
/// Wraps SagaProximityService.QueryAllInteractionsAtPosition in CQRS pattern.
///
/// This allows views to query interactions at arbitrary positions (map clicks, hover, etc.)
/// through the MediatR pipeline instead of calling domain services directly.
/// </summary>
internal sealed class QueryInteractionsAtPositionHandler : IRequestHandler<QueryInteractionsAtPositionQuery, List<SagaInteraction>>
{
    private readonly World? _world;
    private readonly IWorldStateRepository? _worldRepository;

    public QueryInteractionsAtPositionHandler(
        World? world,
        IWorldStateRepository? worldRepository)
    {
        _world = world;
        _worldRepository = worldRepository;
    }

    public Task<List<SagaInteraction>> Handle(QueryInteractionsAtPositionQuery query, CancellationToken ct)
    {
        if (_world == null)
        {
            return Task.FromResult(new List<SagaInteraction>());
        }

        // Delegate to domain service
        var interactions = SagaProximityService.QueryAllInteractionsAtPosition(
            query.ModelX,
            query.ModelZ,
            query.Avatar,
            _world,
            _worldRepository);

        return Task.FromResult(interactions);
    }
}
