namespace Backend;

/// <summary>Brussels wall calendar boundaries for Smappee range semantics.</summary>
public static class BrusselsTime
{
    private static TimeZoneInfo BrusselsTz
    {
        get
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById("Europe/Brussels");
            }
            catch (TimeZoneNotFoundException)
            {
                return TimeZoneInfo.FindSystemTimeZoneById("W. Europe Standard Time");
            }
        }
    }

    /// <summary>Inclusive start and exclusive end in Unix milliseconds for a Brussels calendar month.</summary>
    public static (long StartMsExclusiveUtc, long EndMsExclusiveUtc) MonthRangeUnixMilliseconds(int year, int month)
    {
        var tz = BrusselsTz;
        var startBrussels = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Unspecified);
        var startUtc = TimeZoneInfo.ConvertTimeToUtc(startBrussels, tz);
        var endUtc = TimeZoneInfo.ConvertTimeToUtc(startBrussels.AddMonths(1), tz);
        var startMs = new DateTimeOffset(startUtc).ToUnixTimeMilliseconds();
        var endMs = new DateTimeOffset(endUtc).ToUnixTimeMilliseconds();
        return (startMs, endMs);
    }

    public static (int Year, int Month) BrusselsYearMonthFromUnixMilliseconds(long unixMs)
    {
        var tz = BrusselsTz;
        var utc = DateTimeOffset.FromUnixTimeMilliseconds(unixMs).UtcDateTime;
        var brussels = TimeZoneInfo.ConvertTimeFromUtc(utc, tz);
        return (brussels.Year, brussels.Month);
    }

    /// <summary>UTC instant for midnight at the start of a calendar day in Europe/Brussels.</summary>
    public static long StartOfCalendarDayUnixMilliseconds(DateOnly date)
    {
        var tz = BrusselsTz;
        var local = new DateTime(date.Year, date.Month, date.Day, 0, 0, 0, DateTimeKind.Unspecified);
        var utc = TimeZoneInfo.ConvertTimeToUtc(local, tz);
        return new DateTimeOffset(utc).ToUnixTimeMilliseconds();
    }

    public static bool IsInBrusselsMonth(long fromTimestampMs, int year, int month)
    {
        var (y, m) = BrusselsYearMonthFromUnixMilliseconds(fromTimestampMs);
        return y == year && m == month;
    }
}
