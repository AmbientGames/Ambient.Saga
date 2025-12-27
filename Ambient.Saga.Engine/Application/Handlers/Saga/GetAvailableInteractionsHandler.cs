using Ambient.Domain;
using Ambient.Domain.GameLogic.Gameplay.WorldManagers;
using MediatR;
using Ambient.Saga.Engine.Application.ReadModels;
using Ambient.Saga.Engine.Domain.Rpg.Sagas.TransactionLog;
using Ambient.Saga.Engine.Application.Results.Saga;
using Ambient.Saga.Engine.Contracts.Cqrs;
using Ambient.Saga.Engine.Application.Queries.Saga;
using Ambient.Domain.Contracts;

namespace Ambient.Saga.Engine.Application.Handlers.Saga;

/// <summary>
/// Handler for GetAvailableInteractionsQuery.
/// This is the comprehensive "what can I do right now?" query.
///
/// Implementation:
/// 1. Replays transaction log to get current state
/// 2. Analyzes state to determine available interactions
/// 3. Returns rich view model for client UI
/// </summary>
internal sealed class GetAvailableInteractionsHandler : IRequestHandler<GetAvailableInteractionsQuery, AvailableInteractionsResult>
{
    private readonly ISagaInstanceRepository _instanceRepository;
    private readonly ISagaReadModelRepository _readModelRepository;
    private readonly IWorld _world;

    public GetAvailableInteractionsHandler(
        ISagaInstanceRepository instanceRepository,
        ISagaReadModelRepository readModelRepository,
        IWorld world)
    {
        _instanceRepository = instanceRepository;
        _readModelRepository = readModelRepository;
        _world = world;
    }

    public async Task<AvailableInteractionsResult> Handle(GetAvailableInteractionsQuery query, CancellationToken ct)
    {
        var result = new AvailableInteractionsResult();

        try
        {
            // Get Saga template and triggers
            if (!_world.SagaArcLookup.TryGetValue(query.SagaRef, out var sagaTemplate))
            {
                return result; // Saga not found - return empty result
            }

            if (!_world.SagaTriggersLookup.TryGetValue(query.SagaRef, out var expandedTriggers))
            {
                return result; // Triggers not found - return empty result
            }

            // Get Saga instance
            var instance = await _instanceRepository.GetOrCreateInstanceAsync(query.AvatarId, query.SagaRef, ct);

            // Replay state (with caching)
            var state = await GetStateAsync(query.AvatarId, query.SagaRef, sagaTemplate, expandedTriggers, instance, ct);

            // Convert world coordinates to Saga-relative
            var (avatarX, avatarZ) = ConvertToSagaRelative(query.Latitude, query.Longitude, sagaTemplate, _world);

            // Build result
            result.SagaDiscovered = state.Status != SagaStatus.Undiscovered;
            result.SagaStatus = state.Status;

            // Get nearby characters
            result.NearbyCharacters = BuildInteractableCharacters(state, query.Avatar, query.Latitude, query.Longitude, sagaTemplate);

            // Get active triggers
            result.ActiveTriggers = BuildActiveTriggers(state, avatarX, avatarZ, expandedTriggers);

            return result;
        }
        catch (Exception)
        {
            return result; // Return empty result on error
        }
    }

    private async Task<SagaState> GetStateAsync(
        Guid avatarId,
        string sagaRef,
        SagaArc sagaTemplate,
        List<SagaTrigger> expandedTriggers,
        SagaInstance instance,
        CancellationToken ct)
    {
        // Try to get cached state
        var cachedSequence = await _readModelRepository.GetCachedSequenceNumberAsync(avatarId, sagaRef, ct);
        var currentSequence = instance.GetCommittedTransactions().LastOrDefault()?.SequenceNumber ?? 0;

        if (cachedSequence == currentSequence && cachedSequence > 0)
        {
            var cachedState = await _readModelRepository.GetCachedStateAsync(avatarId, sagaRef, ct);
            if (cachedState != null)
            {
                return cachedState;
            }
        }

        // Replay state
        var stateMachine = new SagaStateMachine(sagaTemplate, expandedTriggers, _world);
        var state = stateMachine.ReplayToNow(instance);

        // Cache the state
        if (currentSequence > 0)
        {
            await _readModelRepository.UpdateCachedStateAsync(avatarId, sagaRef, state, currentSequence, ct);
        }

        return state;
    }

