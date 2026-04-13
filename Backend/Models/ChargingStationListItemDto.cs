namespace Backend.Models;

/// <summary>
/// Aggregated charging point derived from session history (serial + connector, or legacy station id only).
/// </summary>
public class ChargingStationListItemDto
{
    public int StationId { get; set; }
    public string? StationSerialNumber { get; set; }
    public string? Connector { get; set; }
    public string? ChargingStationName { get; set; }
    public int SessionCount { get; set; }
    public DateTime LastSessionStart { get; set; }
    public bool IsLegacy { get; set; }
}
