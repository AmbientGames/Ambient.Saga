using Ambient.Domain;
using Ambient.Domain.Contracts;
using Ambient.Domain.GameLogic.Gameplay.WorldManagers;
using Ambient.Saga.Engine.Contracts;
using Ambient.Saga.Engine.Domain.Rpg.Sagas;
using Ambient.Saga.Engine.Domain.Rpg.Sagas.TransactionLog;

namespace Ambient.Saga.Engine.Domain.Services;

/// <summary>
/// Application service for determining which Sagas are active near a given position.
/// This is the single source of truth for Saga proximity logic - matches actual game behavior.
/// </summary>
public static class SagaProximityService
{
    /// <summary>
    /// Filters SagaArcs to only those within their DiscoverRadius of the given position.
    /// Uses model coordinates and applies proper scale conversion.
    /// Each SagaArc defines its own DiscoverRadius (default 100m in XSD).
    /// </summary>
    /// <param name="modelX">Position X in model coordinates</param>
    /// <param name="modelZ">Position Z in model coordinates</param>
    /// <param name="world">World containing Saga data</param>
    /// <param name="radiusMultiplier">Multiplier for DiscoverRadius (default 1.0, use higher for preview)</param>
    /// <returns>Enumerable of SagaArcs within range, with their model coordinates and distance</returns>
    public static IEnumerable<(SagaArc SagaArc, double SagaModelX, double SagaModelZ, double DistanceMeters)>
        FilterSagaArcsByProximity(
            double modelX,
            double modelZ,
            IWorld world,
            double radiusMultiplier = 1.0)
    {
        if (world.Gameplay.SagaArcs == null)
            yield break;

        // Get proper scale factors for model-to-meters conversion
        var scaleX = world.IsProcedural ? 1.0 : world.HeightMapLongitudeScale;
        var scaleZ = world.IsProcedural ? 1.0 : world.HeightMapLatitudeScale;

        foreach (var sagaArc in world.Gameplay.SagaArcs)
        {
            // Convert Saga GPS to model coordinates
            var sagaModelX = CoordinateConverter.LongitudeToModelX(sagaArc.Longitude, world);
            var sagaModelZ = CoordinateConverter.LatitudeToModelZ(sagaArc.Latitude, world);

            // Calculate distance in meters (must convert X and Z separately due to latitude correction)
            var deltaModelX = modelX - sagaModelX;
            var deltaModelZ = modelZ - sagaModelZ;
            var deltaMetersX = deltaModelX / scaleX;
            var deltaMetersZ = deltaModelZ / scaleZ;
            var distanceMeters = Math.Sqrt(deltaMetersX * deltaMetersX + deltaMetersZ * deltaMetersZ);

            // Quick reject: if beyond saga's DiscoverRadius (with multiplier), skip entirely
            var effectiveRadius = sagaArc.DiscoverRadius * radiusMultiplier;
            if (distanceMeters > effectiveRadius)
                continue;

            yield return (sagaArc, sagaModelX, sagaModelZ, distanceMeters);
        }
    }

  

    /// <summary>
    /// Queries ALL possible interactions at a position: proximity triggers and spawned characters.
    /// Returns results sorted by priority: Character > Trigger.
    /// This is the comprehensive query the UI should use for "what would happen if I clicked here?"
    /// </summary>
    /// <param name="modelX">Position X in model coordinates</param>
    /// <param name="modelZ">Position Z in model coordinates</param>
    /// <param name="avatar">Avatar for availability checking</param>
    /// <param name="world">World data</param>
    /// <param name="worldRepository">Repository for checking character/feature state (optional)</param>
    /// <returns>All interactions at this position, sorted by priority and distance</returns>
    public static async Task<List<SagaInteraction>> QueryAllInteractionsAtPositionAsync(
        double modelX,
        double modelZ,
        AvatarBase? avatar,
        IWorld world,
        IWorldStateRepository worldRepository = null)
    {
        var interactions = new List<SagaInteraction>();

        if (world.Gameplay.SagaArcs == null)
            return interactions;

        var horizontalScale = world.IsProcedural ? 1.0 : world.WorldConfiguration.HeightMapSettings.HorizontalScale;

        // Pre-filter to only nearby SagaArcs (major performance optimization)
        foreach (var (saga, sagaModelX, sagaModelZ, distanceToCenter) in FilterSagaArcsByProximity(modelX, modelZ, world))
        {
            // CHECK PROXIMITY TRIGGERS
            if (!world.SagaTriggersLookup.TryGetValue(saga.RefName, out var triggers))
                continue;

            foreach (var trigger in triggers)
            {
                var scaledEnterRadius = trigger.EnterRadius * horizontalScale;

                var isWithin = TriggerProximityChecker.IsWithinTriggerRadiusSquared(
                    sagaModelX, sagaModelZ,
                    scaledEnterRadius,
                    modelX, modelZ);

                if (isWithin)
                {
                    var triggerStatus = await DetermineTriggerStatusAsync(saga, trigger, avatar, world, worldRepository);
                    interactions.Add(new SagaInteraction
                    {
                        Type = SagaInteractionType.SagaTrigger,
                        SagaRef = saga.RefName,
                        EntityRef = trigger.RefName,
                        DistanceMeters = distanceToCenter,
                        Status = triggerStatus,
                        SagaTriggerRef = trigger.RefName,
                        SpawnCount = trigger.Spawn?.Sum(s => s.Count) ?? 0,
                        Priority = 2 // Character = 1, Trigger = 2
                    });
                }
            }
        }

        // Sort by priority (1=highest), then by distance (closest first)
        return interactions
            .OrderBy(i => i.Priority)
            .ThenBy(i => i.DistanceMeters)
            .ToList();
    }

