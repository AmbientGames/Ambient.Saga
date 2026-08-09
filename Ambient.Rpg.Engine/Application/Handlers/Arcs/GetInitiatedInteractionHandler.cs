using Ambient.Domain.Contracts;
using Ambient.Domain.GameLogic.Gameplay.WorldManagers;
using Ambient.Rpg.Engine.Application.Queries.Arcs;
using Ambient.Rpg.Engine.Application.Results.Arcs;
using Ambient.Rpg.Engine.Domain.Arcs.TransactionLog;
using MediatR;

namespace Ambient.Rpg.Engine.Application.Handlers.Arcs;

/// <summary>
/// Aggregates all interactions across ALL Arcs and selects the single highest-priority one.
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

        // Query ALL Arcs for nearby interactions
        foreach (var arcKvp in _world.ArcLookup)
        {
            // Server-sourced arcs (player-created shopkeepers, geocaches, remnant Loot) are discovered
            // via traces, not proximity. Skip them so their synthetic characters don't auto-initiate.
            if (!string.IsNullOrEmpty(arcKvp.Value.OwnerAvatarId))
                continue;

            var query = new GetAvailableInteractionsQuery
            {
                AvatarId = request.AvatarId,
                ArcRef = arcKvp.Key,
                Latitude = request.Latitude,
                Longitude = request.Longitude,
                Avatar = request.Avatar
            };

            var result = await _mediator.Send(query, ct);

            // Add all nearby characters as candidates
            foreach (var character in result.NearbyCharacters)
            {
                // Convert character's Arc-relative position to world GPS coordinates
                var characterWorldLat = CoordinateConverter.ArcRelativeZToLatitude(
                    character.State.CurrentLatitudeZ,
                    arcKvp.Value.Latitude,
                    _world);

                var characterWorldLon = CoordinateConverter.ArcRelativeXToLongitude(
                    character.State.CurrentLongitudeX,
                    arcKvp.Value.Longitude,
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
                    ArcRef = arcKvp.Key,
                    Character = character,
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
            ArcRef = winner.ArcRef,
            Character = winner.Character,
            Distance = winner.Distance,
            Priority = winner.Priority,
            // Computed by GetAvailableInteractionsHandler from the character's
            // EFFECTIVE traits (template + replayed TraitAssigned/TraitRemoved).
            IsAssault = winner.Character?.Options.IsAssault == true
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

        if (candidate.Character != null)
        {
            // Base priority for characters
            priority += 100;

            // Recently spawned characters get priority boost (within last 30 seconds)
            var secondsSinceSpawn = (DateTime.UtcNow - candidate.Character.State.SpawnedAt).TotalSeconds;
            if (secondsSinceSpawn < 30)
            {
                // Priority decays from 200 to 0 over 30 seconds
                priority += (int)(200 * (1.0 - secondsSinceSpawn / 30.0));
            }

            // Hostile characters get priority (they're threats). Value-aware so a
            // deliberately non-hostile NPC (Hostile="0") isn't ranked as a threat.
            if (candidate.Character.State.CarriesTrait("Hostile"))
            {
                priority += 50;
            }

            // Characters with dialogue get slight priority boost
            if (candidate.Character.Options.CanDialogue)
            {
                priority += 25;
            }
        }

        // Closer is better (inverse distance, capped)
        priority += (int)Math.Min(100, 100.0 / (candidate.Distance + 1.0));

        return priority;
    }

    private class InteractionCandidate
    {
        public string ArcRef { get; set; } = string.Empty;
        public InteractableCharacter? Character { get; set; }
        public double Distance { get; set; }
        public int Priority { get; set; }
    }
}
