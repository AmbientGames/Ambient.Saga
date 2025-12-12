using Ambient.Domain.Contracts;
using Ambient.Saga.Engine.Application.Queries.Saga;
using Ambient.Saga.Engine.Application.ReadModels;
using Ambient.Saga.Engine.Contracts.Cqrs;
using Ambient.Saga.Engine.Domain.Rpg.Sagas.TransactionLog;
using MediatR;

namespace Ambient.Saga.Engine.Application.Handlers.Saga;

/// <summary>
/// Handler for GetSagaStateQuery.
/// Returns current Saga state (cached if available, otherwise replays from transactions).
/// </summary>
internal sealed class GetSagaStateHandler : IRequestHandler<GetSagaStateQuery, SagaState?>
{
    private readonly ISagaInstanceRepository _instanceRepository;
    private readonly ISagaReadModelRepository _readModelRepository;
    private readonly IWorld _world;

    public GetSagaStateHandler(
        ISagaInstanceRepository instanceRepository,
        ISagaReadModelRepository readModelRepository,
        IWorld world)
    {
        _instanceRepository = instanceRepository;
        _readModelRepository = readModelRepository;
        _world = world;
    }

    public async Task<SagaState?> Handle(GetSagaStateQuery query, CancellationToken ct)
    {
        try
        {
            // Get Saga template
            if (!_world.SagaArcLookup.TryGetValue(query.SagaRef, out var sagaTemplate))
            {
                return null;
            }

            // Get expanded triggers
            if (!_world.SagaTriggersLookup.TryGetValue(query.SagaRef, out var expandedTriggers))
            {
                return null;
            }

            // Get Saga instance
            var instance = await _instanceRepository.GetOrCreateInstanceAsync(query.AvatarId, query.SagaRef, ct);

            // Check if cached read model is available and up-to-date
            var cachedSequence = await _readModelRepository.GetCachedSequenceNumberAsync(query.AvatarId, query.SagaRef, ct);
            var currentSequence = instance.GetCommittedTransactions().LastOrDefault()?.SequenceNumber ?? 0;

            if (cachedSequence == currentSequence && cachedSequence > 0)
            {
                // Cache is up-to-date, use it
                var cachedState = await _readModelRepository.GetCachedStateAsync(query.AvatarId, query.SagaRef, ct);
                if (cachedState != null)
                {
                    return cachedState;
                }
            }

            // Cache miss or stale - replay from transactions
            var stateMachine = new SagaStateMachine(sagaTemplate, expandedTriggers, _world);
            var state = stateMachine.ReplayToNow(instance);

            // Update cache for next time
            if (currentSequence > 0)
            {
                await _readModelRepository.UpdateCachedStateAsync(query.AvatarId, query.SagaRef, state, currentSequence, ct);
            }

            return state;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
