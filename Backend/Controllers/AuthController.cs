using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Google.Apis.Auth;
using Backend.Data;
using Backend.Models;
using Backend.Services;

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

        public AuthController(ApplicationDbContext context, IJwtService jwtService, IConfiguration configuration, ILogger<AuthController> logger)
        {
            _context = context;
            _jwtService = jwtService;
            _configuration = configuration;
            _logger = logger;
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

                var user = await _context.Users.FirstOrDefaultAsync(u => u.GoogleId == payload.Subject);

                if (user == null)
                {
                    user = new User
                    {
                        GoogleId = payload.Subject,
                        Email = payload.Email,
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

                return Ok(new
                {
                    token,
                    user = new
                    {
                        id = user.Id,
                        email = user.Email,
                        name = user.Name,
                        picture = user.PictureUrl,
                        role = user.Role.ToString()
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Authentication failed for Google credential");
                return BadRequest($"Authentication failed: {ex.Message}");
            }
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
                    role = user.Role.ToString()
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

                _logger.LogInformation("User {UserId} requesting all users list", userId);

                var users = await _context.Users
                    .OrderByDescending(u => u.CreatedAt)
                    .Select(u => new
                    {
                        id = u.Id,
                        email = u.Email,
                        name = u.Name,
                        picture = u.PictureUrl,
                        role = u.Role.ToString(),
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
                    createdAt = user.CreatedAt,
                    lastLoginAt = user.LastLoginAt
                });
            }
            catch
            {
                return Unauthorized();
            }
        }
    }
}