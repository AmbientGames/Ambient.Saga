namespace Ambient.SagaEngine.Domain.Rpg.Sagas;

/// <summary>
/// Centralizes all trigger proximity/distance checking logic.
/// Provides clear, testable methods for determining if a player is within trigger range.
///
/// Radius Model:
/// - EnterRadius: Distance at which trigger activates (required)
/// - ExitRadius: Distance at which trigger deactivates (optional, defaults to EnterRadius + 10m for hysteresis)
/// </summary>
public static class TriggerProximityChecker
{
    /// <summary>
    /// Default hysteresis margin added to EnterRadius when ExitRadius is not specified.
    /// Prevents flickering when player is near the trigger boundary.
    /// </summary>
    public const float DefaultHysteresisMargin = 10.0f;

    /// <summary>
    /// Gets the exit radius for a trigger.
    /// Always returns EnterRadius + hysteresis margin for consistent behavior.
    /// </summary>
    public static float GetExitRadius(float enterRadius)
    {
        return enterRadius + DefaultHysteresisMargin;
    }
    /// <summary>
    /// Checks if a point is within the trigger's activation radius.
    /// </summary>
    /// <param name="triggerX">Trigger center X coordinate</param>
    /// <param name="triggerY">Trigger center Y coordinate</param>
    /// <param name="enterRadius">Trigger activation radius</param>
    /// <param name="pointX">Point to check X coordinate</param>
    /// <param name="pointY">Point to check Y coordinate</param>
    /// <returns>True if the point is within the trigger's activation radius</returns>
    public static bool IsWithinTriggerRadius(
        double triggerX,
        double triggerY,
        double enterRadius,
        double pointX,
        double pointY)
    {
        var distance = CalculateDistance(triggerX, triggerY, pointX, pointY);
        return distance <= enterRadius;
    }

    /// <summary>
    /// Calculates the Euclidean distance between two points.
    /// </summary>
    /// <param name="x1">First point X coordinate</param>
    /// <param name="y1">First point Y coordinate</param>
    /// <param name="x2">Second point X coordinate</param>
    /// <param name="y2">Second point Y coordinate</param>
    /// <returns>Distance between the two points</returns>
    public static double CalculateDistance(double x1, double y1, double x2, double y2)
    {
        var dx = x2 - x1;
        var dy = y2 - y1;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>
    /// Optimized distance check using squared distances (avoids expensive sqrt).
    /// Use this when you only need to compare distances, not calculate exact values.
    /// </summary>
    public static bool IsWithinTriggerRadiusSquared(
        double triggerX,
        double triggerY,
        double enterRadius,
        double pointX,
        double pointY)
    {
        var dx = pointX - triggerX;
        var dy = pointY - triggerY;
        var distanceSquared = dx * dx + dy * dy;
        var radiusSquared = enterRadius * enterRadius;
        return distanceSquared <= radiusSquared;
    }

    /// <summary>
    /// Gets the distance from a point to the edge of the trigger's radius.
    /// Positive value = outside trigger, negative value = inside trigger.
    /// </summary>
    public static double GetDistanceFromTriggerEdge(
        double triggerX,
        double triggerY,
        double enterRadius,
        double pointX,
        double pointY)
    {
        var distance = CalculateDistance(triggerX, triggerY, pointX, pointY);
        return distance - enterRadius;
    }
}
