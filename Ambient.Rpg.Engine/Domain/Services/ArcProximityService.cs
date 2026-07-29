using Ambient.Domain;
using Ambient.Domain.Contracts;
using Ambient.Domain.GameLogic.Gameplay.WorldManagers;
using Ambient.Rpg.Engine.Contracts;
using Ambient.Rpg.Engine.Contracts.Persistence;
using Ambient.Rpg.Engine.Domain.Arcs;
using Ambient.Rpg.Engine.Domain.Arcs.TransactionLog;

namespace Ambient.Rpg.Engine.Domain.Services;

/// <summary>
/// Application service for determining which Arcs are active near a given position.
/// This is the single source of truth for Arc proximity logic - matches actual game behavior.
/// </summary>
public static class ArcProximityService
{
    /// <summary>
    /// Filters arcs to only those within their DiscoverRadius of the given position.
    /// Uses model coordinates and applies proper scale conversion.
    /// Each Arc defines its own DiscoverRadius (default 100m in XSD).
    /// </summary>
    /// <param name="modelX">Position X in model coordinates</param>
    /// <param name="modelZ">Position Z in model coordinates</param>
    /// <param name="world">World containing Arc data</param>
    /// <param name="radiusMultiplier">Multiplier for DiscoverRadius (default 1.0, use higher for preview)</param>
    /// <returns>Enumerable of arcs within range, with their model coordinates and distance</returns>
    public static IEnumerable<(Arc Arc, double ArcModelX, double ArcModelZ, double DistanceMeters)>
        FilterArcsByProximity(
            double modelX,
            double modelZ,
            IWorld world,
            double radiusMultiplier = 1.0)
    {
        if (world.Gameplay.Saga == null)
            yield break;

        // Get proper scale factors for model-to-meters conversion
        var scaleX = world.IsProcedural ? 1.0 : world.HeightMapLongitudeScale;
        var scaleZ = world.IsProcedural ? 1.0 : world.HeightMapLatitudeScale;

        foreach (var arc in world.Gameplay.Saga)
        {
            // Convert Arc GPS to model coordinates
            var arcModelX = CoordinateConverter.LongitudeToModelX(arc.Longitude, world);
            var arcModelZ = CoordinateConverter.LatitudeToModelZ(arc.Latitude, world);

            // Calculate distance in meters (must convert X and Z separately due to latitude correction)
            var deltaModelX = modelX - arcModelX;
            var deltaModelZ = modelZ - arcModelZ;
            var deltaMetersX = deltaModelX / scaleX;
            var deltaMetersZ = deltaModelZ / scaleZ;
            var distanceMeters = Math.Sqrt(deltaMetersX * deltaMetersX + deltaMetersZ * deltaMetersZ);

            // Quick reject: if beyond arc's DiscoverRadius (with multiplier), skip entirely
            var effectiveRadius = arc.DiscoverRadius * radiusMultiplier;
            if (distanceMeters > effectiveRadius)
                continue;

            yield return (arc, arcModelX, arcModelZ, distanceMeters);
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
    /// <param name="progressRepo">Avatar progress table for cross-arc quest-token gating (optional; without it token gates fall back to per-instance state)</param>
    /// <returns>All interactions at this position, sorted by priority and distance</returns>
    public static async Task<List<ArcInteraction>> QueryAllInteractionsAtPositionAsync(
        double modelX,
        double modelZ,
        AvatarBase? avatar,
        IWorld world,
        IWorldStateRepository worldRepository = null,
        IAvatarProgressRepository? progressRepo = null)
    {
        var interactions = new List<ArcInteraction>();

        if (world.Gameplay.Saga == null)
            return interactions;

        var horizontalScale = world.IsProcedural ? 1.0 : world.WorldConfiguration.HeightMapSettings.HorizontalScale;

        // Pre-filter to only nearby arcs (major performance optimization)
        foreach (var (arc, arcModelX, arcModelZ, distanceToCenter) in FilterArcsByProximity(modelX, modelZ, world))
        {
            // CHECK PROXIMITY TRIGGERS
            if (!world.ArcTriggersLookup.TryGetValue(arc.RefName, out var triggers))
                continue;

            foreach (var trigger in triggers)
            {
                var scaledEnterRadius = trigger.EnterRadius * horizontalScale;

                var isWithin = TriggerProximityChecker.IsWithinTriggerRadiusSquared(
                    arcModelX, arcModelZ,
                    scaledEnterRadius,
                    modelX, modelZ);

                if (isWithin)
                {
                    var triggerStatus = await DetermineTriggerStatusAsync(arc, trigger, avatar, world, worldRepository, progressRepo);
                    interactions.Add(new ArcInteraction
                    {
                        Type = ArcInteractionType.ArcTrigger,
                        ArcRef = arc.RefName,
                        EntityRef = trigger.RefName,
                        DistanceMeters = distanceToCenter,
                        Status = triggerStatus,
                        ArcTriggerRef = trigger.RefName,
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
    /// Uses replayed ArcState from the state machine - no duplicated logic.
    /// </summary>
    private static async Task<InteractionStatus> DetermineTriggerStatusAsync(
        Arc arc,
        ArcTrigger arcTrigger,
        AvatarBase? avatar,
        IWorld world,
        IWorldStateRepository? worldRepository,
        IAvatarProgressRepository? progressRepo = null)
    {
        if (avatar == null)
            return InteractionStatus.Available;

        // Use replayed state from state machine (single source of truth for locked/unlocked + completion)
        if (worldRepository == null)
        {
            System.Diagnostics.Debug.WriteLine($"[DetermineTriggerStatus] worldRepository is null for trigger '{arcTrigger.RefName}'");
            return InteractionStatus.Available;
        }

        var avatarId = avatar.AvatarId.ToString();
        var arcInstance = await worldRepository.GetArcInstanceAsync(avatarId, arc.RefName);

        if (arcInstance == null)
        {
            System.Diagnostics.Debug.WriteLine($"[DetermineTriggerStatus] No arc instance for avatar '{avatarId}', arc '{arc.RefName}'");
            return InteractionStatus.Available;
        }

        if (!world.ArcTriggersLookup.TryGetValue(arc.RefName, out var triggers))
        {
            System.Diagnostics.Debug.WriteLine($"[DetermineTriggerStatus] No triggers in lookup for arc '{arc.RefName}'");
            return InteractionStatus.Available;
        }

        var stateMachine = new ArcStateMachine(arc, triggers, world);
        var state = stateMachine.ReplayToNow(arcInstance);

        // Completion first — a completed trigger is not "locked" just because tokens are missing
        if (state.Triggers.TryGetValue(arcTrigger.RefName, out var triggerState)
            && triggerState.Status == ArcTriggerStatus.Completed)
        {
            return InteractionStatus.Complete;
        }

        // Lock state derives from avatar progress table (or legacy arc state)
        var canActivate = progressRepo != null && avatar != null
            ? TriggerAvailabilityChecker.CanActivate(arcTrigger, progressRepo, avatar.AvatarId)
            : TriggerAvailabilityChecker.CanActivate(arcTrigger, state);
        if (!canActivate)
            return InteractionStatus.Locked;

        return InteractionStatus.Available;
    }
}

/// <summary>
/// Type of interaction at a position in an arc.
/// </summary>
public enum ArcInteractionType
{
    /// <summary>Proximity trigger that spawns characters or activates content</summary>
    ArcTrigger,

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
public class ArcInteraction
{
    /// <summary>Type of interaction (Trigger/Feature/Character)</summary>
    public required ArcInteractionType Type { get; init; }

    /// <summary>Reference to the arc containing this interaction</summary>
    public required string ArcRef { get; init; }

    /// <summary>Reference to the specific entity (trigger/character/feature RefName)</summary>
    public required string EntityRef { get; init; }

    /// <summary>Distance from position to this interaction in meters</summary>
    public required double DistanceMeters { get; init; }

    /// <summary>Availability status (Available/Locked/Complete)</summary>
    public required InteractionStatus Status { get; init; }

    /// <summary>Priority for sorting (1=highest, Character > Trigger)</summary>
    public required int Priority { get; init; }

    /// <summary>Trigger RefName (if this is a Trigger interaction)</summary>
    public string? ArcTriggerRef { get; init; }

    /// <summary>Number of characters that would spawn from this trigger</summary>
    public int SpawnCount { get; init; }

    /// <summary>Character RefName (if this is a Character interaction)</summary>
    public string? CharacterRef { get; init; }
}

