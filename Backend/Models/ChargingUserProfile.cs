namespace Backend.Models
{
    public class ChargingUserProfile
    {
        public int Id { get; set; }
        public string? DisplayName { get; set; }
        public string? Street { get; set; }
        public string? HouseNumber { get; set; }
        public string? PostalCode { get; set; }
        public string? PlaceName { get; set; }
    }
}
