namespace Backend.Models;

public class SmappeeChargingLiveResponse
{
    public DateTimeOffset FetchedAt { get; set; }
    public int ServiceLocationId { get; set; }
    public IReadOnlyList<ChargingPointLiveDto> Points { get; set; } = Array.Empty<ChargingPointLiveDto>();
    public long RangeStartMs { get; set; }
    public long RangeEndMsExclusive { get; set; }
}

public class ChargingPointLiveDto
{
    public string SerialNumber { get; set; } = "";
    /// <summary>Friendly label from config (<c>Smappee:StationDisplayNamesBySerial</c>) when set.</summary>
    public string? StationName { get; set; }
    /// <summary>charging, preparing, suspended, available, finished_recent</summary>
    public string UiState { get; set; } = "unknown";
    public string? SmappeeStatus { get; set; }
    public decimal? EnergyKwh { get; set; }
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? EndedAtUtc { get; set; }
    public string? RfidMasked { get; set; }
}
