using Ambient.Saga.Engine.Domain.Rpg.Sagas;

namespace Ambient.Saga.Engine.Tests;

/// <summary>
/// Unit tests for TriggerProximityChecker which handles trigger radius calculations and distance checking.
/// </summary>
public class TriggerProximityCheckerTests
{
    [Fact]
    public void DefaultHysteresisMargin_IsSetTo10Meters()
    {
        // Assert
        Assert.Equal(10.0f, TriggerProximityChecker.DefaultHysteresisMargin);
    }

    [Theory]
    [InlineData(10.0f, 20.0f)]
    [InlineData(25.0f, 35.0f)]
    [InlineData(45.0f, 55.0f)]
    [InlineData(0.0f, 10.0f)]
    public void GetExitRadius_AddsDefaultHysteresisMargin(float enterRadius, float expectedExitRadius)
    {
        // Act
        var exitRadius = TriggerProximityChecker.GetExitRadius(enterRadius);

        // Assert
        Assert.Equal(expectedExitRadius, exitRadius);
    }

    [Fact]
    public void CalculateDistance_BetweenSamePoint_ReturnsZero()
    {
        // Arrange
        var x = 100.0;
        var y = 200.0;

        // Act
        var distance = TriggerProximityChecker.CalculateDistance(x, y, x, y);

        // Assert
        Assert.Equal(0.0, distance);
    }

    [Theory]
    [InlineData(0.0, 0.0, 3.0, 4.0, 5.0)] // 3-4-5 right triangle
    [InlineData(0.0, 0.0, 1.0, 0.0, 1.0)] // Horizontal distance
    [InlineData(0.0, 0.0, 0.0, 1.0, 1.0)] // Vertical distance
    [InlineData(10.0, 20.0, 13.0, 24.0, 5.0)] // 3-4-5 triangle offset
    public void CalculateDistance_WithVariousCoordinates_ReturnsCorrectEuclideanDistance(
        double x1, double y1, double x2, double y2, double expectedDistance)
    {
        // Act
        var distance = TriggerProximityChecker.CalculateDistance(x1, y1, x2, y2);

        // Assert
        Assert.Equal(expectedDistance, distance, precision: 10);
    }

    [Fact]
    public void IsWithinTriggerRadius_PointOnRadius_ReturnsTrue()
    {
        // Arrange
        var triggerX = 0.0;
        var triggerY = 0.0;
        var enterRadius = 10.0;
        var pointX = 10.0;
        var pointY = 0.0;

        // Act
        var isWithin = TriggerProximityChecker.IsWithinTriggerRadius(
            triggerX, triggerY, enterRadius, pointX, pointY);

        // Assert
        Assert.True(isWithin);
    }

    [Fact]
    public void IsWithinTriggerRadius_PointInsideRadius_ReturnsTrue()
    {
        // Arrange
        var triggerX = 0.0;
        var triggerY = 0.0;
        var enterRadius = 10.0;
        var pointX = 5.0;
        var pointY = 5.0; // Distance = ~7.07, within 10.0

        // Act
        var isWithin = TriggerProximityChecker.IsWithinTriggerRadius(
            triggerX, triggerY, enterRadius, pointX, pointY);

        // Assert
        Assert.True(isWithin);
    }

    [Fact]
    public void IsWithinTriggerRadius_PointOutsideRadius_ReturnsFalse()
    {
        // Arrange
        var triggerX = 0.0;
        var triggerY = 0.0;
        var enterRadius = 10.0;
        var pointX = 8.0;
        var pointY = 8.0; // Distance = ~11.31, outside 10.0

        // Act
        var isWithin = TriggerProximityChecker.IsWithinTriggerRadius(
            triggerX, triggerY, enterRadius, pointX, pointY);

        // Assert
        Assert.False(isWithin);
    }

    [Theory]
    [InlineData(0.0, 0.0, 10.0, 0.0, 0.0, true)]   // Center point
    [InlineData(0.0, 0.0, 10.0, 6.0, 8.0, true)]   // Distance = 10.0, exactly on radius
    [InlineData(0.0, 0.0, 10.0, 7.0, 7.2, false)]  // Distance = ~10.04, just outside
    [InlineData(100.0, 100.0, 25.0, 120.0, 100.0, true)]  // Offset center, horizontal
    [InlineData(100.0, 100.0, 25.0, 100.0, 126.0, false)] // Offset center, vertical outside
    public void IsWithinTriggerRadius_VariousScenarios_ReturnsExpectedResult(
        double triggerX, double triggerY, double enterRadius,
        double pointX, double pointY, bool expectedResult)
    {
        // Act
        var isWithin = TriggerProximityChecker.IsWithinTriggerRadius(
            triggerX, triggerY, enterRadius, pointX, pointY);

        // Assert
        Assert.Equal(expectedResult, isWithin);
    }

