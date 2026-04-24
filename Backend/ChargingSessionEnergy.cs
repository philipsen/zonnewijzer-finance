using Backend.Models;

namespace Backend;

public static class ChargingSessionEnergy
{
    /// <summary>
    /// Positive delivered kWh from cumulative meter readings, or <c>null</c> if missing or non-increasing.
    /// </summary>
    public static decimal? TryDeltaKwhFromReadings(decimal? startReading, decimal? stopReading)
    {
        if (!startReading.HasValue || !stopReading.HasValue)
            return null;
        var start = startReading.Value;
        var stop = stopReading.Value;
        if (stop <= start)
            return null;
        var delta = stop - start;
        if (LooksLikeWhIntegerMeter(start, stop))
            return Math.Max(0m, delta / 1000m);
        return Math.Max(0m, delta);
    }

    /// <summary>
    /// Smappee sometimes leaves <c>startReading</c>/<c>stopReading</c> at zero while <c>energy</c> holds the session kWh.
    /// Prefer a positive meter delta when present; otherwise use <paramref name="energy"/> as kWh (stored as 0 → energy).
    /// </summary>
    public static void SetReadingsFromSmappeeDto(
        ChargingSession session,
        decimal? startReading,
        decimal? stopReading,
        decimal? energy)
    {
        var delta = TryDeltaKwhFromReadings(startReading, stopReading);
        if (delta is > 0m)
        {
            session.StartReading = startReading;
            session.StopReading = stopReading;
        }
        else if (energy is > 0m)
        {
            session.StartReading = 0m;
            session.StopReading = energy;
        }
        else
        {
            session.StartReading = startReading;
            session.StopReading = stopReading;
        }
    }

    /// <summary>
    /// Delivered energy in kWh. Smappee usually sends <c>startReading</c>/<c>stopReading</c> as kWh with decimals,
    /// but some stations report cumulative <strong>Wh</strong> as large integers; then the raw difference must be divided by 1000.
    /// </summary>
    public static decimal ChargedKwh(ChargingSession s) =>
        TryDeltaKwhFromReadings(s.StartReading, s.StopReading) ?? 0m;

    /// <summary>Large cumulative counters without fractional kWh → treat difference as Wh.</summary>
    private static bool LooksLikeWhIntegerMeter(decimal start, decimal stop)
    {
        if (Math.Min(start, stop) < 20_000m)
            return false;
        const decimal eps = 0.0001m;
        var intStart = Math.Truncate(start);
        var intStop = Math.Truncate(stop);
        return Math.Abs(start - intStart) < eps && Math.Abs(stop - intStop) < eps;
    }

    public static DateTime StartUtc(long fromTimestampMs) =>
        DateTimeOffset.FromUnixTimeMilliseconds(fromTimestampMs).UtcDateTime;

    public static DateTime? EndUtc(long? toTimestampMs) =>
        toTimestampMs.HasValue
            ? DateTimeOffset.FromUnixTimeMilliseconds(toTimestampMs.Value).UtcDateTime
            : null;
}
