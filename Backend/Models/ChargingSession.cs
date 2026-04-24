namespace Backend.Models;

public class ChargingSession
{
    public int Id { get; set; }

    /// <summary>Smappee session id; null for manually entered rows.</summary>
    public long? SmappeeSessionId { get; set; }

    public string? SerialNumber { get; set; }
    public string? AuthenticationType { get; set; }
    public string? Rfid { get; set; }
    public string? CsmsTransactionId { get; set; }

    /// <summary>Session start, epoch ms (Smappee <c>from</c>).</summary>
    public long FromTimestamp { get; set; }

    /// <summary>Session end, epoch ms (Smappee <c>to</c>).</summary>
    public long? ToTimestamp { get; set; }

    public string? Status { get; set; }
    public int? Range { get; set; }
    public int? Duration { get; set; }
    public int? Priority { get; set; }

    public decimal? StartReading { get; set; }
    public decimal? StopReading { get; set; }

    public int ChargeCardId { get; set; }

    /// <summary>Derived charging point id (serial + default connector).</summary>
    public int StationId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
