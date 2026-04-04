using Backend.Controllers;
using Backend.Data;
using Backend.Models;
using Backend.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Backend.Tests.Controllers;

public class AuthControllerTests : IDisposable
{
    private readonly ApplicationDbContext _db;
    private readonly Mock<IJwtService> _jwtMock;
    private readonly Mock<IConfiguration> _configMock;

    public AuthControllerTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new ApplicationDbContext(options);
        _jwtMock = new Mock<IJwtService>();
        _configMock = new Mock<IConfiguration>();
    }

    public void Dispose() => _db.Dispose();

    private AuthController BuildController(string? validUserId = "1")
    {
        _jwtMock.Setup(j => j.ValidateToken(It.IsAny<string>())).Returns(validUserId);
        var controller = new AuthController(_db, _jwtMock.Object, _configMock.Object, NullLogger<AuthController>.Instance);
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers.Authorization = "Bearer valid-token";
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        return controller;
    }

    private async Task<User> AddUser(int id, string name, UserRole role = UserRole.Base, int? chargingUserId = null)
    {
        var user = new User
        {
            Id = id,
            GoogleId = $"google-{id}",
            Email = $"user{id}@test.com",
            Name = name,
            Role = role,
            ChargingUserId = chargingUserId
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();
        return user;
    }

    // ── GetAllUsers ────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAllUsers_WithInvalidToken_ReturnsUnauthorized()
    {
        var result = await BuildController(validUserId: null).GetAllUsers();
        Assert.IsType<UnauthorizedResult>(result);
    }

    [Fact]
    public async Task GetAllUsers_ReturnsAllUsers()
    {
        await AddUser(1, "Alice");
        await AddUser(2, "Bob");
        var result = await BuildController().GetAllUsers();
        var ok = Assert.IsType<OkObjectResult>(result);
        var list = Assert.IsAssignableFrom<IEnumerable<object>>(ok.Value).ToList();
        Assert.Equal(2, list.Count);
    }

    [Fact]
    public async Task GetAllUsers_IncludesChargingUserId()
    {
        await AddUser(1, "Alice", chargingUserId: 42);
        var result = await BuildController().GetAllUsers();
        var ok = Assert.IsType<OkObjectResult>(result);
        var json = System.Text.Json.JsonSerializer.Serialize(ok.Value);
        var doc = System.Text.Json.JsonDocument.Parse(json);
        var first = doc.RootElement.EnumerateArray().First();
        Assert.Equal(42, first.GetProperty("chargingUserId").GetInt32());
    }

    // ── GetUserById ────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetUserById_WithInvalidToken_ReturnsUnauthorized()
    {
        var result = await BuildController(validUserId: null).GetUserById(1);
        Assert.IsType<UnauthorizedResult>(result);
    }

    [Fact]
    public async Task GetUserById_WhenNotFound_ReturnsNotFound()
    {
        await AddUser(1, "Alice");
        var result = await BuildController().GetUserById(999);
        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task GetUserById_WhenFound_ReturnsUserData()
    {
        await AddUser(1, "Alice", UserRole.Admin);
        var result = await BuildController().GetUserById(1);
        var ok = Assert.IsType<OkObjectResult>(result);
        var json = System.Text.Json.JsonSerializer.Serialize(ok.Value);
        var doc = System.Text.Json.JsonDocument.Parse(json);
        Assert.Equal("Alice", doc.RootElement.GetProperty("name").GetString());
        Assert.Equal("Admin", doc.RootElement.GetProperty("role").GetString());
    }

    // ── UpdateUser ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateUser_WithInvalidToken_ReturnsUnauthorized()
    {
        var result = await BuildController(validUserId: null).UpdateUser(1, new UpdateUserRequest { Name = "New" });
        Assert.IsType<UnauthorizedResult>(result);
    }

    [Fact]
    public async Task UpdateUser_WhenNotFound_ReturnsNotFound()
    {
        await AddUser(1, "Editor");
        var result = await BuildController().UpdateUser(999, new UpdateUserRequest { Name = "Ghost" });
        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task UpdateUser_UpdatesNameAndReturnsOk()
    {
        await AddUser(1, "Editor");
        await AddUser(2, "OldName");

        var result = await BuildController().UpdateUser(2, new UpdateUserRequest { Name = "NewName" });

        Assert.IsType<OkObjectResult>(result);
        var updated = await _db.Users.FindAsync(2);
        Assert.Equal("NewName", updated!.Name);
    }

    [Fact]
    public async Task UpdateUser_UpdatesRole()
    {
        await AddUser(1, "Editor", UserRole.Admin);
        await AddUser(2, "Target", UserRole.Base);

        var result = await BuildController().UpdateUser(2, new UpdateUserRequest { Role = "Admin" });

        Assert.IsType<OkObjectResult>(result);
        var updated = await _db.Users.FindAsync(2);
        Assert.Equal(UserRole.Admin, updated!.Role);
    }

    // ── UpdateUserRole ─────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateUserRole_WithInvalidToken_ReturnsUnauthorized()
    {
        var result = await BuildController(validUserId: null).UpdateUserRole(1, new UpdateRoleRequest { Role = "Admin" });
        Assert.IsType<UnauthorizedResult>(result);
    }

    [Fact]
    public async Task UpdateUserRole_WhenUserNotFound_ReturnsNotFound()
    {
        await AddUser(1, "Editor");
        var result = await BuildController().UpdateUserRole(999, new UpdateRoleRequest { Role = "Admin" });
        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task UpdateUserRole_WithInvalidRole_ReturnsBadRequest()
    {
        await AddUser(1, "Editor");
        await AddUser(2, "Target");
        var result = await BuildController().UpdateUserRole(2, new UpdateRoleRequest { Role = "SuperAdmin" });
        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task UpdateUserRole_ChangesRole()
    {
        await AddUser(1, "Editor");
        await AddUser(2, "Target", UserRole.Base);

        var result = await BuildController().UpdateUserRole(2, new UpdateRoleRequest { Role = "Admin" });

        Assert.IsType<OkObjectResult>(result);
        var updated = await _db.Users.FindAsync(2);
        Assert.Equal(UserRole.Admin, updated!.Role);
    }

    // ── GetChargingUsers ───────────────────────────────────────────────────────

    [Fact]
    public async Task GetChargingUsers_WithInvalidToken_ReturnsUnauthorized()
    {
        var result = await BuildController(validUserId: null).GetChargingUsers();
        Assert.IsType<UnauthorizedResult>(result);
    }

    [Fact]
    public async Task GetChargingUsers_ReturnsDistinctIds()
    {
        _db.ChargingSessions.AddRange(
            new ChargingSession { UserId = 10, StationId = 1, Start = DateTime.UtcNow.AddHours(-2), End = DateTime.UtcNow.AddHours(-1), ChargedKwh = 5 },
            new ChargingSession { UserId = 10, StationId = 2, Start = DateTime.UtcNow.AddHours(-3), End = DateTime.UtcNow.AddHours(-2), ChargedKwh = 3 },
            new ChargingSession { UserId = 20, StationId = 1, Start = DateTime.UtcNow.AddHours(-1), End = DateTime.UtcNow, ChargedKwh = 7 }
        );
        await _db.SaveChangesAsync();

        var result = await BuildController().GetChargingUsers();

        var ok = Assert.IsType<OkObjectResult>(result);
        var list = Assert.IsAssignableFrom<IEnumerable<object>>(ok.Value).ToList();
        Assert.Equal(2, list.Count);
    }

    // ── GetChargingUserById ────────────────────────────────────────────────────

    [Fact]
    public async Task GetChargingUserById_WithInvalidToken_ReturnsUnauthorized()
    {
        var result = await BuildController(validUserId: null).GetChargingUserById(10);
        Assert.IsType<UnauthorizedResult>(result);
    }

    [Fact]
    public async Task GetChargingUserById_WhenNoSessions_ReturnsNotFound()
    {
        var result = await BuildController().GetChargingUserById(99);
        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task GetChargingUserById_WhenExists_ReturnsData()
    {
        _db.ChargingSessions.Add(new ChargingSession
        {
            UserId = 10, StationId = 1,
            Start = DateTime.UtcNow.AddHours(-1), End = DateTime.UtcNow, ChargedKwh = 5
        });
        _db.ChargingUserProfiles.Add(new ChargingUserProfile
        {
            Id = 10, DisplayName = "Charlie", Street = "Main St", HouseNumber = "1", PostalCode = "1234AB", PlaceName = "Amsterdam"
        });
        await _db.SaveChangesAsync();

        var result = await BuildController().GetChargingUserById(10);

        var ok = Assert.IsType<OkObjectResult>(result);
        var json = System.Text.Json.JsonSerializer.Serialize(ok.Value);
        var doc = System.Text.Json.JsonDocument.Parse(json);
        Assert.Equal("Charlie", doc.RootElement.GetProperty("displayName").GetString());
        Assert.Equal("Main St", doc.RootElement.GetProperty("street").GetString());
    }

    // ── UpdateChargingUser ─────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateChargingUser_WithInvalidToken_ReturnsUnauthorized()
    {
        var result = await BuildController(validUserId: null)
            .UpdateChargingUser(10, new UpdateChargingUserRequest { DisplayName = "Test" });
        Assert.IsType<UnauthorizedResult>(result);
    }

    [Fact]
    public async Task UpdateChargingUser_WhenNoSessions_ReturnsNotFound()
    {
        await AddUser(1, "Editor");
        var result = await BuildController()
            .UpdateChargingUser(99, new UpdateChargingUserRequest { DisplayName = "Ghost" });
        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task UpdateChargingUser_CreatesProfileIfMissing()
    {
        await AddUser(1, "Editor");
        _db.ChargingSessions.Add(new ChargingSession
        {
            UserId = 10, StationId = 1,
            Start = DateTime.UtcNow.AddHours(-1), End = DateTime.UtcNow, ChargedKwh = 5
        });
        await _db.SaveChangesAsync();

        var result = await BuildController()
            .UpdateChargingUser(10, new UpdateChargingUserRequest { DisplayName = "NewName" });

        Assert.IsType<OkObjectResult>(result);
        var profile = await _db.ChargingUserProfiles.FindAsync(10);
        Assert.NotNull(profile);
        Assert.Equal("NewName", profile.DisplayName);
    }

    [Fact]
    public async Task UpdateChargingUser_LinksAppUser()
    {
        await AddUser(1, "Editor");
        await AddUser(2, "AppUser");
        _db.ChargingSessions.Add(new ChargingSession
        {
            UserId = 10, StationId = 1,
            Start = DateTime.UtcNow.AddHours(-1), End = DateTime.UtcNow, ChargedKwh = 5
        });
        await _db.SaveChangesAsync();

        var result = await BuildController()
            .UpdateChargingUser(10, new UpdateChargingUserRequest { LinkedUserId = 2 });

        Assert.IsType<OkObjectResult>(result);
        var appUser = await _db.Users.FindAsync(2);
        Assert.Equal(10, appUser!.ChargingUserId);
    }

    [Fact]
    public async Task UpdateChargingUser_UnlinksAppUser()
    {
        await AddUser(1, "Editor");
        await AddUser(2, "AppUser", chargingUserId: 10);
        _db.ChargingSessions.Add(new ChargingSession
        {
            UserId = 10, StationId = 1,
            Start = DateTime.UtcNow.AddHours(-1), End = DateTime.UtcNow, ChargedKwh = 5
        });
        await _db.SaveChangesAsync();

        var result = await BuildController()
            .UpdateChargingUser(10, new UpdateChargingUserRequest { UnlinkUser = true });

        Assert.IsType<OkObjectResult>(result);
        var appUser = await _db.Users.FindAsync(2);
        Assert.Null(appUser!.ChargingUserId);
    }
}