    private List<InteractableCharacter> BuildInteractableCharacters(SagaState state, AvatarBase avatar, double avatarLat, double avatarLon, SagaArc sagaTemplate)
    {
        var result = new List<InteractableCharacter>();

        //System.Diagnostics.Debug.WriteLine($"[BuildInteractableCharacters] Checking {state.Characters.Count} characters for proximity to avatar at ({avatarLat:F6}, {avatarLon:F6})");

        foreach (var characterState in state.Characters.Values)
        {
            // Only include spawned characters
            if (!characterState.IsSpawned)
                continue;

            // Get character template
            if (!_world.CharactersLookup.TryGetValue(characterState.CharacterRef, out var characterTemplate))
                continue;

            // Convert character's Saga-relative position to world GPS coordinates
            var characterWorldLon = CoordinateConverter.SagaRelativeXToLongitude(
                characterState.CurrentLongitudeX,
                sagaTemplate.LongitudeX,
                _world);
            var characterWorldLat = CoordinateConverter.SagaRelativeZToLatitude(
                characterState.CurrentLatitudeZ,
                sagaTemplate.LatitudeZ,
                _world);

            //System.Diagnostics.Debug.WriteLine($"[BuildInteractableCharacters] Character '{characterState.CharacterRef}' at world ({characterWorldLat:F6}, {characterWorldLon:F6})");

            // Check proximity - calculate distance between avatar and character
            var approachRadius = characterTemplate.Interactable?.ApproachRadius ?? 50.0;
            if (approachRadius > 0) // -1 means player must initiate, 0 means contact required
            {
                var distance = CoordinateConverter.CalculateDistance(avatarLat, avatarLon, characterWorldLat, characterWorldLon, _world);
                System.Diagnostics.Debug.WriteLine("*** distance: " + distance);

                if (distance > approachRadius)
                {
                    continue;
                }
                System.Diagnostics.Debug.WriteLine($"[BuildInteractableCharacters] Distance: {distance:F2}m, ApproachRadius: {approachRadius:F2}m");
            }

            var interactable = new InteractableCharacter
            {
                CharacterInstanceId = characterState.CharacterInstanceId,
                CharacterRef = characterState.CharacterRef,
                DisplayName = characterTemplate.DisplayName,
                State = characterState,
                Options = BuildInteractionOptions(characterState, characterTemplate, avatar)
            };

            // Get CharacterType from AffinityRef (if available)
            if (!string.IsNullOrEmpty(characterTemplate.AffinityRef))
            {
                var affinity = _world.Gameplay?.CharacterAffinities?
                    .FirstOrDefault(a => a.RefName == characterTemplate.AffinityRef);
                interactable.CharacterType = affinity?.DisplayName ?? characterTemplate.AffinityRef;
            }

            result.Add(interactable);
            System.Diagnostics.Debug.WriteLine($"[BuildInteractableCharacters] Added '{characterTemplate.DisplayName}' to nearby list");
        }

        return result;
    }

    private CharacterInteractionOptions BuildInteractionOptions(
        CharacterState characterState,
        Character characterTemplate,
        AvatarBase avatar)
    {
        var options = new CharacterInteractionOptions();

        if (characterTemplate.Interactable == null)
        {
            options.BlockedReason = "Character has no interactions defined";
            return options;
        }

        var interactable = characterTemplate.Interactable;

        // Character must be alive for most interactions
        if (!characterState.IsAlive)
        {
            // Can only loot dead characters
            options.CanLoot = !characterState.HasBeenLooted;
            options.BlockedReason = "Character is defeated";
            return options;
        }

        // Dialogue
        if (!string.IsNullOrEmpty(interactable.DialogueTreeRef))
        {
            options.CanDialogue = true;
            options.DialogueTreeRef = interactable.DialogueTreeRef;
        }

        // Determine available interactions based on character traits
        var hasHostile = characterState.Traits.ContainsKey("Hostile");
        var hasFriendly = characterState.Traits.ContainsKey("Friendly");
        var hasBossFight = characterState.Traits.ContainsKey("BossFight");
        var hasDisengaged = characterState.Traits.ContainsKey("Disengaged");
        var hasSpared = characterState.Traits.ContainsKey("Spared");

        // Disengaged/Spared characters won't fight - player fled or showed mercy
        // This overrides Hostile trait temporarily
        if (hasDisengaged || hasSpared)
        {
            options.CanAttack = false;  // Truce in effect
            options.CanTrade = false;   // Still wary, no trade
            options.CanDialogue = true; // May have new dialogue options
        }
        // Hostile characters can be attacked, but not traded with
        else if (hasHostile)
        {
            options.CanAttack = true;
            options.CanTrade = false;
        }
        // Friendly characters can be traded with, but not attacked
        else if (hasFriendly)
        {
            options.CanAttack = false;
            options.CanTrade = true;
        }
        // No traits assigned yet - allow both (pre-dialogue state)
        else
        {
            options.CanAttack = true;
            options.CanTrade = true;
        }

        return options;
    }

    private List<ActiveTriggerInfo> BuildActiveTriggers(
        SagaState state,
        double avatarX,
        double avatarZ,
        List<SagaTrigger> expandedTriggers)
    {
        var result = new List<ActiveTriggerInfo>();
        var distanceFromCenter = Math.Sqrt(avatarX * avatarX + avatarZ * avatarZ);

        foreach (var trigger in expandedTriggers)
        {
            if (!state.Triggers.TryGetValue(trigger.RefName, out var triggerState))
                continue;

            // Only include active or completed triggers (not inactive/undiscovered)
            if (triggerState.Status == SagaTriggerStatus.Inactive)
                continue;

            var info = new ActiveTriggerInfo
            {
                TriggerRef = trigger.RefName,
                Status = triggerState.Status,
                DistanceFromCenter = distanceFromCenter,
                IsWithinRadius = distanceFromCenter <= trigger.EnterRadius
            };

            result.Add(info);
        }

        return result;
    }

    private static bool HasAllQuestTokens(string[] requiredTokens, AvatarBase avatar)
    {
        if (requiredTokens == null || requiredTokens.Length == 0)
            return true;

        if (avatar.Capabilities?.QuestTokens == null)
            return false;

        foreach (var required in requiredTokens)
        {
            if (!Array.Exists(avatar.Capabilities.QuestTokens, qt => qt.QuestTokenRef == required))
                return false;
        }

        return true;
    }

    private static (double x, double z) ConvertToSagaRelative(double latitude, double longitude, SagaArc sagaArc, IWorld world)
    {
        var x = CoordinateConverter.LongitudeToSagaRelativeX(longitude, sagaArc.LongitudeX, world);
        var z = CoordinateConverter.LatitudeToSagaRelativeZ(latitude, sagaArc.LatitudeZ, world);
        return (x, z);
    }
}
