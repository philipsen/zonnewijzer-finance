namespace Backend.Models
{
    public class ChargingUserProfile
    {
        public int Id { get; set; }
        /// <summary>RFID / token key (segment before <c> (</c> in the export), for display and auditing.</summary>
        public string? RfidTokenKey { get; set; }
        public string? DisplayName { get; set; }
        public string? Street { get; set; }
        public string? HouseNumber { get; set; }
        public string? PostalCode { get; set; }
        public string? PlaceName { get; set; }
    }
}
