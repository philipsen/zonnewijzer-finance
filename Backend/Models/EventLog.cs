namespace Backend.Models
{
    public class EventLog
    {
        public int Id { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public string UserName { get; set; } = string.Empty;
        public string Action { get; set; } = string.Empty;
    }
}
