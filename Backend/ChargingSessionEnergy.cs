using Backend.Models;

namespace Backend;

public static class ChargingSessionEnergy
{
    public static decimal ChargedKwh(ChargingSession s) =>
        s.StartReading.HasValue && s.StopReading.HasValue
            ? Math.Max(0m, s.StopReading.Value - s.StartReading.Value)
            : 0m;

    public static DateTime StartUtc(long fromTimestampMs) =>
        DateTimeOffset.FromUnixTimeMilliseconds(fromTimestampMs).UtcDateTime;

    public static DateTime? EndUtc(long? toTimestampMs) =>
        toTimestampMs.HasValue
            ? DateTimeOffset.FromUnixTimeMilliseconds(toTimestampMs.Value).UtcDateTime
            : null;
}
