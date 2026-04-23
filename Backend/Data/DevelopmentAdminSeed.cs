using Backend.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Serilog;

namespace Backend.Data;

/// <summary>Creates or updates a local admin account when <see cref="DevelopmentSeedOptions"/> is set (Development only).</summary>
public static class DevelopmentAdminSeed
{
    /// <summary>Preferred: set in repo root <c>.env</c> (e.g. via direnv). Also accepts <c>DevelopmentSeed__BootstrapAdmin__Password</c>.</summary>
    public const string BootstrapAdminPasswordEnvVar = "FINANCE_DEV_BOOTSTRAP_ADMIN_PASSWORD";

    private static readonly PasswordHasher<User> PasswordHasher = new();

    public static void Apply(IConfiguration configuration, ApplicationDbContext context)
    {
        var opts = configuration.GetSection(DevelopmentSeedOptions.SectionName).Get<DevelopmentSeedOptions>();
        if (opts?.BootstrapAdmin == null)
            return;

        var admin = opts.BootstrapAdmin;
        var password =
            configuration[BootstrapAdminPasswordEnvVar]?.Trim()
            ?? admin.Password?.Trim();
        if (string.IsNullOrWhiteSpace(admin.Email) || string.IsNullOrWhiteSpace(password))
            return;

        var email = admin.Email.Trim().ToLowerInvariant();
        var name = string.IsNullOrWhiteSpace(admin.Name) ? email.Split('@')[0] : admin.Name.Trim();
        var role = UserRole.Admin;

        var existing = context.Users.FirstOrDefault(u => u.Email == email);
        if (existing == null)
        {
            var entity = new User
            {
                Email = email,
                Name = name,
                Role = role,
                GoogleId = null,
                GitHubId = null,
                CreatedAt = DateTime.UtcNow,
                LastLoginAt = DateTime.UtcNow
            };
            entity.PasswordHash = PasswordHasher.HashPassword(entity, password);
            context.Users.Add(entity);
            context.SaveChanges();
            Log.Information("Development seed: created bootstrap admin {Email}", email);
            return;
        }

        existing.Role = role;
        existing.Name = name;
        existing.PasswordHash = PasswordHasher.HashPassword(existing, password);
        context.SaveChanges();
        Log.Information("Development seed: updated bootstrap admin {Email} (password and role)", email);
    }
}

public class DevelopmentSeedOptions
{
    public const string SectionName = "DevelopmentSeed";

    public BootstrapAdminOptions? BootstrapAdmin { get; set; }
}

public class BootstrapAdminOptions
{
    public string? Email { get; set; }

    /// <summary>Optional in JSON; prefer <see cref="DevelopmentAdminSeed.BootstrapAdminPasswordEnvVar"/> or <c>DevelopmentSeed__BootstrapAdmin__Password</c> in <c>.env</c>.</summary>
    public string? Password { get; set; }

    public string? Name { get; set; }
}
