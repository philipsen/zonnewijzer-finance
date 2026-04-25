using Backend.Services.Sigenergy;

namespace Backend.Tests.Sigenergy;

public class SigenergyCloudClientTests
{
    [Fact]
    public void ParseSigenEnergyEnvelope_ParsesStringifiedData()
    {
        const string json = """
            {
              "code": 0,
              "msg": "success",
              "timestamp": 1777130931,
              "data": "{\"pvPower\":6.1,\"gridPower\":6.1,\"evPower\":0.0,\"acPower\":0.0,\"loadPower\":0.0,\"heatPumpPower\":0.0,\"batteryPower\":-0.5,\"batterySoc\":100.0}"
            }
            """;

        var d = SigenergyCloudClient.ParseSigenEnergyEnvelopeOrThrow(json);
        Assert.Equal(6.1, d.PvPower);
        Assert.Equal(-0.5, d.BatteryPower);
        Assert.Equal(100.0, d.BatterySoc);
    }

    [Fact]
    public void ParseLoginResponse_FindsAccessTokenInDataObject()
    {
        const string json = """
            {
              "code": 0,
              "msg": "success",
              "data": {
                "accessToken": "01234567890123456789012345678901234567890123456789012"
              }
            }
            """;

        var (token, until) = SigenergyCloudClient.ParseLoginResponseForTokenOrThrow(json);
        Assert.Equal("01234567890123456789012345678901234567890123456789012", token);
        Assert.True(until > DateTimeOffset.UtcNow);
    }
}
