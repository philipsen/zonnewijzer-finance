using Backend.Services;

namespace Backend.Tests.Services;

public class MonthlyInvoiceSessionRulesTests
{
    [Fact]
    public void IsBillableInMonth_ExcludesOngoingSessions()
    {
        Assert.False(MonthlyInvoiceSessionRules.IsBillableInMonth(null, 2026, 4));
    }

    [Fact]
    public void IsBillableInMonth_UsesEndMonthNotStartMonth()
    {
        var brusselsTz = TimeZoneInfo.FindSystemTimeZoneById(
            OperatingSystem.IsWindows() ? "W. Europe Standard Time" : "Europe/Brussels");
        var aprilEnd = TimeZoneInfo.ConvertTimeToUtc(
            new DateTime(2026, 4, 30, 23, 0, 0, DateTimeKind.Unspecified), brusselsTz);
        var mayEnd = TimeZoneInfo.ConvertTimeToUtc(
            new DateTime(2026, 5, 2, 10, 0, 0, DateTimeKind.Unspecified), brusselsTz);
        var endMs = new DateTimeOffset(mayEnd).ToUnixTimeMilliseconds();
        var startMs = new DateTimeOffset(aprilEnd).ToUnixTimeMilliseconds();

        Assert.False(MonthlyInvoiceSessionRules.IsBillableInMonth(endMs, 2026, 4));
        Assert.True(MonthlyInvoiceSessionRules.IsBillableInMonth(endMs, 2026, 5));
        Assert.True(MonthlyInvoiceSessionRules.IsBillableInMonth(startMs, 2026, 4));
    }
}
