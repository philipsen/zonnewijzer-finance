using System.Text.Json;
using Backend;
using Backend.Models;
using Backend.Services.Smappee;

namespace Backend.Tests.Smappee;

public class SmappeeSessionDtoJsonTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>Real Smappee v3 shape: <c>serialNumber</c> is string digits, <c>from</c>/<c>to</c> in seconds, <c>energy</c> mirrors kWh.</summary>
    /// <summary>Some EVSEs report <c>energy</c> while cumulative readings stay at zero.</summary>
    public const string SessionZeroReadingsWithEnergyJson =
        """
        {
          "id": 472634640,
          "serialNumber": "5821063919",
          "connector": 1,
          "authenticationType": "RFID",
          "rfid": "04708D62C71F90",
          "from": 1745349420,
          "to": 1745393940,
          "status": "STOPPED",
          "startReading": 0.0,
          "stopReading": 0.0,
          "energy": 62.415
        }
        """;

    public const string Session28383139Json =
        """
        {
          "id": 28383139,
          "serialNumber": "5821063919",
          "connector": 1,
          "authenticationType": "RFID",
          "rfid": "04708D62C71F90",
          "userId": 298575,
          "from": 1776774426,
          "to": 1776791698,
          "status": "STOPPED",
          "externalControl": false,
          "smartMode": "CHARGE_NOW",
          "priority": 50,
          "minimumExcessPercentage": 100,
          "maxAmperes": [15.0, 15.0, 0.0],
          "startReading": 0.0,
          "stopReading": 10.267,
          "energy": 10.267
        }
        """;

    [Fact]
    public void Deserialize_RealWorld28383139_ParsesCoreFieldsAndEnergy()
    {
        var dto = JsonSerializer.Deserialize<SmappeeSessionDto>(Session28383139Json, JsonOptions);
        Assert.NotNull(dto);
        Assert.Equal(28_383_139L, dto.Id);
        Assert.Equal("5821063919", dto.SerialNumber);
        Assert.Equal(1, dto.Connector);
        Assert.Equal("RFID", dto.AuthenticationType);
        Assert.Equal("04708D62C71F90", dto.Rfid);
        Assert.Equal(1_776_774_426L, dto.From);
        Assert.Equal(1_776_791_698L, dto.To);
        Assert.Equal("STOPPED", dto.Status);
        Assert.Equal(50, dto.Priority);
        Assert.Equal(0m, dto.StartReading);
        Assert.Equal(10.267m, dto.StopReading);
        Assert.Equal(10.267m, dto.Energy);
    }

    [Fact]
    public void Session28383139_Timestamps_NormalizeSecondsToMs()
    {
        var dto = JsonSerializer.Deserialize<SmappeeSessionDto>(Session28383139Json, JsonOptions)!;
        Assert.Equal(1_776_774_426_000L, SmappeeSessionTimestamps.ToStoredMilliseconds(dto.From));
        Assert.Equal(1_776_791_698_000L, SmappeeSessionTimestamps.ToStoredMilliseconds(dto.To));
    }

    [Fact]
    public void SessionZeroReadingsWithEnergy_AfterMapping_ChargedKwhUsesEnergy()
    {
        var dto = JsonSerializer.Deserialize<SmappeeSessionDto>(SessionZeroReadingsWithEnergyJson, JsonOptions)!;
        var session = new ChargingSession
        {
            FromTimestamp = 0,
            ChargeCardId = 1,
            StationId = 1,
            CreatedAt = DateTime.UtcNow
        };
        ChargingSessionEnergy.SetReadingsFromSmappeeDto(session, dto.StartReading, dto.StopReading, dto.Energy);
        Assert.Equal(62.415m, ChargingSessionEnergy.ChargedKwh(session));
    }

    [Fact]
    public void Session28383139_ChargedKwh_MatchesEnergyField()
    {
        var dto = JsonSerializer.Deserialize<SmappeeSessionDto>(Session28383139Json, JsonOptions)!;
        var session = new ChargingSession
        {
            FromTimestamp = SmappeeSessionTimestamps.ToStoredMilliseconds(dto.From),
            ToTimestamp = SmappeeSessionTimestamps.ToStoredMilliseconds(dto.To),
            StartReading = dto.StartReading,
            StopReading = dto.StopReading,
            ChargeCardId = 1,
            StationId = 1,
            CreatedAt = DateTime.UtcNow
        };
        Assert.Equal(dto.Energy, ChargingSessionEnergy.ChargedKwh(session));
        Assert.Equal(10.267m, ChargingSessionEnergy.ChargedKwh(session));
    }

    [Fact]
    public void Session28383139_UsesConnectorForChargingPointId()
    {
        var dto = JsonSerializer.Deserialize<SmappeeSessionDto>(Session28383139Json, JsonOptions)!;
        var serial = dto.SerialNumberNormalized();
        var expected = ChargingPointIds.ChargingPointIdFromSerialAndConnector(serial, "1");
        var fromDto = ChargingPointIds.ChargingPointIdFromSerialAndConnector(
            serial,
            dto.Connector!.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(expected, fromDto);
    }
}
