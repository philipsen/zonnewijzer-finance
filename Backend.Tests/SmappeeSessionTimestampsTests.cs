using Backend.Services.Smappee;

namespace Backend.Tests;

public class SmappeeSessionTimestampsTests
{
    [Fact]
    public void ToStoredMilliseconds_LeavesApiMillisecondsUnchanged()
    {
        var ms = 1_776_936_253_000L;
        Assert.Equal(ms, SmappeeSessionTimestamps.ToStoredMilliseconds(ms));
    }

    [Fact]
    public void ToStoredMilliseconds_ConvertsUnixSecondsFromSmappee()
    {
        const long sec = 1_776_936_253L;
        Assert.Equal(sec * 1000L, SmappeeSessionTimestamps.ToStoredMilliseconds(sec));
    }

    [Fact]
    public void ToStoredMilliseconds_NullStaysNull()
    {
        Assert.Null(SmappeeSessionTimestamps.ToStoredMilliseconds(null));
    }
}
