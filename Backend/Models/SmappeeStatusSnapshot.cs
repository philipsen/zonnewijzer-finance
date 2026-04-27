namespace Backend.Models;

public class SmappeeStatusSnapshot
{
    public long Id { get; set; }
    public int ServiceLocationId { get; set; }
    public DateTime FetchedAtUtc { get; set; } = DateTime.UtcNow;
    public bool IsSuccess { get; set; }
    public string? Error { get; set; }
    public long RangeStartMs { get; set; }
    public long RangeEndMsExclusive { get; set; }
    public int ActiveSessionCount { get; set; }
    public int CompletedSessionCount { get; set; }
    public string PayloadJson { get; set; } = "{}";
}
