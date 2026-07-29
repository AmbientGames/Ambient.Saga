using Ambient.Domain;
using Ambient.Domain.Contracts;
using Ambient.Rpg.Engine.Application.Queries.Arcs;
using Ambient.Rpg.Engine.Application.ReadModels;
using Ambient.Rpg.Engine.Contracts.Cqrs;
using Ambient.Rpg.Engine.Domain.Arcs.TransactionLog;
using MediatR;

namespace Ambient.Rpg.Engine.Application.Handlers.Arcs;

/// <summary>
/// Handler for GetCharacterByIdQuery.
/// Returns a specific character from Arc state with its template.
/// </summary>
internal sealed class GetCharacterByIdHandler : IRequestHandler<GetCharacterByIdQuery, (CharacterState? State, Character? Template)>
{
    private readonly IArcInstanceRepository _instanceRepository;
    private readonly IArcReadModelRepository _readModelRepository;
    private readonly IWorld _world;

    public GetCharacterByIdHandler(
        IArcInstanceRepository instanceRepository,
        IArcReadModelRepository readModelRepository,
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
            // Get Arc template and triggers
            if (!_world.ArcLookup.TryGetValue(query.ArcRef, out var arcTemplate))
            {
                return (null, null);
            }

            if (!_world.ArcTriggersLookup.TryGetValue(query.ArcRef, out var expandedTriggers))
            {
                return (null, null);
            }

            // Get Arc instance
            var instance = await _instanceRepository.GetOrCreateInstanceAsync(query.AvatarId, query.ArcRef, ct);

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

    private ArcState ReplayState(
        Arc arcTemplate,
        List<ArcTrigger> expandedTriggers,
        ArcInstance instance)
    {
        var stateMachine = new ArcStateMachine(arcTemplate, expandedTriggers, _world);
        return stateMachine.ReplayToNow(instance);
    }
}
