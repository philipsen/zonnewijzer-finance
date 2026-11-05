namespace Backend.Models
{
    public class PasswordResetToken
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public string TokenHash { get; set; } = string.Empty;
        public DateTime ExpiresAt { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UsedAt { get; set; }
        public User? User { get; set; }
    }

    public class User
    {
        public int Id { get; set; }
        public string? GoogleId { get; set; }
        public string? GitHubId { get; set; }
        public string? PasswordHash { get; set; }
        public string Email { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? PictureUrl { get; set; }
        public UserRole Role { get; set; } = UserRole.Base;
        /// <summary>Inactive users stay in the app and are left out of the e-Boekhouden exports.</summary>
        public bool IsActive { get; set; } = true;
        /// <summary>e-Boekhouden relation code, at most 13 characters. Filled in by an administrator.</summary>
        public string? EboekhoudenCode { get; set; }
        public ICollection<UserChargeCard> UserChargeCards { get; set; } = new List<UserChargeCard>();
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime LastLoginAt { get; set; } = DateTime.UtcNow;
    }
}