using Ambient.Domain.Contracts;
using Ambient.Rpg.Engine.Application.Queries.Arcs;
using Ambient.Rpg.Engine.Application.ReadModels;
using Ambient.Rpg.Engine.Contracts.Cqrs;
using Ambient.Rpg.Engine.Domain.Arcs.TransactionLog;
using MediatR;

namespace Ambient.Rpg.Engine.Application.Handlers.Arcs;

/// <summary>
/// Handler for GetArcStateQuery.
/// Returns current Arc state (cached if available, otherwise replays from transactions).
/// </summary>
internal sealed class GetArcStateHandler : IRequestHandler<GetArcStateQuery, ArcState?>
{
    private readonly IArcInstanceRepository _instanceRepository;
    private readonly IArcReadModelRepository _readModelRepository;
    private readonly IWorld _world;

    public GetArcStateHandler(
        IArcInstanceRepository instanceRepository,
        IArcReadModelRepository readModelRepository,
        IWorld world)
    {
        _instanceRepository = instanceRepository;
        _readModelRepository = readModelRepository;
        _world = world;
    }

    public async Task<ArcState?> Handle(GetArcStateQuery query, CancellationToken ct)
    {
        try
        {
            // Get Arc template
            if (!_world.ArcLookup.TryGetValue(query.ArcRef, out var arcTemplate))
            {
                return null;
            }

            // Get expanded triggers
            if (!_world.ArcTriggersLookup.TryGetValue(query.ArcRef, out var expandedTriggers))
            {
                return null;
            }

            // Get Arc instance
            var instance = await _instanceRepository.GetOrCreateInstanceAsync(query.AvatarId, query.ArcRef, ct);

            // Check if cached read model is available and up-to-date
            var cachedSequence = await _readModelRepository.GetCachedSequenceNumberAsync(query.AvatarId, query.ArcRef, ct);
            var currentSequence = instance.GetCommittedTransactions().LastOrDefault()?.SequenceNumber ?? 0;

            if (cachedSequence == currentSequence && cachedSequence > 0)
            {
                // Cache is up-to-date, use it
                var cachedState = await _readModelRepository.GetCachedStateAsync(query.AvatarId, query.ArcRef, ct);
                if (cachedState != null)
                {
                    return cachedState;
                }
            }

            // Cache miss or stale - replay from transactions
            var stateMachine = new ArcStateMachine(arcTemplate, expandedTriggers, _world);
            var state = stateMachine.ReplayToNow(instance);

            // Update cache for next time
            if (currentSequence > 0)
            {
                await _readModelRepository.UpdateCachedStateAsync(query.AvatarId, query.ArcRef, state, currentSequence, ct);
            }

            return state;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
