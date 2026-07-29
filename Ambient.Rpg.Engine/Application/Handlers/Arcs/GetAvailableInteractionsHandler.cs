using Ambient.Domain;
using Ambient.Domain.Contracts;
using Ambient.Domain.Entities;
using Ambient.Domain.GameLogic.Gameplay.WorldManagers;
using MediatR;
using Ambient.Rpg.Engine.Application.ReadModels;
using Ambient.Rpg.Engine.Domain.Arcs.TransactionLog;
using Ambient.Rpg.Engine.Application.Results.Arcs;
using Ambient.Rpg.Engine.Contracts.Cqrs;
using Ambient.Rpg.Engine.Application.Queries.Arcs;

namespace Ambient.Rpg.Engine.Application.Handlers.Arcs;

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
    private readonly IArcInstanceRepository _instanceRepository;
    private readonly IArcReadModelRepository _readModelRepository;
    private readonly IWorld _world;

    public GetAvailableInteractionsHandler(
        IArcInstanceRepository instanceRepository,
        IArcReadModelRepository readModelRepository,
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
            // Get Arc template and triggers
            if (!_world.ArcLookup.TryGetValue(query.ArcRef, out var arcTemplate))
            {
                return result; // Arc not found - return empty result
            }

            if (!_world.ArcTriggersLookup.TryGetValue(query.ArcRef, out var expandedTriggers))
            {
                return result; // Triggers not found - return empty result
            }

            // Get Arc instance
            var instance = await _instanceRepository.GetOrCreateInstanceAsync(query.AvatarId, query.ArcRef, ct);

            // Replay state (with caching)
            var state = await GetStateAsync(query.AvatarId, query.ArcRef, arcTemplate, expandedTriggers, instance, ct);

            // Convert world coordinates to Arc-relative
            var (avatarX, avatarZ) = ConvertToArcRelative(query.Latitude, query.Longitude, arcTemplate, _world);

            // Build result
            result.ArcDiscovered = state.Status != ArcStatus.Undiscovered;
            result.ArcStatus = state.Status;

            // Get nearby characters
            result.NearbyCharacters = BuildInteractableCharacters(state, query.Avatar, query.Latitude, query.Longitude, arcTemplate);

            // Get active triggers
            result.ActiveTriggers = BuildActiveTriggers(state, avatarX, avatarZ, expandedTriggers);

            return result;
        }
        catch (Exception)
        {
            return result; // Return empty result on error
        }
    }

    private async Task<ArcState> GetStateAsync(
        Guid avatarId,
        string arcRef,
        Arc arcTemplate,
        List<ArcTrigger> expandedTriggers,
        ArcInstance instance,
        CancellationToken ct)
    {
        // Try to get cached state
        var cachedSequence = await _readModelRepository.GetCachedSequenceNumberAsync(avatarId, arcRef, ct);
        var currentSequence = instance.GetCommittedTransactions().LastOrDefault()?.SequenceNumber ?? 0;

        if (cachedSequence == currentSequence && cachedSequence > 0)
        {
            var cachedState = await _readModelRepository.GetCachedStateAsync(avatarId, arcRef, ct);
            if (cachedState != null)
            {
                return cachedState;
            }
        }

        // Replay state
        var stateMachine = new ArcStateMachine(arcTemplate, expandedTriggers, _world);
        var state = stateMachine.ReplayToNow(instance);

        // Cache the state
        if (currentSequence > 0)
        {
            await _readModelRepository.UpdateCachedStateAsync(avatarId, arcRef, state, currentSequence, ct);
        }

        return state;
    }

    private List<InteractableCharacter> BuildInteractableCharacters(ArcState state, AvatarBase avatar, double avatarLat, double avatarLon, Arc arcTemplate)
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

            // Convert character's Arc-relative position to world GPS coordinates
            var characterWorldLon = CoordinateConverter.ArcRelativeXToLongitude(
                characterState.CurrentLongitudeX,
                arcTemplate.Longitude,
                _world);
            var characterWorldLat = CoordinateConverter.ArcRelativeZToLatitude(
                characterState.CurrentLatitudeZ,
                arcTemplate.Latitude,
                _world);

            //System.Diagnostics.Debug.WriteLine($"[BuildInteractableCharacters] Character '{characterState.CharacterRef}' at world ({characterWorldLat:F6}, {characterWorldLon:F6})");

            // Characters without an Interactable section are valid content (scenery/
            // battle-only NPCs) — skip them instead of throwing. The NRE here was
            // swallowed by the catch-all, which blanked EVERY interaction in the arc.
            if (characterTemplate.Interactable == null)
                continue;

            // Check proximity - calculate distance between avatar and character
            var approachRadius = characterTemplate.Interactable.ApproachRadius;
            var distance = CoordinateConverter.CalculateDistance(avatarLat, avatarLon, characterWorldLat, characterWorldLon, _world);
            if (distance > approachRadius)
                continue;

            var interactable = new InteractableCharacter
            {
                CharacterInstanceId = characterState.CharacterInstanceId,
                CharacterRef = characterState.CharacterRef,
                DisplayName = characterTemplate.DisplayName,
                State = characterState,
                Options = BuildInteractionOptions(characterState, characterTemplate, avatar, arcTemplate)
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
        AvatarBase avatar,
        Arc arcTemplate)
    {
        var options = new CharacterInteractionOptions();

        // Check if avatar is the owner of this arc
        var isOwner = !string.IsNullOrEmpty(arcTemplate.OwnerAvatarId)
                      && avatar is AvatarEntity avatarEntity
                      && avatarEntity.AvatarId.ToString() == arcTemplate.OwnerAvatarId;
        options.IsOwner = isOwner;

        if (characterTemplate.Interactable == null)
        {
            options.BlockedReason = "Character has no interactions defined";
            return options;
        }

        var interactable = characterTemplate.Interactable;

        // Character must be alive for most interactions
        if (!characterState.IsAlive)
        {
            options.BlockedReason = "Character is defeated";
            return options;
        }

        // Owner gets free trade, no dialogue, no combat
        if (isOwner)
        {
            options.CanTrade = true;
            options.CanAttack = false;
            options.CanDialogue = false;
            return options;
        }

        // Dialogue
        if (!string.IsNullOrEmpty(interactable.DialogueTreeRef))
        {
            options.CanDialogue = true;
            options.DialogueTreeRef = interactable.DialogueTreeRef;
        }

        // Determine available interactions based on character traits. Use the
        // value-aware CarriesTrait: an explicit Value="0" (e.g. a deliberately
        // non-hostile authored NPC with Hostile="0") must count as NOT carried,
        // never treated as present by a key-only check.
        var hasHostile = characterState.CarriesTrait("Hostile");
        var hasFriendly = characterState.CarriesTrait("Friendly");
        var hasBossFight = characterState.CarriesTrait("BossFight");
        var hasWillTrade = characterState.CarriesTrait("WillTrade");
        var hasDisengaged = characterState.CarriesTrait("Disengaged");
        var hasSpared = characterState.CarriesTrait("Spared");

        // Disengaged/Spared characters won't fight - avatar fled or showed mercy
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
        // Traders (WillTrade) and friendlies can be traded with, but not attacked.
        // WillTrade is the canonical trade-identity trait content/UI use; honor it
        // here so a WillTrade merchant isn't dropped into the attackable fallthrough.
        else if (hasFriendly || hasWillTrade)
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

        // Proximity assault: an effectively-Hostile character initiates battle when
        // the avatar is inside its ApproachRadius, unless a truce trait suppresses
        // it (Disengaged after a successful flee, Spared after mercy). The player
        // can still attack by clicking; this flag only drives CHARACTER-initiated
        // battle. Owner/dead cases returned early above and stay false.
        options.IsAssault = hasHostile && !hasDisengaged && !hasSpared;

        return options;
    }

    private List<ActiveTriggerInfo> BuildActiveTriggers(
        ArcState state,
        double avatarX,
        double avatarZ,
        List<ArcTrigger> expandedTriggers)
    {
        var result = new List<ActiveTriggerInfo>();
        var distanceFromCenter = Math.Sqrt(avatarX * avatarX + avatarZ * avatarZ);

        foreach (var trigger in expandedTriggers)
        {
            if (!state.Triggers.TryGetValue(trigger.RefName, out var triggerState))
                continue;

            // Only include active or completed triggers (not inactive/undiscovered)
            if (triggerState.Status == ArcTriggerStatus.Inactive)
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

    //private static bool HasAllQuestTokens(string[] requiredTokens, AvatarBase avatar)
    //{
    //    if (requiredTokens == null || requiredTokens.Length == 0)
    //        return true;

    //    if (avatar.Capabilities?.QuestTokens == null)
    //        return false;

    //    foreach (var required in requiredTokens)
    //    {
    //        if (!Array.Exists(avatar.Capabilities.QuestTokens, qt => qt.QuestTokenRef == required))
    //            return false;
    //    }

    //    return true;
    //}

    private static (double x, double z) ConvertToArcRelative(double latitude, double longitude, Arc arc, IWorld world)
    {
        var x = CoordinateConverter.LongitudeToArcRelativeX(longitude, arc.Longitude, world);
        var z = CoordinateConverter.LatitudeToArcRelativeZ(latitude, arc.Latitude, world);
        return (x, z);
    }
}
