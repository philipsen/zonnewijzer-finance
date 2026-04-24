using System.Collections.Generic;
using Backend.Services.Smappee;

namespace Backend.Tests.Smappee;

public class SmappeeChargingLiveMapperTests
{
    [Fact]
    public void Build_MarksActiveCharging()
    {
        var now = new DateTimeOffset(2025, 6, 1, 14, 0, 0, TimeSpan.Zero);
        var t0 = new DateTimeOffset(2025, 6, 1, 12, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();
        var active = new SmappeeSessionDto
        {
            Id = 1,
            SerialNumber = "5821000000",
            Connector = 1,
            From = t0,
            To = null,
            Status = "CHARGING",
            Energy = 3.2m
        };
        var completed = Array.Empty<SmappeeSessionDto>();
        var r = SmappeeChargingLiveMapper.Build(42, new[] { active }, completed, 0, 1, now, now);
        Assert.Single(r.Points);
        var p = r.Points[0];
        Assert.Equal("charging", p.UiState);
        Assert.Equal("5821000000", p.SerialNumber);
    }

    [Fact]
    public void Build_RecentlyStoppedWithoutActive_IsFinishedRecent()
    {
        var now = new DateTimeOffset(2025, 6, 1, 14, 0, 0, TimeSpan.Zero);
        var endOneHourAgo = now.AddHours(-1);
        var toSec = endOneHourAgo.ToUnixTimeSeconds();
        var fromSec = endOneHourAgo.AddHours(-1).ToUnixTimeSeconds();
        var end = new SmappeeSessionDto
        {
            Id = 2,
            SerialNumber = "5821000000",
            Connector = 1,
            From = fromSec,
            To = toSec,
            Status = "STOPPED",
            Energy = 5m
        };
        var r = SmappeeChargingLiveMapper.Build(42, Array.Empty<SmappeeSessionDto>(), new[] { end }, 0, 1, now, now);
        Assert.Single(r.Points);
        Assert.Equal("finished_recent", r.Points[0].UiState);
    }

    [Fact]
    public void Build_OldStopWithoutActive_IsAvailable()
    {
        var now = new DateTimeOffset(2025, 6, 1, 14, 0, 0, TimeSpan.Zero);
        var toSec = now.AddDays(-1).ToUnixTimeSeconds();
        var fromSec = toSec - 3_600;
        var st = new SmappeeSessionDto
        {
            Id = 3,
            SerialNumber = "999",
            Connector = 2,
            From = fromSec,
            To = toSec,
            Status = "STOPPED"
        };
        var r = SmappeeChargingLiveMapper.Build(42, Array.Empty<SmappeeSessionDto>(), new[] { st }, 0, 1, now, now);
        var p = r.Points[0];
        Assert.Equal("available", p.UiState);
        Assert.Null(p.EnergyKwh);
        Assert.Null(p.StartedAtUtc);
        Assert.Null(p.EndedAtUtc);
        Assert.Null(p.SmappeeStatus);
        Assert.Null(p.RfidMasked);
    }

    [Fact]
    public void Build_SameSerial_DifferentConnectors_MergesToOneRow_PrefersCharging()
    {
        var now = new DateTimeOffset(2025, 6, 1, 14, 0, 0, TimeSpan.Zero);
        var t0 = new DateTimeOffset(2025, 6, 1, 12, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();
        var a1 = new SmappeeSessionDto
        {
            Id = 10,
            SerialNumber = "5821000000",
            Connector = null,
            From = t0,
            To = null,
            Status = "STARTED",
            Energy = 0.1m
        };
        var a2 = new SmappeeSessionDto
        {
            Id = 11,
            SerialNumber = "5821000000",
            Connector = 1,
            From = t0,
            To = null,
            Status = "CHARGING",
            Energy = 3m
        };
        var r = SmappeeChargingLiveMapper.Build(42, new[] { a1, a2 }, Array.Empty<SmappeeSessionDto>(), 0, 1, now, now);
        Assert.Single(r.Points);
        Assert.Equal("charging", r.Points[0].UiState);
    }

    [Fact]
    public void Build_KnownSerialWithoutRecentSession_IsAvailableWithDisplayName()
    {
        var now = new DateTimeOffset(2025, 6, 1, 14, 0, 0, TimeSpan.Zero);
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["777"] = "Testpaal" };
        var r = SmappeeChargingLiveMapper.Build(
            42,
            Array.Empty<SmappeeSessionDto>(),
            Array.Empty<SmappeeSessionDto>(),
            0,
            1,
            now,
            now,
            names,
            new[] { "777" });
        Assert.Single(r.Points);
        Assert.Equal("available", r.Points[0].UiState);
        Assert.Equal("777", r.Points[0].SerialNumber);
        Assert.Equal("Testpaal", r.Points[0].StationName);
    }

    [Fact]
    public void Build_NeverUsedSerial_OnlyListedInNameMap_ShowsAvailable()
    {
        var now = new DateTimeOffset(2025, 6, 1, 14, 0, 0, TimeSpan.Zero);
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["888"] = "Nog niet gebruikt" };
        var r = SmappeeChargingLiveMapper.Build(
            42,
            Array.Empty<SmappeeSessionDto>(),
            Array.Empty<SmappeeSessionDto>(),
            0,
            1,
            now,
            now,
            names,
            null);
        Assert.Single(r.Points);
        Assert.Equal("available", r.Points[0].UiState);
        Assert.Equal("888", r.Points[0].SerialNumber);
        Assert.Equal("Nog niet gebruikt", r.Points[0].StationName);
    }

    [Fact]
    public void Build_NeverUsedSerial_InExpectedChargerSerials_ShowsAvailable()
    {
        var now = new DateTimeOffset(2025, 6, 1, 14, 0, 0, TimeSpan.Zero);
        var r = SmappeeChargingLiveMapper.Build(
            42,
            Array.Empty<SmappeeSessionDto>(),
            Array.Empty<SmappeeSessionDto>(),
            0,
            1,
            now,
            now,
            null,
            null,
            new[] { "9990000001" });
        Assert.Single(r.Points);
        Assert.Equal("available", r.Points[0].UiState);
        Assert.Equal("9990000001", r.Points[0].SerialNumber);
        Assert.Null(r.Points[0].StationName);
    }

    [Fact]
    public void Build_OutOfRangeUnixFrom_DoesNotThrow_StartsAtNull()
    {
        var now = new DateTimeOffset(2025, 6, 1, 14, 0, 0, TimeSpan.Zero);
        var active = new SmappeeSessionDto
        {
            Id = 1,
            SerialNumber = "5821000000",
            Connector = 1,
            From = long.MaxValue,
            To = null,
            Status = "CHARGING",
            Energy = 1m
        };
        var r = SmappeeChargingLiveMapper.Build(42, new[] { active }, Array.Empty<SmappeeSessionDto>(), 0, 1, now, now);
        Assert.Single(r.Points);
        Assert.Null(r.Points[0].StartedAtUtc);
    }

    [Fact]
    public void MaskRfid_TrimsAndEllipsis()
    {
        Assert.Null(SmappeeChargingLiveMapper.MaskRfid(null));
        Assert.Equal("****", SmappeeChargingLiveMapper.MaskRfid("abcd"));
        Assert.Equal("…1F90", SmappeeChargingLiveMapper.MaskRfid("04708D62C71F90"));
    }
}
