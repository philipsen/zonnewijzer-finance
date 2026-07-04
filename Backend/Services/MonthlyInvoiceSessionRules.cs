namespace Backend.Services;

/// <summary>
/// Monthly invoices bill completed sessions in the Brussels month they ended.
/// Ongoing sessions are excluded until they finish (then billed in that end month).
/// </summary>
public static class MonthlyInvoiceSessionRules
{
    public static bool IsBillableInMonth(long? toTimestampMs, int year, int month)
    {
        if (!toTimestampMs.HasValue)
            return false;

        var (startMs, endMs) = BrusselsTime.MonthRangeUnixMilliseconds(year, month);
        return IsBillableInMonth(toTimestampMs, startMs, endMs);
    }

    public static bool IsBillableInMonth(long? toTimestampMs, long rangeStartMs, long rangeEndMsExclusive) =>
        toTimestampMs.HasValue
        && toTimestampMs.Value >= rangeStartMs
        && toTimestampMs.Value < rangeEndMsExclusive;
}
