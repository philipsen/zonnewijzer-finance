namespace Backend.Services;

/// <summary>One charging session from Smappee, mapped to an app user for combined invoicing.</summary>
public sealed class CombinedInvoiceSourceSession
{
    public int AppUserId { get; init; }
    public int ChargeCardId { get; init; }
    public long FromTimestampMs { get; init; }
    public long? ToTimestampMs { get; init; }
    public int StationId { get; init; }
    public string? SerialNumber { get; init; }
    public string? Connector { get; init; }
    public decimal ChargedKwh { get; init; }
}

public sealed class CombinedInvoiceSmappeeFetchResult
{
    public required IReadOnlyList<CombinedInvoiceSourceSession> Sessions { get; init; }
    public required IReadOnlyDictionary<int, IReadOnlyList<CombinedInvoiceSourceSession>> ByAppUserId { get; init; }
    public int SkippedUnmapped { get; init; }
    public int SkippedInvalid { get; init; }
}

public interface ISmappeeCombinedInvoiceSessionSource
{
    Task<CombinedInvoiceSmappeeFetchResult> FetchSessionsForRangeAsync(
        int startYear,
        int startMonth,
        int endYear,
        int endMonth,
        CancellationToken cancellationToken = default);
}

/// <summary>Combined invoices include only completed sessions that ended before the range end (e.g. before 1 June for Apr–May).</summary>
public static class CombinedInvoiceSessionRules
{
    public static bool StartedInRange(long fromTimestampMs, long rangeStartMs, long rangeEndMsExclusive) =>
        fromTimestampMs >= rangeStartMs && fromTimestampMs < rangeEndMsExclusive;

    /// <summary>Session must have ended strictly before the range end instant (ongoing or ending in the next month are excluded).</summary>
    public static bool EndedBeforeRangeEnd(long? toTimestampMs, long rangeEndMsExclusive) =>
        toTimestampMs.HasValue && toTimestampMs.Value < rangeEndMsExclusive;

    public static bool IncludeSession(long fromTimestampMs, long? toTimestampMs, long rangeStartMs, long rangeEndMsExclusive) =>
        StartedInRange(fromTimestampMs, rangeStartMs, rangeEndMsExclusive)
        && EndedBeforeRangeEnd(toTimestampMs, rangeEndMsExclusive);
}
