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
}