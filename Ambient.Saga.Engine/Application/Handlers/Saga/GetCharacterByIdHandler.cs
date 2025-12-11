using Ambient.Domain;
using Ambient.Domain.DefinitionExtensions;
using Ambient.Saga.Engine.Application.Queries.Saga;
using Ambient.Saga.Engine.Application.ReadModels;
using Ambient.Saga.Engine.Contracts.Cqrs;
using Ambient.Saga.Engine.Domain.Rpg.Sagas.TransactionLog;
using MediatR;

namespace Ambient.Saga.Engine.Application.Handlers.Saga;

/// <summary>
/// Handler for GetCharacterByIdQuery.
/// Returns a specific character from Saga state with its template.
/// </summary>
internal sealed class GetCharacterByIdHandler : IRequestHandler<GetCharacterByIdQuery, (CharacterState? State, Character? Template)>
{
    private readonly ISagaInstanceRepository _instanceRepository;
    private readonly ISagaReadModelRepository _readModelRepository;
    private readonly IWorld _world;

    public GetCharacterByIdHandler(
        ISagaInstanceRepository instanceRepository,
        ISagaReadModelRepository readModelRepository,
        IWorld world)
    {
        _instanceRepository = instanceRepository;
        _readModelRepository = readModelRepository;
        _world = world;
    }

    public async Task<(CharacterState? State, Character? Template)> Handle(GetCharacterByIdQuery query, CancellationToken ct)
    {
        try
        {
            // Get Saga template and triggers
            if (!_world.SagaArcLookup.TryGetValue(query.SagaRef, out var sagaTemplate))
            {
                return (null, null);
            }

            if (!_world.SagaTriggersLookup.TryGetValue(query.SagaRef, out var expandedTriggers))
            {
                return (null, null);
            }

            // Get Saga instance
            var instance = await _instanceRepository.GetOrCreateInstanceAsync(query.AvatarId, query.SagaRef, ct);

            // Try to get cached state
            var cachedSequence = await _readModelRepository.GetCachedSequenceNumberAsync(query.AvatarId, query.SagaRef, ct);
            var currentSequence = instance.GetCommittedTransactions().LastOrDefault()?.SequenceNumber ?? 0;

            SagaState state;
            if (cachedSequence == currentSequence && cachedSequence > 0)
            {
                var cachedState = await _readModelRepository.GetCachedStateAsync(query.AvatarId, query.SagaRef, ct);
                state = cachedState ?? ReplayState(sagaTemplate, expandedTriggers, instance);
            }
            else
            {
                state = ReplayState(sagaTemplate, expandedTriggers, instance);
                if (currentSequence > 0)
                {
                    await _readModelRepository.UpdateCachedStateAsync(query.AvatarId, query.SagaRef, state, currentSequence, ct);
                }
            }

            // Find character by ID
            var characterKey = query.CharacterInstanceId.ToString();
            if (state.Characters.TryGetValue(characterKey, out var characterState))
            {
                // Get character template
                Character? template = null;
                if (!string.IsNullOrEmpty(characterState.CharacterRef))
                {
                    template = _world.TryGetCharacterByRefName(characterState.CharacterRef);
                }

                return (characterState, template);
            }

            return (null, null);
        }
        catch (Exception)
        {
            return (null, null);
        }
    }

    private SagaState ReplayState(
        SagaArc sagaTemplate,
        List<SagaTrigger> expandedTriggers,
        SagaInstance instance)
    {
        var stateMachine = new SagaStateMachine(sagaTemplate, expandedTriggers, _world);
        return stateMachine.ReplayToNow(instance);
    }
}
