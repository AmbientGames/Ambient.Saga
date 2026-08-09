using Ambient.Domain.Contracts;
using Ambient.Rpg.Engine.Application.Queries.Arcs;
using Ambient.Rpg.Engine.Contracts;
using Ambient.Rpg.Engine.Contracts.Persistence;
using Ambient.Rpg.Engine.Domain.Services;
using MediatR;

namespace Ambient.Rpg.Engine.Application.Handlers.Arcs;

/// <summary>
/// Handler for QueryInteractionsAtPositionQuery.
/// Wraps ArcProximityService.QueryAllInteractionsAtPositionAsync in CQRS pattern.
///
/// This allows views to query interactions at arbitrary positions (map clicks, hover, etc.)
/// through the MediatR pipeline instead of calling domain services directly.
/// </summary>
internal sealed class QueryInteractionsAtPositionHandler : IRequestHandler<QueryInteractionsAtPositionQuery, List<ArcInteraction>>
{
    private readonly IWorld _world;
    private readonly IWorldStateRepository _worldRepository;
    private readonly IAvatarProgressRepository _avatarProgressRepository;

    public QueryInteractionsAtPositionHandler(
        IWorld world,
        IWorldStateRepository worldRepository,
        IAvatarProgressRepository avatarProgressRepository)
    {
        _world = world;
        _worldRepository = worldRepository;
        _avatarProgressRepository = avatarProgressRepository;
    }

    public async Task<List<ArcInteraction>> Handle(QueryInteractionsAtPositionQuery query, CancellationToken ct)
    {
        if (_world == null)
        {
            return new List<ArcInteraction>();
        }

        // Delegate to domain service
        var interactions = await ArcProximityService.QueryAllInteractionsAtPositionAsync(
            query.ModelX,
            query.ModelZ,
            query.Avatar,
            _world,
            _worldRepository,
            _avatarProgressRepository);

        return interactions;
    }
}
