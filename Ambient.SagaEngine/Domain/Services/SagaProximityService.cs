using Ambient.Domain;
using Ambient.Domain.DefinitionExtensions;
using Ambient.Domain.GameLogic.Gameplay.WorldManagers;
using Ambient.SagaEngine.Contracts;
using Ambient.SagaEngine.Domain.Rpg.Sagas;
using Ambient.SagaEngine.Domain.Rpg.Sagas.TransactionLog;

namespace Ambient.SagaEngine.Domain.Services;

/// <summary>
/// Application service for determining which Sagas are active near a given position.
/// This is the single source of truth for Saga proximity logic - matches actual game behavior.
/// </summary>
public static class SagaProximityService
{
    /// <summary>
    /// Gets all Sagas that should be active (visible/interactable) near the given position.
    /// Uses 150% of outermost trigger radius as the "preview distance" for early discovery.
    /// </summary>
    /// <param name="latitude">Avatar latitude in degrees</param>
    /// <param name="longitude">Avatar longitude in degrees</param>
    /// <param name="world">World containing Saga data</param>
    /// <returns>List of active Sagas with their expanded triggers, sorted by distance</returns>
    public static List<ActiveSaga> GetActiveSagasNearPosition(
        double latitude,
        double longitude,
        World world)
    {
        if (world.Gameplay.SagaArcs == null)
            return new List<ActiveSaga>();

        var results = new List<ActiveSaga>();

        foreach (var sagaArc in world.Gameplay.SagaArcs)
        {
            // Get pre-expanded triggers from world lookup
            if (!world.SagaTriggersLookup.TryGetValue(sagaArc.RefName, out var triggers) || !triggers.Any())
                continue;

            // Find outermost trigger (largest enter radius)
            var outermostTrigger = triggers.OrderByDescending(t => t.EnterRadius).First();

            // Calculate distance from avatar to Saga center
            var distanceMeters = TriggerProximityChecker.CalculateDistance(
                sagaArc.LongitudeX, sagaArc.LatitudeZ,
                longitude, latitude);

            // Preview distance: show Saga when avatar gets within 150% of outermost trigger
            // This gives players early warning and smooth discovery experience
            var previewDistance = outermostTrigger.EnterRadius * 1.5;

            if (distanceMeters <= previewDistance)
            {
                // Add hysteresis margin to preview for exit behavior
                var previewExitDistance = TriggerProximityChecker.GetExitRadius((float)previewDistance);

                results.Add(new ActiveSaga
                {
                    SagaArc = sagaArc,
                    SagaTriggers = triggers.OrderByDescending(t => t.EnterRadius).ToList(), // Sorted outer→inner
                    DistanceMeters = distanceMeters,
                    PreviewEnterDistance = previewDistance,
                    PreviewExitDistance = previewExitDistance,
                    IsWithinPreviewRange = distanceMeters <= previewDistance
                });
            }
        }

        // Return sorted by distance (closest first)
        return results.OrderBy(poi => poi.DistanceMeters).ToList();
    }

    /// <summary>
    /// Checks if an avatar has entered a specific trigger's activation radius.
    /// </summary>
    public static bool IsAvatarWithinSagaTrigger(
        double avatarLatitude,
        double avatarLongitude,
        SagaArc sagaArc,
        SagaTrigger sagaTrigger)
    {
        var distance = TriggerProximityChecker.CalculateDistance(
            sagaArc.LongitudeX, sagaArc.LatitudeZ,
            avatarLongitude, avatarLatitude);

        return distance <= sagaTrigger.EnterRadius;
    }

    /// <summary>
    /// Determines which specific triggers are active for a Saga based on avatar position.
    /// Returns triggers sorted by radius (outermost first) with indication of which are triggered.
    /// </summary>
    public static List<SagaTriggerActivation> GetActivatedSagaTriggersForSaga(
        double avatarLatitude,
        double avatarLongitude,
        SagaArc sagaArc,
        List<SagaTrigger> sagaTriggers)
    {
        var results = new List<SagaTriggerActivation>();

        foreach (var sagaTrigger in sagaTriggers.OrderByDescending(t => t.EnterRadius))
        {
            var distance = TriggerProximityChecker.CalculateDistance(
                sagaArc.LongitudeX, sagaArc.LatitudeZ,
                avatarLongitude, avatarLatitude);

            var exitRadius = TriggerProximityChecker.GetExitRadius(sagaTrigger.EnterRadius);
            var isWithinEnterRadius = distance <= sagaTrigger.EnterRadius;
            var isWithinExitRadius = distance <= exitRadius;

            results.Add(new SagaTriggerActivation
            {
                SagaTrigger = sagaTrigger,
                DistanceMeters = distance,
                IsWithinEnterRadius = isWithinEnterRadius,
                IsWithinExitRadius = isWithinExitRadius
            });
        }

        return results;
    }

