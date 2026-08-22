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

    /// <summary>Monthly invoices bill completed sessions in the Brussels month they ended.</summary>
    public static bool IsEndedInBrusselsMonth(long? toTimestampMs, int year, int month)
    {
        if (!toTimestampMs.HasValue)
            return false;

        var (y, m) = BrusselsYearMonthFromUnixMilliseconds(toTimestampMs.Value);
        return y == year && m == month;
    }

    /// <summary>Completed session ended within [startMs, endMs) in Brussels wall time.</summary>
    public static bool EndedInUnixMillisecondsRange(long? toTimestampMs, long startMs, long endMsExclusive) =>
        toTimestampMs.HasValue && toTimestampMs.Value >= startMs && toTimestampMs.Value < endMsExclusive;

    public static (int Year, int Month) CurrentBrusselsYearMonth()
    {
        var brussels = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, BrusselsTz);
        return (brussels.Year, brussels.Month);
    }

    /// <summary>True while the Brussels calendar month is still in progress (not yet billable).</summary>
    public static bool IsCurrentBrusselsMonth(int year, int month)
    {
        var (y, m) = CurrentBrusselsYearMonth();
        return y == year && m == month;
    }

    /// <summary>Assumes <paramref name="unixMs"/> is <strong>milliseconds</strong> (EF / Smappee convention).</summary>
    public static string DescribeUnixMilliseconds(long unixMs)
    {
        var utc = DateTimeOffset.FromUnixTimeMilliseconds(unixMs).UtcDateTime;
        var brussels = TimeZoneInfo.ConvertTimeFromUtc(utc, BrusselsTz);
        return $"{unixMs} ms | UTC {utc:yyyy-MM-dd HH:mm:ss.fff}Z | Brussels {brussels:yyyy-MM-dd HH:mm:ss.fff}";
    }

    public static string DescribeUnixMilliseconds(long? unixMs) =>
        unixMs.HasValue ? DescribeUnixMilliseconds(unixMs.Value) : "null";

    /// <summary>
    /// Values under ~year 2001 in <em>ms</em> are suspicious; Smappee may send <strong>seconds</strong> or the field may be mis-parsed.
    /// </summary>
    public static bool LooksLikeUnixSecondsInsteadOfMilliseconds(long unixMs) =>
        unixMs is > 0 and < 10_000_000_000L;
}
