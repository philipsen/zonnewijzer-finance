namespace Backend.Models
{
    public class ChargingSession
    {
        public int Id { get; set; }
        public DateTime Start { get; set; }
        /// <summary>Null while the session is still in progress.</summary>
        public DateTime? End { get; set; }
        public int UserId { get; set; }
        /// <summary>Stable id for the charging point (station serial + connector).</summary>
        public int StationId { get; set; }
        public decimal ChargedKwh { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>Hardware serial from the export; optional for legacy rows.</summary>
        public string? StationSerialNumber { get; set; }

        /// <summary>Connector index from the export; optional for legacy rows.</summary>
        public string? Connector { get; set; }

        /// <summary>Human-readable station name from the export.</summary>
        public string? ChargingStationName { get; set; }
    }
}