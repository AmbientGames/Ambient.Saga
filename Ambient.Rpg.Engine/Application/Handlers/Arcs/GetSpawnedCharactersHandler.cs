using Ambient.Domain;
using Ambient.Domain.Contracts;
using Ambient.Rpg.Engine.Application.Queries.Arcs;
using Ambient.Rpg.Engine.Application.ReadModels;
using Ambient.Rpg.Engine.Contracts.Cqrs;
using Ambient.Rpg.Engine.Domain.Arcs.TransactionLog;
using MediatR;

namespace Ambient.Rpg.Engine.Application.Handlers.Arcs;

/// <summary>
/// Handler for GetSpawnedCharactersQuery.
/// Returns all spawned characters in an arc.
/// </summary>
internal sealed class GetSpawnedCharactersHandler : IRequestHandler<GetSpawnedCharactersQuery, List<CharacterState>>
{
    private readonly IArcInstanceRepository _instanceRepository;
    private readonly IArcReadModelRepository _readModelRepository;
    private readonly IWorld _world;

    public GetSpawnedCharactersHandler(
        IArcInstanceRepository instanceRepository,
        IArcReadModelRepository readModelRepository,
        IWorld world)
    {
        _instanceRepository = instanceRepository;
        _readModelRepository = readModelRepository;
        _world = world;
    }

    public async Task<List<CharacterState>> Handle(GetSpawnedCharactersQuery query, CancellationToken ct)
    {
        try
        {
            // Get Arc template and triggers
            if (!_world.ArcLookup.TryGetValue(query.ArcRef, out var arcTemplate))
            {
                return new List<CharacterState>();
            }

            if (!_world.ArcTriggersLookup.TryGetValue(query.ArcRef, out var expandedTriggers))
            {
                return new List<CharacterState>();
            }

            // Get Arc instance
            var instance = await _instanceRepository.GetOrCreateInstanceAsync(query.AvatarId, query.ArcRef, ct);
            //System.Diagnostics.Debug.WriteLine($"[GetSpawnedCharacters] Instance has {instance.GetCommittedTransactions().Count()} committed transactions");

            // Try to get cached state
            var cachedSequence = await _readModelRepository.GetCachedSequenceNumberAsync(query.AvatarId, query.ArcRef, ct);
            var currentSequence = instance.GetCommittedTransactions().LastOrDefault()?.SequenceNumber ?? 0;

            ArcState state;
            if (cachedSequence == currentSequence && cachedSequence > 0)
            {
                var cachedState = await _readModelRepository.GetCachedStateAsync(query.AvatarId, query.ArcRef, ct);
                state = cachedState ?? ReplayState(arcTemplate, expandedTriggers, instance);
            }
            else
            {
                state = ReplayState(arcTemplate, expandedTriggers, instance);
                if (currentSequence > 0)
                {
                    await _readModelRepository.UpdateCachedStateAsync(query.AvatarId, query.ArcRef, state, currentSequence, ct);
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
                //System.Diagnostics.Debug.WriteLine($"  - {ch.CharacterRef} at ({ch.CurrentLongitude:F6}, {ch.CurrentLatitude:F6}), IsSpawned={ch.IsSpawned}, IsAlive={ch.IsAlive}");
            }

            return result;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[GetSpawnedCharacters] ERROR: {ex.Message}");
            return new List<CharacterState>();
        }
    }

    private ArcState ReplayState(
        Arc arcTemplate,
        List<ArcTrigger> expandedTriggers,
        ArcInstance instance)
    {
        var stateMachine = new ArcStateMachine(arcTemplate, expandedTriggers, _world);
        return stateMachine.ReplayToNow(instance);
    }
}
