using Backend.Controllers;
using Backend.Data;
using Backend.Models;
using Backend.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
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

    [Fact]
    public async Task Register_CreatesPasswordUser_AndReturnsToken()
    {
        _jwtMock.Setup(j => j.GenerateToken(It.IsAny<User>())).Returns("jwt-token");
        var controller = BuildController();
        var result = await controller.Register(new RegisterRequest
        {
            Email = "newuser@test.com",
            Password = "Password123!",
            Name = "New User"
        });

        var ok = Assert.IsType<OkObjectResult>(result);
        var created = await _db.Users.FirstOrDefaultAsync(u => u.Email == "newuser@test.com");
        Assert.NotNull(created);
        Assert.False(string.IsNullOrWhiteSpace(created!.PasswordHash));
        var json = System.Text.Json.JsonSerializer.Serialize(ok.Value);
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        Assert.Equal("jwt-token", doc.RootElement.GetProperty("token").GetString());
    }

    [Fact]
    public async Task Login_WithValidPassword_ReturnsToken()
    {
        var user = await AddUser(1, "Local User");
        var hasher = new PasswordHasher<User>();
        user.PasswordHash = hasher.HashPassword(user, "Password123!");
        user.GoogleId = null;
        await _db.SaveChangesAsync();
        _jwtMock.Setup(j => j.GenerateToken(It.IsAny<User>())).Returns("jwt-login");

        var result = await BuildController().Login(new LoginRequest
        {
            Email = user.Email,
            Password = "Password123!"
        });

        var ok = Assert.IsType<OkObjectResult>(result);
        var json = System.Text.Json.JsonSerializer.Serialize(ok.Value);
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        Assert.Equal("jwt-login", doc.RootElement.GetProperty("token").GetString());
    }

    [Fact]
    public async Task Login_WithoutPasswordAccount_ReturnsUnauthorized()
    {
        await AddUser(1, "Google Only");
        var result = await BuildController().Login(new LoginRequest
        {
            Email = "user1@test.com",
            Password = "Password123!"
        });
        Assert.IsType<UnauthorizedObjectResult>(result);
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
        await AddUser(1, "Admin", UserRole.Admin);
        await AddUser(2, "Alice");
        await AddUser(3, "Bob");
        var result = await BuildController().GetAllUsers();
        var ok = Assert.IsType<OkObjectResult>(result);
        var list = Assert.IsAssignableFrom<IEnumerable<object>>(ok.Value).ToList();
        Assert.Equal(3, list.Count);
    }

    [Fact]
    public async Task GetAllUsers_IncludesChargingUserId()
    {
        await AddUser(1, "Admin", UserRole.Admin);
        await AddUser(2, "Alice", chargingUserId: 42);
        var result = await BuildController().GetAllUsers();
        var ok = Assert.IsType<OkObjectResult>(result);
        var json = System.Text.Json.JsonSerializer.Serialize(ok.Value);
        var doc = System.Text.Json.JsonDocument.Parse(json);
        var alice = doc.RootElement.EnumerateArray().First(e => e.GetProperty("id").GetInt32() == 2);
        Assert.Equal(42, alice.GetProperty("chargingUserId").GetInt32());
    }

    [Fact]
    public async Task GetAllUsers_NonAdmin_ReturnsForbid()
    {
        await AddUser(1, "Regular", UserRole.Base);
        await AddUser(2, "Other");
        var result = await BuildController().GetAllUsers();
        Assert.IsType<ForbidResult>(result);
    }

    // ── GetUser ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetUser_WithInvalidToken_ReturnsUnauthorized()
    {
        var result = await BuildController(validUserId: null).GetUser();
        Assert.IsType<UnauthorizedResult>(result);
    }

    [Fact]
    public async Task GetUser_WhenUserMissing_ReturnsNotFound()
    {
        _jwtMock.Setup(j => j.ValidateToken(It.IsAny<string>())).Returns("42");
        var controller = new AuthController(_db, _jwtMock.Object, _configMock.Object, NullLogger<AuthController>.Instance);
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers.Authorization = "Bearer valid-token";
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var result = await controller.GetUser();
        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task GetUser_WhenFound_ReturnsProfile()
    {
        await AddUser(1, "Pat", UserRole.Admin, chargingUserId: 7);
        var result = await BuildController().GetUser();
        var ok = Assert.IsType<OkObjectResult>(result);
        var json = System.Text.Json.JsonSerializer.Serialize(ok.Value);
        var doc = System.Text.Json.JsonDocument.Parse(json);
        Assert.Equal("Pat", doc.RootElement.GetProperty("name").GetString());
        Assert.Equal("Admin", doc.RootElement.GetProperty("role").GetString());
        Assert.Equal(7, doc.RootElement.GetProperty("chargingUserId").GetInt32());
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

    // ── WipeDatabase ───────────────────────────────────────────────────────────

    [Fact]
    public async Task WipeDatabase_InvalidToken_ReturnsUnauthorized()
    {
        var result = await BuildController(validUserId: null).WipeDatabase(
            new WipeDatabaseRequest { Confirm = true });
        Assert.IsType<UnauthorizedResult>(result);
    }

    [Fact]
    public async Task WipeDatabase_WithoutConfirm_ReturnsBadRequest()
    {
        await AddUser(1, "Admin", UserRole.Admin);
        var result = await BuildController().WipeDatabase(new WipeDatabaseRequest());
        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task WipeDatabase_NonAdmin_ReturnsForbid()
    {
        await AddUser(1, "Regular", UserRole.Base);
        var result = await BuildController().WipeDatabase(
            new WipeDatabaseRequest { Confirm = true });
        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task WipeDatabase_Admin_ClearsDataButPreservesUsers()
    {
        await AddUser(1, "Admin", UserRole.Admin, chargingUserId: 10);
        await AddUser(2, "Bob", UserRole.Base);
        var t = DateTime.UtcNow;
        _db.ChargingSessions.Add(new ChargingSession { UserId = 10, StationId = 1, Start = t, End = t.AddHours(1), ChargedKwh = 1 });
        _db.EventLogs.Add(new EventLog { Timestamp = DateTime.UtcNow, UserName = "x", Action = "y" });
        _db.EnergyPrices.Add(new EnergyPrice { Year = 2024, Month = 1, PricePerKwh = 0.2m });
        _db.ChargingUserProfiles.Add(new ChargingUserProfile { Id = 10, DisplayName = "P" });
        _db.MolliePayments.Add(new MolliePayment
        {
            ChargingUserId = 1,
            Year = 2024,
            Month = 1,
            MolliePaymentId = "tr",
            CheckoutUrl = "https://x",
            Amount = 1,
            Status = "open"
        });
        _db.Invoices.Add(new Invoice
        {
            UserId = 2,
            Year = 2024,
            Month = 1,
            InvoiceNumber = "INV-1",
            UserName = "Bob",
            TotalAmount = 1,
            PdfPath = null
        });
        await _db.SaveChangesAsync();

        var result = await BuildController().WipeDatabase(
            new WipeDatabaseRequest { Confirm = true });

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal(2, await _db.Users.CountAsync());
        Assert.Null((await _db.Users.FindAsync(1))!.ChargingUserId);
        Assert.Equal(0, await _db.ChargingSessions.CountAsync());
        Assert.Equal(0, await _db.EventLogs.CountAsync());
        Assert.Equal(0, await _db.Invoices.CountAsync());
    }

    // ── ClearChargingData ───────────────────────────────────────────────────────

    [Fact]
    public async Task ClearChargingData_WrongPhrase_ReturnsBadRequest()
    {
        await AddUser(1, "Admin", UserRole.Admin);
        var result = await BuildController().ClearChargingData(new WipeDatabaseRequest { Confirmation = "wrong" });
        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task ClearChargingData_NonAdmin_ReturnsForbid()
    {
        await AddUser(1, "Regular", UserRole.Base);
        var result = await BuildController().ClearChargingData(
            new WipeDatabaseRequest { Confirmation = AuthController.ClearChargingDataConfirmationPhrase });
        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task ClearChargingData_Admin_ClearsSessionsProfilesUnlinksAndKeepsInvoices()
    {
        await AddUser(1, "Admin", UserRole.Admin);
        await AddUser(2, "Bob", UserRole.Base, chargingUserId: 10);
        var t = DateTime.UtcNow;
        _db.ChargingSessions.Add(new ChargingSession { UserId = 10, StationId = 1, Start = t, End = t.AddHours(1), ChargedKwh = 2 });
        _db.ChargingUserProfiles.Add(new ChargingUserProfile { Id = 10, DisplayName = "Profile" });
        _db.Invoices.Add(new Invoice
        {
            UserId = 2,
            Year = 2024,
            Month = 3,
            InvoiceNumber = "INV-X",
            UserName = "Bob",
            TotalAmount = 5,
            PdfPath = null
        });
        await _db.SaveChangesAsync();

        var result = await BuildController().ClearChargingData(
            new WipeDatabaseRequest { Confirmation = AuthController.ClearChargingDataConfirmationPhrase });

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal(0, await _db.ChargingSessions.CountAsync());
        Assert.Equal(0, await _db.ChargingUserProfiles.CountAsync());
        Assert.Null((await _db.Users.FindAsync(2))!.ChargingUserId);
        Assert.Equal(1, await _db.Invoices.CountAsync());
    }
}