    /// <summary>
    /// Finds the innermost trigger that contains the specified model coordinates.
    /// Uses model space coordinates with HorizontalScale applied for accurate hit detection.
    /// </summary>
    /// <param name="modelX">Avatar X position in model/world coordinates</param>
    /// <param name="modelZ">Avatar Z position in model/world coordinates</param>
    /// <param name="world">World containing Saga data</param>
    /// <returns>The innermost activated trigger, or null if no triggers are active</returns>
    public static ActivatedSagaTriggerResult? FindInnermostSagaTriggerAtModelPosition(
        double modelX,
        double modelZ,
        World world)
    {
        if (world.Gameplay.SagaArcs == null)
            return null;

        // Get horizontal scale for model space calculations
        var horizontalScale = world.IsProcedural ? 1.0 : world.WorldConfiguration.HeightMapSettings.HorizontalScale;

        ActivatedSagaTriggerResult? innermostTrigger = null;
        var smallestRadius = double.MaxValue;

        foreach (var saga in world.Gameplay.SagaArcs)
        {
            // Get pre-expanded triggers from world lookup
            if (!world.SagaTriggersLookup.TryGetValue(saga.RefName, out var triggers) || !triggers.Any())
                continue;

            // Convert Saga GPS to model coordinates
            var sagaModelX = CoordinateConverter.LongitudeToModelX(saga.LongitudeX, world);
            var sagaModelZ = CoordinateConverter.LatitudeToModelZ(saga.LatitudeZ, world);

            // Check all triggers for this Saga
            foreach (var trigger in triggers)
            {
                // Scale trigger radius for model space (model coordinates have HorizontalScale already applied)
                var scaledEnterRadius = trigger.EnterRadius * horizontalScale;

                // Check if point is within this trigger
                var isWithin = TriggerProximityChecker.IsWithinTriggerRadiusSquared(
                    sagaModelX, sagaModelZ,
                    scaledEnterRadius,
                    modelX, modelZ);

                // Keep track of smallest (innermost) trigger
                if (isWithin && scaledEnterRadius < smallestRadius)
                {
                    smallestRadius = scaledEnterRadius;
                    innermostTrigger = new ActivatedSagaTriggerResult
                    {
                        SagaArc = saga,
                        SagaTrigger = trigger,
                        ScaledEnterRadius = scaledEnterRadius,
                        SagaModelX = sagaModelX,
                        SagaModelZ = sagaModelZ
                    };
                }
            }
        }

        return innermostTrigger;
    }

