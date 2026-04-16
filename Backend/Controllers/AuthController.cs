using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Google.Apis.Auth;
using Backend.Data;
using Backend.Models;
using Backend.Services;
using System.Security.Cryptography;
using System.Text;

namespace Backend.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly IJwtService _jwtService;
        private readonly IConfiguration _configuration;
        private readonly ILogger<AuthController> _logger;
        private readonly IEmailService _emailService;
        private static readonly PasswordHasher<User> PasswordHasher = new();

        public AuthController(ApplicationDbContext context, IJwtService jwtService, IConfiguration configuration, ILogger<AuthController> logger, IEmailService emailService)
        {
            _context = context;
            _jwtService = jwtService;
            _configuration = configuration;
            _logger = logger;
            _emailService = emailService;
        }

        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] RegisterRequest request)
        {
            try
            {
                var email = request.Email?.Trim().ToLowerInvariant();
                if (string.IsNullOrWhiteSpace(email) || !email.Contains('@'))
                    return BadRequest("A valid email address is required.");

                if (string.IsNullOrWhiteSpace(request.Password) || request.Password.Length < 8)
                    return BadRequest("Password must be at least 8 characters.");

                var existing = await _context.Users.FirstOrDefaultAsync(u => u.Email == email);
                if (existing != null)
                    return Conflict("An account with this email already exists.");

                var displayName = string.IsNullOrWhiteSpace(request.Name)
                    ? email.Split('@')[0]
                    : request.Name.Trim();

                var user = new User
                {
                    Email = email,
                    Name = displayName,
                    CreatedAt = DateTime.UtcNow,
                    LastLoginAt = DateTime.UtcNow
                };
                user.PasswordHash = PasswordHasher.HashPassword(user, request.Password);

                _context.Users.Add(user);
                _context.EventLogs.Add(new EventLog
                {
                    Timestamp = DateTime.UtcNow,
                    UserName = user.Name,
                    Action = "Register (Email/Password)"
                });

                await _context.SaveChangesAsync();
                var token = _jwtService.GenerateToken(user);
                return Ok(BuildAuthResponse(user, token));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Email/password registration failed for {Email}", request.Email);
                return BadRequest($"Registration failed: {ex.Message}");
            }
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginRequest request)
        {
            try
            {
                var email = request.Email?.Trim().ToLowerInvariant();
                if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(request.Password))
                    return Unauthorized("Invalid email or password.");

                var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == email);
                if (user == null || string.IsNullOrWhiteSpace(user.PasswordHash))
                    return Unauthorized("Invalid email or password.");

                var verify = PasswordHasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);
                if (verify == PasswordVerificationResult.Failed)
                    return Unauthorized("Invalid email or password.");

                if (verify == PasswordVerificationResult.SuccessRehashNeeded)
                    user.PasswordHash = PasswordHasher.HashPassword(user, request.Password);

                user.LastLoginAt = DateTime.UtcNow;
                _context.EventLogs.Add(new EventLog
                {
                    Timestamp = DateTime.UtcNow,
                    UserName = user.Name,
                    Action = "Login (Email/Password)"
                });
                await _context.SaveChangesAsync();

                var token = _jwtService.GenerateToken(user);
                return Ok(BuildAuthResponse(user, token));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Email/password login failed for {Email}", request.Email);
                return Unauthorized();
            }
        }

        [HttpPost("forgot-password")]
        public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest request)
        {
            try
            {
                var normalizedEmail = request.Email?.Trim().ToLowerInvariant();
                if (string.IsNullOrWhiteSpace(normalizedEmail))
                    return Ok(new { message = "If an account exists for that email, a reset link has been sent." });

                var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == normalizedEmail);
                if (user == null || string.IsNullOrWhiteSpace(user.PasswordHash))
                    return Ok(new { message = "If an account exists for that email, a reset link has been sent." });

                var rawToken = GenerateResetToken();
                var tokenHash = HashToken(rawToken);
                var expiresAt = DateTime.UtcNow.AddMinutes(30);

                var existingActiveTokens = await _context.PasswordResetTokens
                    .Where(t => t.UserId == user.Id && t.UsedAt == null)
                    .ToListAsync();
                foreach (var token in existingActiveTokens)
                    token.UsedAt = DateTime.UtcNow;

                _context.PasswordResetTokens.Add(new PasswordResetToken
                {
                    UserId = user.Id,
                    TokenHash = tokenHash,
                    ExpiresAt = expiresAt,
                    CreatedAt = DateTime.UtcNow
                });
                await _context.SaveChangesAsync();

                var resetLink = BuildResetLink(request.ResetBaseUrl, rawToken);
                var body = $@"
<p>Hello {System.Net.WebUtility.HtmlEncode(user.Name)},</p>
<p>You requested a password reset for your Zonnewijzer Finance account.</p>
<p><a href=""{resetLink}"">Click here to reset your password</a></p>
<p>This link is valid for 30 minutes and can only be used once.</p>
<p>If you did not request this, you can ignore this email.</p>";

                await _emailService.SendAsync(user.Email, "Reset your Zonnewijzer Finance password", body);
                return Ok(new { message = "If an account exists for that email, a reset link has been sent." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Forgot-password flow failed for {Email}", request.Email);
                return Ok(new { message = "If an account exists for that email, a reset link has been sent." });
            }
        }

        [HttpPost("reset-password")]
        public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest request)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(request.Token))
                    return BadRequest("Invalid reset token.");
                if (string.IsNullOrWhiteSpace(request.NewPassword) || request.NewPassword.Length < 8)
                    return BadRequest("Password must be at least 8 characters.");

                var tokenHash = HashToken(request.Token.Trim());
                var resetToken = await _context.PasswordResetTokens
                    .Include(t => t.User)
                    .FirstOrDefaultAsync(t => t.TokenHash == tokenHash);

                if (resetToken == null || resetToken.User == null || resetToken.UsedAt != null || resetToken.ExpiresAt <= DateTime.UtcNow)
                    return BadRequest("This reset link is invalid or expired.");

                var user = resetToken.User;
                user.PasswordHash = PasswordHasher.HashPassword(user, request.NewPassword);
                user.LastLoginAt = DateTime.UtcNow;
                resetToken.UsedAt = DateTime.UtcNow;

                _context.EventLogs.Add(new EventLog
                {
                    Timestamp = DateTime.UtcNow,
                    UserName = user.Name,
                    Action = "Password reset"
                });
                await _context.SaveChangesAsync();

                return Ok(new { message = "Password has been reset successfully." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Password reset failed");
                return BadRequest("Password reset failed.");
            }
        }

        [HttpPost("google")]
        public async Task<IActionResult> GoogleAuth([FromBody] GoogleAuthRequest request)
        {
            try
            {
                _logger.LogInformation("Google authentication attempt started");
                
                var clientId = _configuration["Google:ClientId"];
                if (string.IsNullOrEmpty(clientId))
                {
                    _logger.LogError("Google Client ID not configured");
                    return BadRequest("Google Client ID not configured");
                }

                var payload = await GoogleJsonWebSignature.ValidateAsync(request.Credential, new GoogleJsonWebSignature.ValidationSettings
                {
                    Audience = new[] { clientId }
                });

                _logger.LogInformation("Google token validated for user {Email}", payload.Email);

                var normalizedEmail = payload.Email.Trim().ToLowerInvariant();
                var user = await _context.Users.FirstOrDefaultAsync(u => u.GoogleId == payload.Subject)
                    ?? await _context.Users.FirstOrDefaultAsync(u => u.Email == normalizedEmail);

                if (user == null)
                {
                    user = new User
                    {
                        GoogleId = payload.Subject,
                        Email = normalizedEmail,
                        Name = payload.Name,
                        PictureUrl = payload.Picture,
                        CreatedAt = DateTime.UtcNow,
                        LastLoginAt = DateTime.UtcNow
                    };
                    _context.Users.Add(user);
                    _logger.LogInformation("New user created: {Email} with Google ID {GoogleId}", payload.Email, payload.Subject);
                }
                else
                {
                    if (string.IsNullOrWhiteSpace(user.GoogleId))
                        user.GoogleId = payload.Subject;
                    user.LastLoginAt = DateTime.UtcNow;
                    user.Name = payload.Name;
                    user.PictureUrl = payload.Picture;
                    _logger.LogInformation("Existing user login: {Email} (ID: {UserId})", user.Email, user.Id);
                }

                _context.EventLogs.Add(new EventLog
                {
                    Timestamp = DateTime.UtcNow,
                    UserName = user.Name,
                    Action = "Login"
                });

                await _context.SaveChangesAsync();

                var token = _jwtService.GenerateToken(user);

                _logger.LogInformation("Authentication successful for user {Email}", user.Email);

                return Ok(BuildAuthResponse(user, token));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Authentication failed for Google credential");
                return BadRequest($"Authentication failed: {ex.Message}");
            }
        }

        private static object BuildAuthResponse(User user, string token) => new
        {
            token,
            user = new
            {
                id = user.Id,
                email = user.Email,
                name = user.Name,
                picture = user.PictureUrl,
                role = user.Role.ToString(),
                chargingUserId = user.ChargingUserId
            }
        };

        private static string GenerateResetToken()
        {
            var bytes = RandomNumberGenerator.GetBytes(32);
            return Convert.ToBase64String(bytes)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
        }

        private static string HashToken(string token)
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
            return Convert.ToHexString(bytes);
        }

        private string BuildResetLink(string? requestBaseUrl, string token)
        {
            var baseUrl = requestBaseUrl;
            if (string.IsNullOrWhiteSpace(baseUrl))
                baseUrl = _configuration["Auth:PasswordResetBaseUrl"];
            if (string.IsNullOrWhiteSpace(baseUrl))
                baseUrl = $"{Request.Scheme}://{Request.Host}";

            if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                throw new InvalidOperationException("Invalid reset base URL.");
            }

            return $"{uri.ToString().TrimEnd('/')}/reset-password?token={Uri.EscapeDataString(token)}";
        }

        [HttpPost("logout")]
        public async Task<IActionResult> Logout()
        {
            var token = Request.Headers.Authorization.ToString().Replace("Bearer ", "");
            var userIdString = _jwtService.ValidateToken(token);

            if (userIdString != null && int.TryParse(userIdString, out int userId))
            {
                var user = await _context.Users.FindAsync(userId);
                if (user != null)
                {
                    _context.EventLogs.Add(new EventLog
                    {
                        Timestamp = DateTime.UtcNow,
                        UserName = user.Name,
                        Action = "Logout"
                    });
                    await _context.SaveChangesAsync();
                }
            }

            return Ok();
        }

        [HttpGet("user")]
        public async Task<IActionResult> GetUser()
        {
            try
            {
                var token = Request.Headers.Authorization.ToString().Replace("Bearer ", "");
                var userIdString = _jwtService.ValidateToken(token);
                
                if (userIdString == null || !int.TryParse(userIdString, out int userId))
                {
                    _logger.LogWarning("Invalid token provided for user lookup");
                    return Unauthorized();
                }

                var user = await _context.Users.FindAsync(userId);
                if (user == null)
                {
                    _logger.LogWarning("User not found for ID {UserId}", userId);
                    return NotFound();
                }

                _logger.LogInformation("User profile retrieved for {Email} (ID: {UserId})", user.Email, user.Id);

                return Ok(new
                {
                    id = user.Id,
                    email = user.Email,
                    name = user.Name,
                    picture = user.PictureUrl,
                    role = user.Role.ToString(),
                    chargingUserId = user.ChargingUserId
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving user profile");
                return Unauthorized();
            }
        }

        [HttpGet("users")]
        public async Task<IActionResult> GetAllUsers()
        {
            try
            {
                var token = Request.Headers.Authorization.ToString().Replace("Bearer ", "");
                var userIdString = _jwtService.ValidateToken(token);
                
                if (userIdString == null || !int.TryParse(userIdString, out int userId))
                {
                    _logger.LogWarning("Unauthorized attempt to access users list");
                    return Unauthorized();
                }

                var requestingUser = await _context.Users.FindAsync(userId);
                if (requestingUser == null || requestingUser.Role != UserRole.Admin)
                {
                    _logger.LogWarning("Non-admin user {UserId} attempted to access users list", userId);
                    return Forbid();
                }

                _logger.LogInformation("Admin {UserId} requesting all users list", userId);

                var users = await _context.Users
                    .OrderByDescending(u => u.CreatedAt)
                    .Select(u => new
                    {
                        id = u.Id,
                        email = u.Email,
                        name = u.Name,
                        picture = u.PictureUrl,
                        role = u.Role.ToString(),
                        chargingUserId = u.ChargingUserId,
                        createdAt = u.CreatedAt,
                        lastLoginAt = u.LastLoginAt
                    })
                    .ToListAsync();

                _logger.LogInformation("Retrieved {UserCount} users for user {RequestingUserId}", users.Count, userId);

                return Ok(users);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving users list");
                return Unauthorized();
            }
        }

        /// <summary>
        /// Permanently removes charging sessions, invoices, Mollie payments, energy prices, charging user profiles, and event logs.
        /// User accounts are preserved; each user's charging profile link is cleared.
        /// Requires an authenticated admin and <c>{ "confirm": true }</c> in the JSON body.
        /// </summary>
        [HttpPost("admin/wipe-database")]
        public async Task<IActionResult> WipeDatabase([FromBody] WipeDatabaseRequest? request)
        {
            try
            {
                if (request == null || !request.Confirm)
                {
                    return BadRequest(new { message = "Send JSON body { \"confirm\": true } to wipe the database." });
                }

                var token = Request.Headers.Authorization.ToString().Replace("Bearer ", "");
                var userIdString = _jwtService.ValidateToken(token);

                if (userIdString == null || !int.TryParse(userIdString, out int userId))
                {
                    _logger.LogWarning("Unauthorized attempt to wipe database");
                    return Unauthorized();
                }

                var requestingUser = await _context.Users.FindAsync(userId);
                if (requestingUser == null || requestingUser.Role != UserRole.Admin)
                {
                    _logger.LogWarning("Non-admin user {UserId} attempted database wipe", userId);
                    return Forbid();
                }

                _logger.LogWarning(
                    "Application data wipe (users preserved) executed by admin {AdminId} ({Email})",
                    userId,
                    requestingUser.Email);

                // RemoveRange (not ExecuteDelete) so SQLite and InMemory test providers behave consistently.
                var deletedInvoices = await _context.Invoices.CountAsync();
                var deletedMollie = await _context.MolliePayments.CountAsync();
                var deletedEvents = await _context.EventLogs.CountAsync();
                var deletedSessions = await _context.ChargingSessions.CountAsync();
                var deletedPrices = await _context.EnergyPrices.CountAsync();
                var deletedProfiles = await _context.ChargingUserProfiles.CountAsync();
                var usersPreserved = await _context.Users.CountAsync();

                foreach (var u in await _context.Users.ToListAsync())
                    u.ChargingUserId = null;

                _context.Invoices.RemoveRange(await _context.Invoices.ToListAsync());
                _context.MolliePayments.RemoveRange(await _context.MolliePayments.ToListAsync());
                _context.EventLogs.RemoveRange(await _context.EventLogs.ToListAsync());
                _context.ChargingSessions.RemoveRange(await _context.ChargingSessions.ToListAsync());
                _context.EnergyPrices.RemoveRange(await _context.EnergyPrices.ToListAsync());
                _context.ChargingUserProfiles.RemoveRange(await _context.ChargingUserProfiles.ToListAsync());

                // InMemory (used in tests) does not support explicit transactions the same way as SQLite.
                var useTransaction = _context.Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite";
                if (useTransaction)
                {
                    await using var transaction = await _context.Database.BeginTransactionAsync();
                    try
                    {
                        await _context.SaveChangesAsync();
                        await transaction.CommitAsync();
                    }
                    catch
                    {
                        await transaction.RollbackAsync();
                        throw;
                    }
                }
                else
                {
                    await _context.SaveChangesAsync();
                }

                return Ok(new WipeDatabaseResult
                {
                    Message = "Financial and operational records have been removed. User accounts were preserved.",
                    UsersPreserved = usersPreserved,
                    Deleted = new WipeDatabaseDeletedCounts
                    {
                        ChargingSessions = deletedSessions,
                        Invoices = deletedInvoices,
                        EventLogs = deletedEvents,
                        EnergyPrices = deletedPrices,
                        ChargingUserProfiles = deletedProfiles,
                        MolliePayments = deletedMollie
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during database wipe");
                return BadRequest(new { message = $"Database wipe failed: {ex.Message}" });
            }
        }

        public const string ClearChargingDataConfirmationPhrase = "CLEAR CHARGING DATA";

        /// <summary>
        /// Removes all charging sessions and charging user profiles, and clears app users' ChargingUserId links.
        /// Does not remove invoices, payments, or other tables. Admin only; requires confirmation phrase.
        /// </summary>
        [HttpPost("admin/clear-charging-data")]
        public async Task<IActionResult> ClearChargingData([FromBody] WipeDatabaseRequest? request)
        {
            try
            {
                if (request == null || request.Confirmation != ClearChargingDataConfirmationPhrase)
                {
                    return BadRequest(new
                    {
                        message = $"Type the confirmation phrase exactly: {ClearChargingDataConfirmationPhrase}"
                    });
                }

                var token = Request.Headers.Authorization.ToString().Replace("Bearer ", "");
                var userIdString = _jwtService.ValidateToken(token);

                if (userIdString == null || !int.TryParse(userIdString, out int userId))
                {
                    _logger.LogWarning("Unauthorized attempt to clear charging data");
                    return Unauthorized();
                }

                var requestingUser = await _context.Users.FindAsync(userId);
                if (requestingUser == null || requestingUser.Role != UserRole.Admin)
                {
                    _logger.LogWarning("Non-admin user {UserId} attempted clear charging data", userId);
                    return Forbid();
                }

                _logger.LogWarning(
                    "Charging sessions and profiles cleared by admin {AdminId} ({Email})",
                    userId,
                    requestingUser.Email);

                var removedSessions = await _context.ChargingSessions.CountAsync();
                var removedProfiles = await _context.ChargingUserProfiles.CountAsync();
                var appUsersUnlinked = await _context.Users.CountAsync(u => u.ChargingUserId != null);

                foreach (var u in await _context.Users.ToListAsync())
                    u.ChargingUserId = null;

                _context.ChargingSessions.RemoveRange(await _context.ChargingSessions.ToListAsync());
                _context.ChargingUserProfiles.RemoveRange(await _context.ChargingUserProfiles.ToListAsync());

                var useTransaction = _context.Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite";
                if (useTransaction)
                {
                    await using var transaction = await _context.Database.BeginTransactionAsync();
                    try
                    {
                        await _context.SaveChangesAsync();
                        await transaction.CommitAsync();
                    }
                    catch
                    {
                        await transaction.RollbackAsync();
                        throw;
                    }
                }
                else
                {
                    await _context.SaveChangesAsync();
                }

                _context.EventLogs.Add(new EventLog
                {
                    Timestamp = DateTime.UtcNow,
                    UserName = requestingUser.Name,
                    Action = $"Cleared charging data: {removedSessions} sessions, {removedProfiles} profiles, app users unlinked"
                });
                await _context.SaveChangesAsync();

                return Ok(new ClearChargingDataResult
                {
                    Message = "Charging sessions and charging user profiles removed. App users were unlinked from charging accounts.",
                    ChargingSessionsRemoved = removedSessions,
                    ChargingUserProfilesRemoved = removedProfiles,
                    AppUsersUnlinked = appUsersUnlinked
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error clearing charging data");
                return BadRequest(new { message = $"Clear charging data failed: {ex.Message}" });
            }
        }

        [Authorize(Roles = "Admin")]
        [HttpPut("users/{id}")]
        public async Task<IActionResult> UpdateUser(int id, [FromBody] UpdateUserRequest request)
        {
            try
            {
                var token = Request.Headers.Authorization.ToString().Replace("Bearer ", "");
                var userIdString = _jwtService.ValidateToken(token);
                
                if (userIdString == null || !int.TryParse(userIdString, out int currentUserId))
                {
                    return Unauthorized();
                }

                var user = await _context.Users.FindAsync(id);
                if (user == null)
                {
                    return NotFound("User not found");
                }

                var editor = await _context.Users.FindAsync(currentUserId);
                var editorName = editor?.Name ?? $"User {currentUserId}";

                var changes = new List<string>();

                // Update user fields
                if (!string.IsNullOrWhiteSpace(request.Name) && request.Name.Trim() != user.Name)
                {
                    changes.Add($"Name: '{user.Name}' → '{request.Name.Trim()}'");
                    user.Name = request.Name.Trim();
                }
                
                if (!string.IsNullOrWhiteSpace(request.Email) && request.Email.Trim() != user.Email)
                {
                    changes.Add($"Email: '{user.Email}' → '{request.Email.Trim()}'");
                    user.Email = request.Email.Trim();
                }

                // Allow role changes for any authenticated user
                if (!string.IsNullOrWhiteSpace(request.Role))
                {
                    if (Enum.TryParse<UserRole>(request.Role, true, out var newRole) && newRole != user.Role)
                    {
                        changes.Add($"Role: '{user.Role}' → '{newRole}'");
                        user.Role = newRole;
                        _logger.LogInformation("User {CurrentUserId} changed role of user {UserId} to {Role}", currentUserId, id, newRole);
                    }
                }

                if (changes.Any())
                {
                    _context.EventLogs.Add(new EventLog
                    {
                        Timestamp = DateTime.UtcNow,
                        UserName = editorName,
                        Action = $"Edited user '{user.Name}': {string.Join(", ", changes)}"
                    });
                }

                await _context.SaveChangesAsync();

                return Ok(new
                {
                    id = user.Id,
                    email = user.Email,
                    name = user.Name,
                    picture = user.PictureUrl,
                    role = user.Role.ToString(),
                    chargingUserId = user.ChargingUserId,
                    createdAt = user.CreatedAt,
                    lastLoginAt = user.LastLoginAt
                });
            }
            catch (Exception ex)
            {
                return BadRequest($"Update failed: {ex.Message}");
            }        }

        [Authorize(Roles = "Admin")]
        [HttpPut("users/{id}/role")]
        public async Task<IActionResult> UpdateUserRole(int id, [FromBody] UpdateRoleRequest request)
        {
            try
            {
                var token = Request.Headers.Authorization.ToString().Replace("Bearer ", "");
                var userIdString = _jwtService.ValidateToken(token);
                
                if (userIdString == null || !int.TryParse(userIdString, out int currentUserId))
                {
                    return Unauthorized();
                }

                // Allow any authenticated user to change user roles
                var user = await _context.Users.FindAsync(id);
                if (user == null)
                {
                    return NotFound("User not found");
                }

                if (!Enum.TryParse<UserRole>(request.Role, true, out var newRole))
                {
                    return BadRequest("Invalid role. Valid roles are: Base, Admin");
                }

                var editor = await _context.Users.FindAsync(currentUserId);
                var editorName = editor?.Name ?? $"User {currentUserId}";
                var oldRole = user.Role;

                user.Role = newRole;

                _context.EventLogs.Add(new EventLog
                {
                    Timestamp = DateTime.UtcNow,
                    UserName = editorName,
                    Action = $"Changed role of '{user.Name}': '{oldRole}' → '{newRole}'"
                });

                await _context.SaveChangesAsync();

                _logger.LogInformation("User {UserId} changed role of user {TargetUserId} to {Role}", currentUserId, id, newRole);

                return Ok(new
                {
                    id = user.Id,
                    email = user.Email,
                    name = user.Name,
                    picture = user.PictureUrl,
                    role = user.Role.ToString(),
                    createdAt = user.CreatedAt,
                    lastLoginAt = user.LastLoginAt
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating user role");
                return BadRequest($"Update failed: {ex.Message}");
            }
        }

        [HttpGet("charging-users")]
        public async Task<IActionResult> GetChargingUsers()
        {
            var token = Request.Headers.Authorization.ToString().Replace("Bearer ", "");
            if (_jwtService.ValidateToken(token) == null)
                return Unauthorized();

            var ids = await _context.ChargingSessions
                .Select(cs => cs.UserId)
                .Distinct()
                .OrderBy(id => id)
                .ToListAsync();

            var linkedMap = await _context.Users
                .Where(u => u.ChargingUserId != null)
                .Select(u => new { u.ChargingUserId, u.Name, UserId = u.Id })
                .ToListAsync();

            var profiles = await _context.ChargingUserProfiles
                .Where(p => ids.Contains(p.Id))
                .ToListAsync();

            var result = ids.Select(id =>
            {
                var linked = linkedMap.FirstOrDefault(l => l.ChargingUserId == id);
                var profile = profiles.FirstOrDefault(p => p.Id == id);
                return new
                {
                    id,
                    rfidTokenKey = profile?.RfidTokenKey,
                    displayName = profile?.DisplayName,
                    linkedUserId = linked?.UserId,
                    linkedUserName = linked?.Name
                };
            });

            return Ok(result);
        }

        // Convenience route so `/charging-users` (without `/api/auth` prefix) also works
        [HttpGet("/charging-users")]
        public Task<IActionResult> GetChargingUsersRoot() => GetChargingUsers();

        [HttpGet("charging-users/{id}")]
        public async Task<IActionResult> GetChargingUserById(int id)
        {
            var token = Request.Headers.Authorization.ToString().Replace("Bearer ", "");
            if (_jwtService.ValidateToken(token) == null)
                return Unauthorized();

            var sessionExists = await _context.ChargingSessions.AnyAsync(cs => cs.UserId == id);
            if (!sessionExists)
                return NotFound("Charging user not found");

            var profile = await _context.ChargingUserProfiles.FindAsync(id);
            var linkedUser = await _context.Users.FirstOrDefaultAsync(u => u.ChargingUserId == id);

            return Ok(new
            {
                id,
                rfidTokenKey = profile?.RfidTokenKey,
                displayName = profile?.DisplayName,
                street = profile?.Street,
                houseNumber = profile?.HouseNumber,
                postalCode = profile?.PostalCode,
                placeName = profile?.PlaceName,
                linkedUserId = linkedUser?.Id,
                linkedUserName = linkedUser?.Name
            });
        }

        [Authorize(Roles = "Admin")]
        [HttpPut("charging-users/{id}")]
        public async Task<IActionResult> UpdateChargingUser(int id, [FromBody] UpdateChargingUserRequest request)
        {
            var token = Request.Headers.Authorization.ToString().Replace("Bearer ", "");
            var editorIdString = _jwtService.ValidateToken(token);
            if (editorIdString == null || !int.TryParse(editorIdString, out int editorId))
                return Unauthorized();

            var sessionExists = await _context.ChargingSessions.AnyAsync(cs => cs.UserId == id);
            if (!sessionExists)
                return NotFound("Charging user not found");

            var editor = await _context.Users.FindAsync(editorId);
            var editorName = editor?.Name ?? $"User {editorId}";
            var changes = new List<string>();

            // Update or create display name
            var profile = await _context.ChargingUserProfiles.FindAsync(id);
            var oldDisplayName = profile?.DisplayName;
            if (request.DisplayName != oldDisplayName)
            {
                if (profile == null)
                {
                    profile = new Backend.Models.ChargingUserProfile { Id = id };
                    _context.ChargingUserProfiles.Add(profile);
                }
                profile.DisplayName = string.IsNullOrWhiteSpace(request.DisplayName) ? null : request.DisplayName.Trim();
                changes.Add($"DisplayName: '{oldDisplayName ?? "none"}' → '{profile.DisplayName ?? "none"}'");
            }

            // Update address fields
            var newStreet = string.IsNullOrWhiteSpace(request.Street) ? null : request.Street.Trim();
            var newHouseNumber = string.IsNullOrWhiteSpace(request.HouseNumber) ? null : request.HouseNumber.Trim();
            var newPostalCode = string.IsNullOrWhiteSpace(request.PostalCode) ? null : request.PostalCode.Trim().ToUpper().Replace(" ", "");
            var newPlaceName = string.IsNullOrWhiteSpace(request.PlaceName) ? null : request.PlaceName.Trim();

            if (newStreet != profile?.Street || newHouseNumber != profile?.HouseNumber ||
                newPostalCode != profile?.PostalCode || newPlaceName != profile?.PlaceName)
            {
                if (profile == null)
                {
                    profile = new Backend.Models.ChargingUserProfile { Id = id };
                    _context.ChargingUserProfiles.Add(profile);
                }
                profile.Street = newStreet;
                profile.HouseNumber = newHouseNumber;
                profile.PostalCode = newPostalCode;
                profile.PlaceName = newPlaceName;
                changes.Add($"Address updated");
            }

            // Handle user linking
            if (request.UnlinkUser)
            {
                var currentlyLinked = await _context.Users.FirstOrDefaultAsync(u => u.ChargingUserId == id);
                if (currentlyLinked != null)
                {
                    changes.Add($"Unlinked user '{currentlyLinked.Name}'");
                    currentlyLinked.ChargingUserId = null;
                }
            }
            else if (request.LinkedUserId.HasValue)
            {
                // Unlink anyone currently linked to this charging user
                var currentlyLinked = await _context.Users.FirstOrDefaultAsync(u => u.ChargingUserId == id);
                if (currentlyLinked != null && currentlyLinked.Id != request.LinkedUserId.Value)
                {
                    currentlyLinked.ChargingUserId = null;
                }

                var newLinkedUser = await _context.Users.FindAsync(request.LinkedUserId.Value);
                if (newLinkedUser == null)
                    return NotFound("Linked user not found");

                if (newLinkedUser.ChargingUserId != id)
                {
                    changes.Add($"Linked user: '{newLinkedUser.Name}'");
                    newLinkedUser.ChargingUserId = id;
                }
            }

            if (changes.Any())
            {
                _context.EventLogs.Add(new EventLog
                {
                    Timestamp = DateTime.UtcNow,
                    UserName = editorName,
                    Action = $"Edited charging user {id}: {string.Join(", ", changes)}"
                });
            }

            await _context.SaveChangesAsync();

            var linkedUserAfter = await _context.Users.FirstOrDefaultAsync(u => u.ChargingUserId == id);
            var profileAfter = await _context.ChargingUserProfiles.FindAsync(id);

            return Ok(new
            {
                id,
                rfidTokenKey = profileAfter?.RfidTokenKey,
                displayName = profileAfter?.DisplayName,
                street = profileAfter?.Street,
                houseNumber = profileAfter?.HouseNumber,
                postalCode = profileAfter?.PostalCode,
                placeName = profileAfter?.PlaceName,
                linkedUserId = linkedUserAfter?.Id,
                linkedUserName = linkedUserAfter?.Name
            });
        }

        [HttpGet("users/{id}")]
        public async Task<IActionResult> GetUserById(int id)
        {
            try
            {
                var token = Request.Headers.Authorization.ToString().Replace("Bearer ", "");
                var userIdString = _jwtService.ValidateToken(token);
                
                if (userIdString == null || !int.TryParse(userIdString, out int currentUserId))
                {
                    return Unauthorized();
                }

                var user = await _context.Users.FindAsync(id);
                if (user == null)
                {
                    return NotFound("User not found");
                }

                return Ok(new
                {
                    id = user.Id,
                    email = user.Email,
                    name = user.Name,
                    picture = user.PictureUrl,
                    role = user.Role.ToString(),
                    chargingUserId = user.ChargingUserId,
                    createdAt = user.CreatedAt,
                    lastLoginAt = user.LastLoginAt
                });
            }
            catch
            {
                return Unauthorized();
            }
        }

        [HttpGet("github")]
        public IActionResult GitHubLogin()
        {
            var clientId = _configuration["GitHub:ClientId"]
                ?? throw new InvalidOperationException("GitHub ClientId not configured");
            var callbackUrl = _configuration["GitHub:CallbackUrl"]
                ?? throw new InvalidOperationException("GitHub CallbackUrl not configured");

            var url = $"https://github.com/login/oauth/authorize?client_id={clientId}&redirect_uri={Uri.EscapeDataString(callbackUrl)}&scope=user:email";
            return Redirect(url);
        }

        [HttpGet("github/callback")]
        public async Task<IActionResult> GitHubCallback([FromQuery] string? code, [FromQuery] string? error)
        {
            var frontendUrl = _configuration["GitHub:FrontendUrl"] ?? "http://localhost:3000";

            if (!string.IsNullOrEmpty(error) || string.IsNullOrEmpty(code))
            {
                _logger.LogWarning("GitHub OAuth error: {Error}", error);
                return Redirect($"{frontendUrl}?auth_error=github_denied");
            }

            try
            {
                var clientId = _configuration["GitHub:ClientId"]!;
                var clientSecret = _configuration["GitHub:ClientSecret"]!;

                // Exchange code for access token
                using var http = new HttpClient();
                http.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
                http.DefaultRequestHeaders.UserAgent.ParseAdd("ZonnewijzerFinance/1.0");

                var tokenResponse = await http.PostAsync(
                    "https://github.com/login/oauth/access_token",
                    new FormUrlEncodedContent(new Dictionary<string, string>
                    {
                        ["client_id"] = clientId,
                        ["client_secret"] = clientSecret,
                        ["code"] = code
                    }));

                var tokenJson = await tokenResponse.Content.ReadAsStringAsync();
                using var tokenDoc = System.Text.Json.JsonDocument.Parse(tokenJson);
                var accessToken = tokenDoc.RootElement.GetProperty("access_token").GetString();

                if (string.IsNullOrEmpty(accessToken))
                {
                    _logger.LogError("GitHub OAuth: no access token in response: {Body}", tokenJson);
                    return Redirect($"{frontendUrl}?auth_error=github_token_failed");
                }

                // Fetch user info
                http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
                var userResponse = await http.GetAsync("https://api.github.com/user");
                var userJson = await userResponse.Content.ReadAsStringAsync();
                using var userDoc = System.Text.Json.JsonDocument.Parse(userJson);
                var root = userDoc.RootElement;

                var githubId = root.GetProperty("id").GetInt64().ToString();
                var githubName = root.TryGetProperty("name", out var nameProp) && nameProp.ValueKind != System.Text.Json.JsonValueKind.Null
                    ? nameProp.GetString() ?? root.GetProperty("login").GetString()!
                    : root.GetProperty("login").GetString()!;
                var githubAvatar = root.TryGetProperty("avatar_url", out var avProp) ? avProp.GetString() : null;

                // Fetch primary verified email
                var emailResponse = await http.GetAsync("https://api.github.com/user/emails");
                var emailJson = await emailResponse.Content.ReadAsStringAsync();
                using var emailDoc = System.Text.Json.JsonDocument.Parse(emailJson);
                var email = emailDoc.RootElement.EnumerateArray()
                    .Where(e => e.TryGetProperty("primary", out var p) && p.GetBoolean() &&
                                e.TryGetProperty("verified", out var v) && v.GetBoolean())
                    .Select(e => e.GetProperty("email").GetString())
                    .FirstOrDefault();

                if (string.IsNullOrEmpty(email))
                {
                    _logger.LogError("GitHub OAuth: no verified primary email for user {GithubId}", githubId);
                    return Redirect($"{frontendUrl}?auth_error=github_no_email");
                }
                email = email.Trim().ToLowerInvariant();

                // Find existing user by GitHubId or email
                var user = await _context.Users.FirstOrDefaultAsync(u => u.GitHubId == githubId)
                        ?? await _context.Users.FirstOrDefaultAsync(u => u.Email == email);

                if (user == null)
                {
                    user = new User
                    {
                        GoogleId = null,
                        GitHubId = githubId,
                        Email = email,
                        Name = githubName,
                        PictureUrl = githubAvatar,
                        CreatedAt = DateTime.UtcNow,
                        LastLoginAt = DateTime.UtcNow
                    };
                    _context.Users.Add(user);
                    _logger.LogInformation("New user created via GitHub: {Email}", email);
                }
                else
                {
                    user.GitHubId = githubId;
                    user.LastLoginAt = DateTime.UtcNow;
                    if (string.IsNullOrEmpty(user.PictureUrl)) user.PictureUrl = githubAvatar;
                }

                _context.EventLogs.Add(new EventLog
                {
                    Timestamp = DateTime.UtcNow,
                    UserName = user.Name,
                    Action = "Login (GitHub)"
                });

                await _context.SaveChangesAsync();

                var jwt = _jwtService.GenerateToken(user);
                return Redirect($"{frontendUrl}?github_token={Uri.EscapeDataString(jwt)}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GitHub OAuth callback failed");
                return Redirect($"{frontendUrl}?auth_error=github_error");
            }
        }
    }
}