namespace Ambient.Domain.Enums;

/// <summary>
/// Provides constants for time calculations and conversions.
/// </summary>
public static class TimeConstants
{
    /// <summary>
    /// The number of seconds in one hour.
    /// </summary>
    public const int SecondsInHour = 60;
    
    /// <summary>
    /// The number of days in one month.
    /// </summary>
    public const int DaysInMonth = 30;
    
    /// <summary>
    /// The number of months in one year.
    /// </summary>
    public const int MonthsInYear = 12;
    
    /// <summary>
    /// The number of days in one year.
    /// </summary>
    public const int DaysInYear = DaysInMonth * MonthsInYear;
    
    /// <summary>
    /// The number of hours in one day.
    /// </summary>
    public const int HoursInDay = 24;
}