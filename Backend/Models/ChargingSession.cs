namespace Backend.Models
{
    public class ChargingSession
    {
        public int Id { get; set; }
        public DateTime Start { get; set; }
        public DateTime End { get; set; }
        public int UserId { get; set; }
        public int StationId { get; set; }
        public decimal ChargedKwh { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}