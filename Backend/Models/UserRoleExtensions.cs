namespace Backend.Models;

public static class UserRoleExtensions
{
    public static bool IsAppAdministrator(this UserRole role) =>
        role is UserRole.Admin or UserRole.Owner;

    /// <summary>List app users and edit names, emails, and roles (Owner assignment rules still apply). Base + administrators.</summary>
    public static bool CanManageUserProfiles(this UserRole role) =>
        role == UserRole.Base || role.IsAppAdministrator();

    /// <summary>GnuCash insert / customer / wipe and related UI — owner only.</summary>
    public static bool CanUseGnucash(this UserRole role) => role == UserRole.Owner;

    /// <summary>Clear all charging sessions and charge cards (keep invoices) — owner only.</summary>
    public static bool CanClearChargingData(this UserRole role) => role == UserRole.Owner;
}
