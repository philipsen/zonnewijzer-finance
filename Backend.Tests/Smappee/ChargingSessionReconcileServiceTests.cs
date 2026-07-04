using Backend.Services.Smappee;

namespace Backend.Tests.Smappee;

public class ChargingSessionReconcileServiceTests
{
    [Fact]
    public void ComputeSyncRange_WithNoOpenSessions_UsesDefaultLookback()
    {
        var brusselsTz = TimeZoneInfo.FindSystemTimeZoneById(
            OperatingSystem.IsWindows() ? "W. Europe Standard Time" : "Europe/Brussels");
        var todayBrussels = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, brusselsTz));

        var (from, to) = ChargingSessionReconcileService.ComputeSyncRange(Array.Empty<OpenSessionSummary>());

        Assert.Equal(todayBrussels, to);
        Assert.Equal(todayBrussels.AddDays(-90), from);
    }

    [Fact]
    public void ComputeSyncRange_WithOldOpenSession_ExtendsFromDate()
    {
        var brusselsTz = TimeZoneInfo.FindSystemTimeZoneById(
            OperatingSystem.IsWindows() ? "W. Europe Standard Time" : "Europe/Brussels");
        var todayBrussels = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, brusselsTz));
        var oldStart = TimeZoneInfo.ConvertTimeToUtc(
            new DateTime(todayBrussels.AddDays(-120).Year, todayBrussels.AddDays(-120).Month, todayBrussels.AddDays(-120).Day, 12, 0, 0, DateTimeKind.Unspecified),
            brusselsTz);

        var (from, to) = ChargingSessionReconcileService.ComputeSyncRange(
        [
            new OpenSessionSummary { Id = 1, ChargeCardId = 1, StartUtc = oldStart }
        ]);

        Assert.Equal(todayBrussels, to);
        Assert.Equal(todayBrussels.AddDays(-120), from);
    }
}
