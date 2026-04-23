using Backend.Models;

namespace Backend.Tests;

internal static class TestData
{
    public static long UtcMs(DateTime utc) =>
        new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).ToUnixTimeMilliseconds();

    public static ChargingSession Session(
        int chargeCardId,
        int stationId,
        DateTime startUtc,
        DateTime? endUtc,
        decimal kwh,
        string serial = "6531001272",
        string rfid = "RFID1")
    {
        var from = UtcMs(startUtc);
        long? to = endUtc.HasValue ? UtcMs(endUtc.Value) : null;
        return new ChargingSession
        {
            ChargeCardId = chargeCardId,
            StationId = stationId,
            FromTimestamp = from,
            ToTimestamp = to,
            StartReading = 0,
            StopReading = kwh,
            SerialNumber = serial,
            Rfid = rfid,
            Status = "STOPPED"
        };
    }
}
