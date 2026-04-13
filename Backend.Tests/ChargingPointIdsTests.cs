using Backend;
using Xunit;

namespace Backend.Tests;

public class ChargingPointIdsTests
{
    [Fact]
    public void ChargingPointIdFromSerialAndConnector_IsDeterministic()
    {
        var a = ChargingPointIds.ChargingPointIdFromSerialAndConnector("6531001272", "1");
        var b = ChargingPointIds.ChargingPointIdFromSerialAndConnector("6531001272", "1");
        Assert.Equal(a, b);
        Assert.True(a > 0);
    }

    [Fact]
    public void ChargingPointIdFromSerialAndConnector_DiffersByConnector()
    {
        var c1 = ChargingPointIds.ChargingPointIdFromSerialAndConnector("6531001272", "1");
        var c2 = ChargingPointIds.ChargingPointIdFromSerialAndConnector("6531001272", "2");
        Assert.NotEqual(c1, c2);
    }
}
