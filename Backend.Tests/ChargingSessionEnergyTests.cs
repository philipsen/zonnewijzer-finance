using Backend.Models;

namespace Backend.Tests;

public class ChargingSessionEnergyTests
{
    private static ChargingSession S(decimal? start, decimal? stop) =>
        new()
        {
            StartReading = start,
            StopReading = stop,
            FromTimestamp = 0,
            ChargeCardId = 1,
            StationId = 1,
            CreatedAt = DateTime.UtcNow
        };

    [Fact]
    public void ChargedKwh_DecimalReadings_KwhDifference()
    {
        Assert.Equal(14.25m, ChargingSessionEnergy.ChargedKwh(S(100.96m, 115.21m)));
    }

    [Fact]
    public void ChargedKwh_LargeIntegerReadings_WhToKwh()
    {
        var start = 5_821_000_000m;
        var stop = 5_821_063_919m;
        Assert.Equal(63.919m, ChargingSessionEnergy.ChargedKwh(S(start, stop)));
    }

    [Fact]
    public void ChargedKwh_MediumIntegerReadings_WhToKwh()
    {
        Assert.Equal(36.629m, ChargingSessionEnergy.ChargedKwh(S(142_342m, 178_971m)));
    }

    [Fact]
    public void ChargedKwh_SmallIntegerReadings_StaysKwh()
    {
        Assert.Equal(65m, ChargingSessionEnergy.ChargedKwh(S(50m, 115m)));
    }

    [Fact]
    public void ChargedKwh_ManualStyleZeroStart_Kwh()
    {
        Assert.Equal(10.5m, ChargingSessionEnergy.ChargedKwh(S(0m, 10.5m)));
    }

    [Fact]
    public void ChargedKwh_MissingStop_Zero()
    {
        Assert.Equal(0m, ChargingSessionEnergy.ChargedKwh(S(100m, null)));
    }

    [Fact]
    public void SetReadingsFromSmappeeDto_ZeroReadingsPositiveEnergy_StoresEnergyAsStop()
    {
        var s = S(null, null);
        ChargingSessionEnergy.SetReadingsFromSmappeeDto(s, 0m, 0m, 48.73m);
        Assert.Equal(0m, s.StartReading);
        Assert.Equal(48.73m, s.StopReading);
        Assert.Equal(48.73m, ChargingSessionEnergy.ChargedKwh(s));
    }

    [Fact]
    public void SetReadingsFromSmappeeDto_PositiveDelta_IgnoresEnergy()
    {
        var s = S(null, null);
        ChargingSessionEnergy.SetReadingsFromSmappeeDto(s, 0m, 10.267m, 999m);
        Assert.Equal(0m, s.StartReading);
        Assert.Equal(10.267m, s.StopReading);
        Assert.Equal(10.267m, ChargingSessionEnergy.ChargedKwh(s));
    }

    [Fact]
    public void TryDeltaKwhFromReadings_StopNotAboveStart_Null()
    {
        Assert.Null(ChargingSessionEnergy.TryDeltaKwhFromReadings(10m, 10m));
        Assert.Null(ChargingSessionEnergy.TryDeltaKwhFromReadings(100m, 50m));
    }
}
