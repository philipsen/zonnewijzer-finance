namespace Backend.Models
{
    public class WipeDatabaseDeletedCounts
    {
        public int ChargingSessions { get; set; }
        public int Invoices { get; set; }
        public int EventLogs { get; set; }
        public int EnergyPrices { get; set; }
        public int ChargingUserProfiles { get; set; }
        public int MolliePayments { get; set; }
    }

    public class WipeDatabaseResult
    {
        public string Message { get; set; } = string.Empty;
        public int UsersPreserved { get; set; }
        public WipeDatabaseDeletedCounts Deleted { get; set; } = new();
    }
}
