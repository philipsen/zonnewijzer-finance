using Backend;

namespace Backend.Services.Smappee;

/// <summary>
/// Smappee charging-park session API returns <c>from</c> / <c>to</c> as Unix <strong>seconds</strong>;
/// this app stores <strong>milliseconds</strong> on <see cref="Models.ChargingSession"/>.
/// </summary>
public static class SmappeeSessionTimestamps
{
    public static long ToStoredMilliseconds(long apiValue) =>
        BrusselsTime.LooksLikeUnixSecondsInsteadOfMilliseconds(apiValue)
            ? apiValue * 1000L
            : apiValue;

    public static long? ToStoredMilliseconds(long? apiValue) =>
        apiValue.HasValue ? ToStoredMilliseconds(apiValue.Value) : null;
}