    /// <summary>
    /// Determines the status of a trigger (available, locked, complete).
    /// Uses replayed SagaState from the state machine - no duplicated logic.
    /// </summary>
    private static async Task<InteractionStatus> DetermineTriggerStatusAsync(
        SagaArc sagaArc,
        SagaTrigger sagaTrigger,
        AvatarBase? avatar,
        IWorld world,
        IWorldStateRepository? worldRepository)
    {
        if (avatar == null)
            return InteractionStatus.Available;

        // Check quest token requirements first
        if (!TriggerAvailabilityChecker.CanActivate(sagaTrigger, avatar))
            return InteractionStatus.Locked;

        // Use replayed state from state machine to check completion
        if (worldRepository == null)
        {
            System.Diagnostics.Debug.WriteLine($"[DetermineTriggerStatus] worldRepository is null for trigger '{sagaTrigger.RefName}'");
            return InteractionStatus.Available;
        }

        var avatarId = avatar.AvatarId.ToString();
        var sagaInstance = await worldRepository.GetSagaInstanceAsync(avatarId, sagaArc.RefName);

        if (sagaInstance == null)
        {
            System.Diagnostics.Debug.WriteLine($"[DetermineTriggerStatus] No saga instance for avatar '{avatarId}', saga '{sagaArc.RefName}'");
            return InteractionStatus.Available;
        }

        if (!world.SagaTriggersLookup.TryGetValue(sagaArc.RefName, out var triggers))
        {
            System.Diagnostics.Debug.WriteLine($"[DetermineTriggerStatus] No triggers in lookup for saga '{sagaArc.RefName}'");
            return InteractionStatus.Available;
        }

        // Replay state using the state machine (single source of truth)
        var stateMachine = new SagaStateMachine(sagaArc, triggers, world);
        var state = stateMachine.ReplayToNow(sagaInstance);

        // Check trigger status from replayed state
        if (state.Triggers.TryGetValue(sagaTrigger.RefName, out var triggerState))
        {
            if (triggerState.Status == SagaTriggerStatus.Completed)
                return InteractionStatus.Complete;
        }

        return InteractionStatus.Available;
    }
}

/// <summary>
/// Type of interaction at a position in a Saga.
/// </summary>
public enum SagaInteractionType
{
    /// <summary>Proximity trigger that spawns characters or activates content</summary>
    SagaTrigger,

    /// <summary>Spawned character (future: when character positions are tracked)</summary>
    Character
}

/// <summary>
/// Status/availability of an interaction.
/// </summary>
public enum InteractionStatus
{
    /// <summary>Available to interact with</summary>
    Available,

    /// <summary>Locked (missing quest tokens, etc.)</summary>
    Locked,

    /// <summary>Complete (character defeated, quest done, etc.)</summary>
    Complete
}

/// <summary>
/// Comprehensive result of querying interactions at a position.
/// Includes proximity triggers, features, and spawned characters.
/// </summary>
public class SagaInteraction
{
    /// <summary>Type of interaction (Trigger/Feature/Character)</summary>
    public required SagaInteractionType Type { get; init; }

    /// <summary>Reference to the Saga containing this interaction</summary>
    public required string SagaRef { get; init; }

    /// <summary>Reference to the specific entity (trigger/character/feature RefName)</summary>
    public required string EntityRef { get; init; }

    /// <summary>Distance from position to this interaction in meters</summary>
    public required double DistanceMeters { get; init; }

    /// <summary>Availability status (Available/Locked/Complete)</summary>
    public required InteractionStatus Status { get; init; }

    /// <summary>Priority for sorting (1=highest, Character > Trigger)</summary>
    public required int Priority { get; init; }

    /// <summary>Trigger RefName (if this is a Trigger interaction)</summary>
    public string? SagaTriggerRef { get; init; }

    /// <summary>Number of characters that would spawn from this trigger</summary>
    public int SpawnCount { get; init; }

    /// <summary>Character RefName (if this is a Character interaction)</summary>
    public string? CharacterRef { get; init; }
}