    [Fact]
    public void IsWithinTriggerRadiusSquared_PointInsideRadius_ReturnsTrue()
    {
        // Arrange
        var triggerX = 0.0;
        var triggerY = 0.0;
        var enterRadius = 10.0; // Function squares this internally
        var pointX = 5.0;
        var pointY = 5.0; // Distance squared = 50, radius squared = 100

        // Act
        var isWithin = TriggerProximityChecker.IsWithinTriggerRadiusSquared(
            triggerX, triggerY, enterRadius, pointX, pointY);

        // Assert
        Assert.True(isWithin);
    }

    [Fact]
    public void IsWithinTriggerRadiusSquared_PointOutsideRadius_ReturnsFalse()
    {
        // Arrange
        var triggerX = 0.0;
        var triggerY = 0.0;
        var enterRadius = 10.0; // Function squares this internally
        var pointX = 8.0;
        var pointY = 8.0; // Distance squared = 128, radius squared = 100

        // Act
        var isWithin = TriggerProximityChecker.IsWithinTriggerRadiusSquared(
            triggerX, triggerY, enterRadius, pointX, pointY);

        // Assert
        Assert.False(isWithin);
    }

    [Theory]
    [InlineData(0.0, 0.0, 10.0, 10.0, 0.0, 0.0)]    // On radius edge (distance=10, radius=10, diff=0)
    [InlineData(0.0, 0.0, 10.0, 5.0, 0.0, -5.0)]    // Inside by 5m (distance=5, radius=10, diff=-5)
    [InlineData(0.0, 0.0, 10.0, 15.0, 0.0, 5.0)]    // Outside by 5m (distance=15, radius=10, diff=5)
    [InlineData(0.0, 0.0, 10.0, 0.0, 0.0, -10.0)]   // Center (distance=0, radius=10, diff=-10)
    public void GetDistanceFromTriggerEdge_VariousPositions_ReturnsCorrectDistance(
        double triggerX, double triggerY, double enterRadius,
        double pointX, double pointY, double expectedDistance)
    {
        // Act
        var distance = TriggerProximityChecker.GetDistanceFromTriggerEdge(
            triggerX, triggerY, enterRadius, pointX, pointY);

        // Assert
        Assert.Equal(expectedDistance, distance, precision: 10);
    }

    [Fact]
    public void GetDistanceFromTriggerEdge_NegativeValue_MeansInsideRadius()
    {
        // Arrange - Point well inside the trigger radius
        var triggerX = 0.0;
        var triggerY = 0.0;
        var enterRadius = 100.0;
        var pointX = 0.0;
        var pointY = 0.0; // At center, distance = 0

        // Act
        var distance = TriggerProximityChecker.GetDistanceFromTriggerEdge(
            triggerX, triggerY, enterRadius, pointX, pointY);

        // Assert
        // distance - enterRadius = 0 - 100 = -100 (negative means inside)
        Assert.True(distance < 0, "Distance from edge should be negative when inside radius");
        Assert.Equal(-100.0, distance); // At center, result = distance - radius = 0 - 100
    }

    [Fact]
    public void GetDistanceFromTriggerEdge_PositiveValue_MeansOutsideRadius()
    {
        // Arrange - Point outside the trigger radius
        var triggerX = 0.0;
        var triggerY = 0.0;
        var enterRadius = 10.0;
        var pointX = 20.0;
        var pointY = 0.0; // 20m from center, 10m outside radius

        // Act
        var distance = TriggerProximityChecker.GetDistanceFromTriggerEdge(
            triggerX, triggerY, enterRadius, pointX, pointY);

        // Assert
        // distance - enterRadius = 20 - 10 = 10 (positive means outside)
        Assert.True(distance > 0, "Distance from edge should be positive when outside radius");
        Assert.Equal(10.0, distance, precision: 10);
    }

    [Theory]
    [InlineData(10.0f)]
    [InlineData(25.0f)]
    [InlineData(45.0f)]
    public void Hysteresis_PreventsTriggerFlickering(float enterRadius)
    {
        // Arrange
        var triggerX = 0.0;
        var triggerY = 0.0;
        var exitRadius = TriggerProximityChecker.GetExitRadius(enterRadius);

        // Point just inside enter radius
        var pointX = enterRadius - 1.0;
        var pointY = 0.0;

        // Act & Assert - Point should be within enter radius
        var withinEnter = TriggerProximityChecker.IsWithinTriggerRadius(
            triggerX, triggerY, enterRadius, pointX, pointY);
        Assert.True(withinEnter, "Point should trigger on enter");

        // Move point slightly outward (between enter and exit radius)
        pointX = enterRadius + 2.0; // 2m outside enter radius, but inside exit radius

        var withinExit = TriggerProximityChecker.IsWithinTriggerRadius(
            triggerX, triggerY, exitRadius, pointX, pointY);
        Assert.True(withinExit, "Point should still be within exit radius (hysteresis)");

        var outsideEnter = !TriggerProximityChecker.IsWithinTriggerRadius(
            triggerX, triggerY, enterRadius, pointX, pointY);
        Assert.True(outsideEnter, "Point should be outside enter radius");
    }
}
