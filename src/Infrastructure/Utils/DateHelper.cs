namespace Subscrio.Core.Infrastructure.Utils;

/// <summary>
/// Standardized date utilities for consistent date handling across the application
/// </summary>
public static class DateHelper
{
    /// <summary>
    /// Get the current date/time in UTC
    /// This ensures all dates are consistent regardless of server timezone
    /// </summary>
    public static DateTime Now()
    {
        return DateTime.UtcNow;
    }

    /// <summary>
    /// Get the current date/time as ISO string
    /// Useful for logging and API responses
    /// </summary>
    public static string NowISO()
    {
        return DateTime.UtcNow.ToString("O");
    }

    /// <summary>
    /// Create a date from an ISO string
    /// Throws an error if the string is invalid
    /// </summary>
    public static DateTime FromISOString(string isoString)
    {
        if (!DateTime.TryParse(isoString, out var date))
        {
            throw new ArgumentException($"Invalid ISO date string: {isoString}");
        }
        return date;
    }

    /// <summary>
    /// Add days to a date
    /// </summary>
    public static DateTime AddDays(DateTime date, int days)
    {
        return date.AddDays(days);
    }

    /// <summary>
    /// Add months to a date
    /// </summary>
    public static DateTime AddMonths(DateTime date, int months)
    {
        return date.AddMonths(months);
    }

    /// <summary>
    /// Add years to a date
    /// </summary>
    public static DateTime AddYears(DateTime date, int years)
    {
        return date.AddYears(years);
    }

    /// <summary>
    /// Check if a date is in the past
    /// </summary>
    public static bool IsPast(DateTime date)
    {
        return date < Now();
    }

    /// <summary>
    /// Check if a date is in the future
    /// </summary>
    public static bool IsFuture(DateTime date)
    {
        return date > Now();
    }

    /// <summary>
    /// Check if a date is today
    /// </summary>
    public static bool IsToday(DateTime date)
    {
        var today = Now();
        return date.Date == today.Date;
    }

    /// <summary>
    /// Format date for display
    /// </summary>
    public static string FormatDate(DateTime date, string locale = "en-US")
    {
        return date.ToShortDateString();
    }

    /// <summary>
    /// Format date and time for display
    /// </summary>
    public static string FormatDateTime(DateTime date, string locale = "en-US")
    {
        return date.ToString();
    }
}


