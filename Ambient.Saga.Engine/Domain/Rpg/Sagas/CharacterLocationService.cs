using Ambient.Domain.DefinitionExtensions;
using Ambient.Domain.GameLogic.Gameplay.WorldManagers;
using Ambient.Saga.Engine.Domain.Rpg.Sagas.TransactionLog;

namespace Ambient.Saga.Engine.Domain.Rpg.Sagas;

/// <summary>
/// Domain service for character spawning and location logic.
/// Handles model coordinate-based positioning with proper scale application.
/// Server and client use this same logic for deterministic character placement.
/// </summary>
public static class CharacterLocationService
{
    /// <summary>
    /// Calculates spawn positions in a circle around a center point.
    /// Returns GPS coordinates (latitude/longitude) for each spawn position.
    /// </summary>
    /// <param name="centerLatitude">Center latitude in degrees</param>
    /// <param name="centerLongitude">Center longitude in degrees</param>
    /// <param name="radiusMeters">Radius in meters from center</param>
    /// <param name="count">Number of spawn positions to calculate</param>
    /// <param name="world">World for coordinate conversion</param>
    /// <returns>List of (latitude, longitude) tuples for spawn positions</returns>
    public static List<(double latitude, double longitude)> CalculateCircularSpawnPositions(
        double centerLatitude,
        double centerLongitude,
        double radiusMeters,
        int count,
        World world)
    {
        var positions = new List<(double, double)>();

        if (count <= 0)
            return positions;

        // Convert center GPS to model coordinates
        var centerModelX = CoordinateConverter.LongitudeToModelX(centerLongitude, world);
        var centerModelZ = CoordinateConverter.LatitudeToModelZ(centerLatitude, world);

        // Get horizontal scale for proper radius scaling
        var horizontalScale = world.IsProcedural ? 1.0 : world.WorldConfiguration.HeightMapSettings.HorizontalScale;
        var scaledRadius = radiusMeters * horizontalScale;

        // Calculate positions evenly distributed in a circle
        var angleStep = 2.0 * Math.PI / count;

        for (var i = 0; i < count; i++)
        {
            var angle = i * angleStep;
            var offsetX = scaledRadius * Math.Sin(angle);
            var offsetZ = scaledRadius * Math.Cos(angle);

            var spawnModelX = centerModelX + offsetX;
            var spawnModelZ = centerModelZ + offsetZ;

            // Convert back to GPS
            var spawnLatitude = CoordinateConverter.ModelZToLatitude(spawnModelZ, world);
            var spawnLongitude = CoordinateConverter.ModelXToLongitude(spawnModelX, world);

            positions.Add((spawnLatitude, spawnLongitude));
        }

        return positions;
    }

    /// <summary>
    /// Calculates the distance between two GPS positions using model coordinates.
    /// Properly accounts for HorizontalScale.
    /// </summary>
    /// <param name="latitude1">First position latitude</param>
    /// <param name="longitude1">First position longitude</param>
    /// <param name="latitude2">Second position latitude</param>
    /// <param name="longitude2">Second position longitude</param>
    /// <param name="world">World for coordinate conversion</param>
    /// <returns>Distance in meters (unscaled)</returns>
    public static double GetDistanceBetweenPositions(
        double latitude1,
        double longitude1,
        double latitude2,
        double longitude2,
        World world)
    {
        // Convert both positions to model coordinates
        var modelX1 = CoordinateConverter.LongitudeToModelX(longitude1, world);
        var modelZ1 = CoordinateConverter.LatitudeToModelZ(latitude1, world);
        var modelX2 = CoordinateConverter.LongitudeToModelX(longitude2, world);
        var modelZ2 = CoordinateConverter.LatitudeToModelZ(latitude2, world);

        // Calculate distance in model space
        var dx = modelX2 - modelX1;
        var dz = modelZ2 - modelZ1;
        var scaledDistance = Math.Sqrt(dx * dx + dz * dz);

        // Convert back to real-world meters
        var horizontalScale = world.IsProcedural ? 1.0 : world.WorldConfiguration.HeightMapSettings.HorizontalScale;
        return scaledDistance / horizontalScale;
    }

    /// <summary>
    /// Checks if a character is already spawned near a given position.
    /// </summary>
    /// <param name="characterRef">Character template reference to check</param>
    /// <param name="centerLatitude">Center latitude in degrees</param>
    /// <param name="centerLongitude">Center longitude in degrees</param>
    /// <param name="radiusMeters">Search radius in meters</param>
    /// <param name="world">World for coordinate conversion</param>
    /// <param name="spawnedCharacters">Collection of spawned character instances</param>
    /// <returns>True if a matching character is found within radius</returns>
    public static bool IsCharacterSpawnedNearby(
        string characterRef,
        double centerLatitude,
        double centerLongitude,
        double radiusMeters,
        World world,
        IEnumerable<CharacterState> spawnedCharacters)
    {
        foreach (var spawned in spawnedCharacters)
        {
            if (spawned.CharacterRef != characterRef)
                continue;

            var distance = GetDistanceBetweenPositions(
                centerLatitude, centerLongitude,
                spawned.CurrentLatitudeZ, spawned.CurrentLongitudeX,
                world);

            // Within 2x radius = considered "nearby" to prevent duplicate spawns
            if (distance <= radiusMeters * 2)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Gets the distance between a character instance and a GPS position.
    /// </summary>
    /// <param name="character">Character instance</param>
    /// <param name="targetLatitude">Target latitude</param>
    /// <param name="targetLongitude">Target longitude</param>
    /// <param name="world">World for coordinate conversion</param>
    /// <returns>Distance in meters</returns>
    public static double GetDistanceToCharacter(
        CharacterState character,
        double targetLatitude,
        double targetLongitude,
        World world)
    {
        return GetDistanceBetweenPositions(
            character.CurrentLatitudeZ,
            character.CurrentLongitudeX,
            targetLatitude,
            targetLongitude,
            world);
    }

    /// <summary>
    /// Checks if a point in model coordinates is within a character's hit radius.
    /// Properly applies HorizontalScale to the radius for accurate hit detection.
    /// </summary>
    /// <param name="characterModelX">Character X in model coordinates</param>
    /// <param name="characterModelZ">Character Z in model coordinates</param>
    /// <param name="pointModelX">Test point X in model coordinates</param>
    /// <param name="pointModelZ">Test point Z in model coordinates</param>
    /// <param name="radiusMeters">Hit radius in real-world meters</param>
    /// <param name="world">World for scale information</param>
    /// <returns>True if point is within radius of character</returns>
    public static bool IsPointNearCharacterInModelSpace(
        double characterModelX,
        double characterModelZ,
        double pointModelX,
        double pointModelZ,
        double radiusMeters,
        World world)
    {
        // Get horizontal scale for proper radius scaling
        var horizontalScale = world.IsProcedural ? 1.0 : world.WorldConfiguration.HeightMapSettings.HorizontalScale;
        var scaledRadius = radiusMeters * horizontalScale;

        // Use squared distance for efficiency
        return TriggerProximityChecker.IsWithinTriggerRadiusSquared(
            characterModelX, characterModelZ,
            scaledRadius,
            pointModelX, pointModelZ);
    }
}
