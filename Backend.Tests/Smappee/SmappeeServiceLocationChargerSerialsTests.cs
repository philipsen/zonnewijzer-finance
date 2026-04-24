using Backend.Services.Smappee;

namespace Backend.Tests.Smappee;

public class SmappeeServiceLocationChargerSerialsTests
{
    [Fact]
    public void SameServiceLocationId_MultipleRows_AllSerialsAreChargers()
    {
        var rows = new List<SmappeeServiceLocationRowDto>
        {
            new() { ServiceLocationId = 42, ServiceLocationUuid = "u1", DeviceSerialNumber = "5822000001" },
            new() { ServiceLocationId = 42, ServiceLocationUuid = "u1", DeviceSerialNumber = "5822000002" }
        };
        var serials = SmappeeServiceLocationChargerSerials.SelectForChargingPark(rows, 42);
        Assert.Equal(2, serials.Count);
        Assert.Contains("5822000001", serials);
        Assert.Contains("5822000002", serials);
    }

    [Fact]
    public void SingleRowForConfiguredId_IsSiteRoot_OtherRowsWithSameUuidAreChargers()
    {
        var u = "6eefadeb-5c08-47cd-87b6-9897acb9fb8f";
        var rows = new List<SmappeeServiceLocationRowDto>
        {
            new() { ServiceLocationId = 307785, ServiceLocationUuid = u, DeviceSerialNumber = "4000000001" },
            new() { ServiceLocationId = 307786, ServiceLocationUuid = u, DeviceSerialNumber = "6531001272" },
            new() { ServiceLocationId = 307787, ServiceLocationUuid = u, DeviceSerialNumber = "6531001273" }
        };
        var serials = SmappeeServiceLocationChargerSerials.SelectForChargingPark(rows, 307785);
        Assert.Equal(2, serials.Count);
        Assert.Contains("6531001272", serials);
        Assert.Contains("6531001273", serials);
        Assert.DoesNotContain("4000000001", serials);
    }

    [Fact]
    public void SingleConfiguredRow_NoUuid_ReturnsEmpty()
    {
        var rows = new List<SmappeeServiceLocationRowDto>
        {
            new() { ServiceLocationId = 307785, ServiceLocationUuid = null, DeviceSerialNumber = "4000000001" }
        };
        var serials = SmappeeServiceLocationChargerSerials.SelectForChargingPark(rows, 307785);
        Assert.Empty(serials);
    }

    [Fact]
    public void ConfiguredIdMissingFromList_ReturnsEmpty()
    {
        var rows = new List<SmappeeServiceLocationRowDto>
        {
            new() { ServiceLocationId = 1, ServiceLocationUuid = "a", DeviceSerialNumber = "x" }
        };
        var serials = SmappeeServiceLocationChargerSerials.SelectForChargingPark(rows, 307785);
        Assert.Empty(serials);
    }
}
