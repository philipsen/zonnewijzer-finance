namespace Backend.Models;

public class SigenergyStatusSnapshot
{
    public long Id { get; set; }
    public DateTime FetchedAtUtc { get; set; } = DateTime.UtcNow;
    public bool IsSuccess { get; set; }
    public string? Error { get; set; }
    public bool Configured { get; set; }
    public double? BatterySocPercent { get; set; }
    public double? BatteryPowerKw { get; set; }
    public double? SolarPowerKw { get; set; }
    public double? RemainingBatteryKwh { get; set; }
    public string PayloadJson { get; set; } = "{}";
}
