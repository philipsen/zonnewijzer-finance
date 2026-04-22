namespace Backend.Models
{
    public enum UserRole
    {
        Base = 0,
        Admin = 1,
        /// <summary>Same privileges as <see cref="Admin"/>, plus GnuCash integration API and UI.</summary>
        Owner = 2
    }
}