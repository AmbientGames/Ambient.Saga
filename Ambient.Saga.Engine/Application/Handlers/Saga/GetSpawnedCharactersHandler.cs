using Ambient.Domain;
using Ambient.Domain.DefinitionExtensions;
using Ambient.Saga.Engine.Application.Queries.Saga;
using Ambient.Saga.Engine.Application.ReadModels;
using Ambient.Saga.Engine.Contracts.Cqrs;
using Ambient.Saga.Engine.Domain.Rpg.Sagas.TransactionLog;
using MediatR;

namespace Ambient.Saga.Engine.Application.Handlers.Saga;

/// <summary>
/// Handler for GetSpawnedCharactersQuery.
/// Returns all spawned characters in a Saga.
/// </summary>
internal sealed class GetSpawnedCharactersHandler : IRequestHandler<GetSpawnedCharactersQuery, List<CharacterState>>
{
    private readonly ISagaInstanceRepository _instanceRepository;
    private readonly ISagaReadModelRepository _readModelRepository;
    private readonly World _world;

    public GetSpawnedCharactersHandler(
        ISagaInstanceRepository instanceRepository,
        ISagaReadModelRepository readModelRepository,
        World world)
    {
        _instanceRepository = instanceRepository;
        _readModelRepository = readModelRepository;
        _world = world;
    }

    public async Task<List<CharacterState>> Handle(GetSpawnedCharactersQuery query, CancellationToken ct)
    {
        try
        {
            // Get Saga template and triggers
            if (!_world.SagaArcLookup.TryGetValue(query.SagaRef, out var sagaTemplate))
            {
                return new List<CharacterState>();
            }

            if (!_world.SagaTriggersLookup.TryGetValue(query.SagaRef, out var expandedTriggers))
            {
                return new List<CharacterState>();
            }

            // Get Saga instance
            var instance = await _instanceRepository.GetOrCreateInstanceAsync(query.AvatarId, query.SagaRef, ct);
            //System.Diagnostics.Debug.WriteLine($"[GetSpawnedCharacters] Instance has {instance.GetCommittedTransactions().Count()} committed transactions");

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

            var characters = state.Characters.Values.AsEnumerable();

            if (query.SpawnedOnly)
            {
                characters = characters.Where(c => c.IsSpawned);
                //System.Diagnostics.Debug.WriteLine($"[GetSpawnedCharacters] After SpawnedOnly filter: {characters.Count()} characters");
            }

            if (query.AliveOnly)
            {
                characters = characters.Where(c => c.IsAlive);
                //System.Diagnostics.Debug.WriteLine($"[GetSpawnedCharacters] After AliveOnly filter: {characters.Count()} characters");
            }

            var result = characters.ToList();

            foreach (var ch in result)
            {
                //System.Diagnostics.Debug.WriteLine($"  - {ch.CharacterRef} at ({ch.CurrentLongitudeX:F6}, {ch.CurrentLatitudeZ:F6}), IsSpawned={ch.IsSpawned}, IsAlive={ch.IsAlive}");
            }

            return result;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[GetSpawnedCharacters] ERROR: {ex.Message}");
            return new List<CharacterState>();
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