    /// <summary>
    /// Queries ALL possible interactions at a position: proximity triggers, features, and spawned characters.
    /// Returns results sorted by priority: Character > Feature > Trigger.
    /// This is the comprehensive query the UI should use for "what would happen if I clicked here?"
    /// </summary>
    /// <param name="modelX">Position X in model coordinates</param>
    /// <param name="modelZ">Position Z in model coordinates</param>
    /// <param name="avatar">Avatar for availability checking</param>
    /// <param name="world">World data</param>
    /// <param name="worldRepository">Repository for checking character/feature state (optional)</param>
    /// <returns>All interactions at this position, sorted by priority and distance</returns>
    public static List<SagaInteraction> QueryAllInteractionsAtPosition(
        double modelX,
        double modelZ,
        AvatarBase? avatar,
        World world,
        IWorldStateRepository? worldRepository = null)
    {
        var interactions = new List<SagaInteraction>();

        if (world.Gameplay.SagaArcs == null)
            return interactions;

        // Get proper scale factors for model-to-meters conversion (X and Z have different scales due to latitude correction)
        var scaleX = world.IsProcedural ? 1.0 : world.HeightMapLongitudeScale;
        var scaleZ = world.IsProcedural ? 1.0 : world.HeightMapLatitudeScale;
        var horizontalScale = world.IsProcedural ? 1.0 : world.WorldConfiguration.HeightMapSettings.HorizontalScale;

        const double FEATURE_RADIUS_METERS = 5.0; // Hardcoded proximity for features
        const double CHARACTER_RADIUS_METERS = 5.0; // Hardcoded proximity for spawned characters

        foreach (var saga in world.Gameplay.SagaArcs)
        {
            // Convert Saga GPS to model coordinates
            var sagaModelX = CoordinateConverter.LongitudeToModelX(saga.LongitudeX, world);
            var sagaModelZ = CoordinateConverter.LatitudeToModelZ(saga.LatitudeZ, world);

            // Calculate distance from position to saga center
            // CRITICAL: Must convert X and Z separately due to latitude correction factor
            var deltaModelX = modelX - sagaModelX;
            var deltaModelZ = modelZ - sagaModelZ;
            var deltaMetersX = deltaModelX / scaleX;  // Convert model X to meters using longitude scale
            var deltaMetersZ = deltaModelZ / scaleZ;  // Convert model Z to meters using latitude scale
            var distanceToCenter = Math.Sqrt(
                Math.Pow(deltaMetersX, 2) +
                Math.Pow(deltaMetersZ, 2)); // Euclidean distance in meters

            // 1. CHECK FEATURE (at saga center, 5m radius)
            if (distanceToCenter <= FEATURE_RADIUS_METERS)
            {
                // Check if saga has a feature
                if (!string.IsNullOrEmpty(saga.SagaFeatureRef))
                {
                    var featureStatus = DetermineFeatureStatus(saga, avatar, world, worldRepository);
                    interactions.Add(new SagaInteraction
                    {
                        Type = SagaInteractionType.Feature,
                        SagaRef = saga.RefName,
                        EntityRef = saga.SagaFeatureRef,
                        DistanceMeters = distanceToCenter,
                        Status = featureStatus,
                        Priority = 2 // Feature = medium priority
                    });
                }
            }

            // 2. CHECK PROXIMITY TRIGGERS
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
                    var triggerStatus = DetermineSagaTriggerStatus(trigger, avatar);
                    interactions.Add(new SagaInteraction
                    {
                        Type = SagaInteractionType.SagaTrigger,
                        SagaRef = saga.RefName,
                        EntityRef = trigger.RefName,
                        DistanceMeters = distanceToCenter, // Use saga center distance for now
                        Status = triggerStatus,
                        SagaTriggerRef = trigger.RefName,
                        Priority = 3 // Trigger = lowest priority
                    });

                    // 3. CHECK SPAWNED CHARACTERS (at trigger, 5m radius)
                    // TODO: Query worldRepository for spawned characters at this trigger
                    // For now, we don't have character position data, so skip
                    // When character positions are available, add CHARACTER interactions here
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
    /// Determines the status of a feature (available, locked, complete).
    /// </summary>
    private static InteractionStatus DetermineFeatureStatus(
        SagaArc sagaArc,
        AvatarBase? avatar,
        World world,
        IWorldStateRepository? worldRepository)
    {
        // Check for completion first (if feature has been interacted with)
        if (worldRepository != null && avatar != null)
        {
            try
            {
                var avatarId = avatar.AvatarId.ToString();
                var sagaInstance = worldRepository.GetSagaInstanceAsync(avatarId, sagaArc.RefName).GetAwaiter().GetResult();

                if (sagaInstance != null && sagaInstance.Transactions != null)
                {
                    // Check transaction log for EntityInteracted or LandmarkDiscovered
                    foreach (var transaction in sagaInstance.Transactions)
                    {
                        if ((transaction.Type == SagaTransactionType.EntityInteracted ||
                             transaction.Type == SagaTransactionType.LandmarkDiscovered) &&
                            transaction.Data != null &&
                            transaction.Data.ContainsKey("EntityRef") &&
                            transaction.Data["EntityRef"] == sagaArc.SagaFeatureRef)
                        {
                            return InteractionStatus.Complete;
                        }
                    }
                }
            }
            catch
            {
                // If repository access fails, continue with other checks
            }
        }

        // Check if saga requires quest tokens (locked if missing)
        if (!string.IsNullOrEmpty(sagaArc.SagaFeatureRef))
        {
            var feature = world.TryGetSagaFeatureByRefName(sagaArc.SagaFeatureRef);
            if (feature?.Interactable?.RequiresQuestTokenRef != null)
            {
                if (avatar == null || !HasAllQuestTokens(feature.Interactable.RequiresQuestTokenRef, avatar))
                    return InteractionStatus.Locked;
            }
        }

        return InteractionStatus.Available;
    }

    /// <summary>
    /// Determines the status of a proximity trigger.
    /// </summary>
    private static InteractionStatus DetermineSagaTriggerStatus(SagaTrigger sagaTrigger, AvatarBase? avatar)
    {
        if (avatar == null)
            return InteractionStatus.Available; // No avatar = show as available

        // Check quest token requirements
        if (!TriggerAvailabilityChecker.CanActivate(sagaTrigger, avatar))
            return InteractionStatus.Locked;

        // TODO: Check if all spawned characters are defeated = Complete status

        return InteractionStatus.Available;
    }

    /// <summary>
    /// Helper to check if avatar has all required quest tokens.
    /// </summary>
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
}

/// <summary>
/// Type of interaction at a position in a Saga.
/// </summary>
public enum SagaInteractionType
{
    /// <summary>Proximity trigger that spawns characters or activates content</summary>
    SagaTrigger,

    /// <summary>Feature at saga center (Landmark/Structure/QuestSignpost)</summary>
    Feature,

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

    /// <summary>Priority for sorting (1=highest, Character > Feature > Trigger)</summary>
    public required int Priority { get; init; }

    /// <summary>Trigger RefName (if this is a Trigger interaction)</summary>
    public string? SagaTriggerRef { get; init; }

    /// <summary>Character RefName (if this is a Character interaction)</summary>
    public string? CharacterRef { get; init; }
}

/// <summary>
/// Result of finding an activated trigger at a position.
/// </summary>
public class ActivatedSagaTriggerResult
{
    /// <summary>The Saga containing this trigger</summary>
    public required SagaArc SagaArc { get; init; }

    /// <summary>The activated trigger</summary>
    public required SagaTrigger SagaTrigger { get; init; }

    /// <summary>Scaled enter radius in model space</summary>
    public required double ScaledEnterRadius { get; init; }

    /// <summary>Saga center X in model coordinates</summary>
    public required double SagaModelX { get; init; }

    /// <summary>Saga center Z in model coordinates</summary>
    public required double SagaModelZ { get; init; }
}

/// <summary>
/// Represents a Saga that is active (within preview range) for the avatar.
/// Contains all information needed to render and interact with the Saga.
/// </summary>
public class ActiveSaga
{
    /// <summary>The Saga entity</summary>
    public required SagaArc SagaArc { get; init; }

    /// <summary>Pre-expanded triggers for this Saga (sorted outermost → innermost)</summary>
    public required List<SagaTrigger> SagaTriggers { get; init; }

    /// <summary>Distance from avatar to Saga center in meters</summary>
    public required double DistanceMeters { get; init; }

    /// <summary>Distance at which Saga enters preview (outermost trigger radius * 1.5)</summary>
    public required double PreviewEnterDistance { get; init; }

    /// <summary>Distance at which Saga exits preview (with hysteresis)</summary>
    public required double PreviewExitDistance { get; init; }

    /// <summary>True if avatar is within preview range</summary>
    public required bool IsWithinPreviewRange { get; init; }
}

/// <summary>
/// Represents the activation state of a specific trigger.
/// </summary>
public class SagaTriggerActivation
{
    /// <summary>The trigger entity</summary>
    public required SagaTrigger SagaTrigger { get; init; }

    /// <summary>Distance from avatar to trigger center in meters</summary>
    public required double DistanceMeters { get; init; }

    /// <summary>True if avatar is within enter radius (trigger activates)</summary>
    public required bool IsWithinEnterRadius { get; init; }

    /// <summary>True if avatar is within exit radius (hysteresis zone)</summary>
    public required bool IsWithinExitRadius { get; init; }
}
