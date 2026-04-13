namespace Backend.Controllers
{
    public class GoogleAuthRequest
    {
        public string Credential { get; set; } = string.Empty;
    }

    public class UpdateUserRequest
    {
        public string? Name { get; set; }
        public string? Email { get; set; }
        public string? Role { get; set; }
    }

    public class UpdateRoleRequest
    {
        public string Role { get; set; } = string.Empty;
    }

    public class UpdateChargingUserRequest
    {
        public string? DisplayName { get; set; }
        public int? LinkedUserId { get; set; }
        public bool UnlinkUser { get; set; } = false;
        public string? Street { get; set; }
        public string? HouseNumber { get; set; }
        public string? PostalCode { get; set; }
        public string? PlaceName { get; set; }
    }

    public class WipeDatabaseRequest
    {
        /// <summary>When true, authorizes POST <c>admin/wipe-database</c> (admin JWT required).</summary>
        public bool Confirm { get; set; }

        /// <summary>Exact phrase for POST <c>admin/clear-charging-data</c>.</summary>
        public string? Confirmation { get; set; }
    }
}