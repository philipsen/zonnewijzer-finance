using Backend.Models;
using Backend.Services.Smappee;

namespace Backend.Tests.Smappee;

public class SmappeeChargingSessionMergeTests
{
    [Fact]
    public void MergeActiveAndCompleted_PrefersCompletedRowWithEndTime()
    {
        var active = new List<SmappeeSessionDto>
        {
            new() { Id = 1, From = 1000, To = null, Status = "CHARGING" }
        };
        var completed = new List<SmappeeSessionDto>
        {
            new() { Id = 1, From = 1000, To = 2000, Status = "STOPPED" }
        };

        var merged = SmappeeChargingSessionMerge.MergeActiveAndCompleted(active, completed);

        var session = Assert.Single(merged);
        Assert.Equal(2000, session.To);
        Assert.Equal("STOPPED", session.Status);
    }

    [Fact]
    public void InferCloseFromNextSessionOnSameCard_ClosesOlderOpenRows()
    {
        var sessions = new List<ChargingSession>
        {
            new() { Id = 1, ChargeCardId = 12, FromTimestamp = 1000, ToTimestamp = null, Status = "CHARGING" },
            new() { Id = 2, ChargeCardId = 12, FromTimestamp = 5000, ToTimestamp = null, Status = "CHARGING" },
            new() { Id = 3, ChargeCardId = 13, FromTimestamp = 2000, ToTimestamp = null, Status = "CHARGING" }
        };

        var closed = SmappeeChargingSessionMerge.InferCloseFromNextSessionOnSameCard(sessions);

        Assert.Equal(1, closed);
        Assert.Equal(5000, sessions[0].ToTimestamp);
        Assert.Equal("STOPPED", sessions[0].Status);
        Assert.Null(sessions[1].ToTimestamp);
        Assert.Null(sessions[2].ToTimestamp);
    }
}
