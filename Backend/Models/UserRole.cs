namespace Backend.Models
{
    public enum UserRole
    {
        Base = 0,
        Admin = 1,
        /// <summary>Same privileges as <see cref="Admin"/>, plus owner-only administration.</summary>
        Owner = 2
    }
}