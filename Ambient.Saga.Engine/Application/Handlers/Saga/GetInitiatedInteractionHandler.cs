using Ambient.Domain.Contracts;
using Ambient.Domain.GameLogic.Gameplay.WorldManagers;
using Ambient.Saga.Engine.Application.Queries.Saga;
using Ambient.Saga.Engine.Application.Results.Saga;
using Ambient.Saga.Engine.Domain.Rpg.Sagas.TransactionLog;
using MediatR;

namespace Ambient.Saga.Engine.Application.Handlers.Saga;

/// <summary>
/// Aggregates all interactions across ALL Sagas and selects the single highest-priority one.
/// This is the "arbiter" that decides which interaction should happen.
/// </summary>
internal sealed class GetInitiatedInteractionHandler : IRequestHandler<GetInitiatedInteractionQuery, InitiatedInteractionResult>
{
    private readonly IMediator _mediator;
    private readonly IWorld _world;

    public GetInitiatedInteractionHandler(IMediator mediator, IWorld world)
    {
        _mediator = mediator;
        _world = world;
    }

    public async Task<InitiatedInteractionResult> Handle(GetInitiatedInteractionQuery request, CancellationToken ct)
    {
        var candidates = new List<InteractionCandidate>();

        // Query ALL Sagas for nearby interactions
        foreach (var sagaKvp in _world.SagaArcLookup)
        {
            var query = new GetAvailableInteractionsQuery
            {
                AvatarId = request.AvatarId,
                SagaRef = sagaKvp.Key,
                Latitude = request.Latitude,
                Longitude = request.Longitude,
                Avatar = request.Avatar
            };

            var result = await _mediator.Send(query, ct);

            // Add all nearby characters as candidates
            foreach (var character in result.NearbyCharacters)
            {
                // Convert character's Saga-relative position to world GPS coordinates
                var characterWorldLat = CoordinateConverter.SagaRelativeZToLatitude(
                    character.State.CurrentLatitudeZ,
                    sagaKvp.Value.LatitudeZ,
                    _world);

                var characterWorldLon = CoordinateConverter.SagaRelativeXToLongitude(
                    character.State.CurrentLongitudeX,
                    sagaKvp.Value.LongitudeX,
                    _world);

                // Calculate distance (character coordinates are already in world GPS from GetAvailableInteractionsHandler)
                var distance = CoordinateConverter.CalculateDistance(
                    request.Latitude,
                    request.Longitude,
                    characterWorldLat,
                    characterWorldLon,
                    _world);

                candidates.Add(new InteractionCandidate
                {
                    SagaRef = sagaKvp.Key,
                    Character = character,
                    Distance = distance
                });
            }

            // Add nearby features as candidates
            foreach (var feature in result.NearbyFeatures)
            {
                // Calculate distance (feature is at Saga center)
                var distance = CoordinateConverter.CalculateDistance(
                    request.Latitude,
                    request.Longitude,
                    sagaKvp.Value.LatitudeZ,
                    sagaKvp.Value.LongitudeX,
                    _world);

                candidates.Add(new InteractionCandidate
                {
                    SagaRef = sagaKvp.Key,
                    Feature = feature,
                    Distance = distance
                });
            }
        }

        // No interactions available
        if (candidates.Count == 0)
        {
            return new InitiatedInteractionResult { HasInteraction = false };
        }

        // Select highest priority interaction
        var winner = SelectWinner(candidates);

        return new InitiatedInteractionResult
        {
            HasInteraction = true,
            SagaRef = winner.SagaRef,
            Character = winner.Character,
            Feature = winner.Feature,
            Distance = winner.Distance,
            Priority = winner.Priority
        };
    }

    private InteractionCandidate SelectWinner(List<InteractionCandidate> candidates)
    {
        // Calculate priority for each
        foreach (var candidate in candidates)
        {
            candidate.Priority = CalculatePriority(candidate);
        }

        // Return highest priority, with distance as tiebreaker
        return candidates
            .OrderByDescending(c => c.Priority)
            .ThenBy(c => c.Distance)
            .First();
    }

    private int CalculatePriority(InteractionCandidate candidate)
    {
        var priority = 0;

        // TODO: SpawnAndInitiate gets highest priority when we track trigger type
        // if (candidate.IsInitiating) priority += 1000;

        // Characters get higher priority than features
        if (candidate.Character != null)
        {
            priority += 100;
        }
        else if (candidate.Feature != null)
        {
            priority += 50; // Features have lower priority than characters
        }

        // Closer is better (inverse distance, capped)
        priority += (int)Math.Min(100, 100.0 / (candidate.Distance + 1.0));

        return priority;
    }

    private class InteractionCandidate
    {
        public string SagaRef { get; set; } = string.Empty;
        public InteractableCharacter? Character { get; set; }
        public InteractableFeature? Feature { get; set; }
        public double Distance { get; set; }
        public int Priority { get; set; }
    }
}
