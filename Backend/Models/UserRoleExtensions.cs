namespace Backend.Models;

public static class UserRoleExtensions
{
    public static bool IsAppAdministrator(this UserRole role) =>
        role is UserRole.Admin or UserRole.Owner;

    /// <summary>GnuCash insert / customer / wipe and related UI — owner only.</summary>
    public static bool CanUseGnucash(this UserRole role) => role == UserRole.Owner;
}
