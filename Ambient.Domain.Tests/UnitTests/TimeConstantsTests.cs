using Ambient.Domain.Enums;
using Xunit;

namespace Ambient.Domain.Tests.UnitTests;

/// <summary>
/// Unit tests for the TimeConstants class and its associated functionality.
/// </summary>
public class TimeConstantsTests
{
    [Fact]
    public void TimeConstants_ShouldHaveCorrectSecondsInHour()
    {
        // Assert
        Assert.Equal(60, TimeConstants.SecondsInHour);
    }

    [Fact]
    public void TimeConstants_ShouldHaveCorrectDaysInMonth()
    {
        // Assert
        Assert.Equal(30, TimeConstants.DaysInMonth);
    }

    [Fact]
    public void TimeConstants_ShouldHaveCorrectMonthsInYear()
    {
        // Assert
        Assert.Equal(12, TimeConstants.MonthsInYear);
    }

    [Fact]
    public void TimeConstants_ShouldHaveCorrectHoursInDay()
    {
        // Assert
        Assert.Equal(24, TimeConstants.HoursInDay);
    }

    [Fact]
    public void TimeConstants_DaysInYear_ShouldBeCalculatedCorrectly()
    {
        // Arrange
        var expectedDaysInYear = TimeConstants.DaysInMonth * TimeConstants.MonthsInYear;

        // Assert
        Assert.Equal(expectedDaysInYear, TimeConstants.DaysInYear);
        Assert.Equal(360, TimeConstants.DaysInYear); // 30 * 12 = 360
    }

    [Fact]
    public void TimeConstants_ShouldProvideConsistentTimeSystem()
    {
        // Assert - Verify the time system relationships
        Assert.True(TimeConstants.SecondsInHour > 0);
        Assert.True(TimeConstants.DaysInMonth > 0);
        Assert.True(TimeConstants.MonthsInYear > 0);
        Assert.True(TimeConstants.HoursInDay > 0);
        Assert.True(TimeConstants.DaysInYear > TimeConstants.DaysInMonth);
        Assert.True(TimeConstants.DaysInYear > TimeConstants.MonthsInYear);
    }

    [Fact]
    public void TimeConstants_ShouldSupportTimeCalculations()
    {
        // Act - Calculate total seconds in a day
        var secondsInDay = TimeConstants.HoursInDay * TimeConstants.SecondsInHour;
        
        // Assert
        Assert.Equal(1440, secondsInDay); // 24 * 60 = 1440 seconds
    }

    [Fact]
    public void TimeConstants_ShouldSupportYearCalculations()
    {
        // Act - Calculate total hours in a year
        var hoursInYear = TimeConstants.DaysInYear * TimeConstants.HoursInDay;
        
        // Assert
        Assert.Equal(8640, hoursInYear); // 360 * 24 = 8640 hours
    }
}
