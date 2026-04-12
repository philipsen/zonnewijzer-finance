using Backend.Controllers;
using Backend.Data;
using Backend.Models;
using Backend.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Backend.Tests.Controllers;

public class MyChargingSessionsControllerTests : IDisposable
{
    private readonly ApplicationDbContext _db;
    private readonly Mock<IJwtService> _jwtMock;

    public MyChargingSessionsControllerTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new ApplicationDbContext(options);
        _jwtMock = new Mock<IJwtService>();
    }

    public void Dispose() => _db.Dispose();

    private MyChargingSessionsController BuildController(string? userId = "1")
    {
        _jwtMock.Setup(j => j.ValidateToken(It.IsAny<string>())).Returns(userId);
        var controller = new MyChargingSessionsController(_db, _jwtMock.Object, NullLogger<MyChargingSessionsController>.Instance);
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers.Authorization = "Bearer t";
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        return controller;
    }

    [Fact]
    public async Task GetAvailableMonths_InvalidToken_ReturnsUnauthorized()
    {
        var result = await BuildController(userId: null).GetAvailableMonths();
        Assert.IsType<UnauthorizedResult>(result);
    }

    [Fact]
    public async Task GetAvailableMonths_NoChargingUserId_ReturnsForbid()
    {
        _db.Users.Add(new User { Id = 1, Email = "a@test.com", Name = "A", GoogleId = "g1" });
        await _db.SaveChangesAsync();

        var result = await BuildController().GetAvailableMonths();
        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task GetAvailableMonths_ReturnsDistinctMonths()
    {
        _db.Users.Add(new User { Id = 1, Email = "a@test.com", Name = "A", GoogleId = "g1", ChargingUserId = 10 });
        var t = new DateTime(2024, 3, 15, 10, 0, 0, DateTimeKind.Utc);
        _db.ChargingSessions.AddRange(
            new ChargingSession { UserId = 10, StationId = 1, Start = t, End = t.AddHours(1), ChargedKwh = 5 },
            new ChargingSession { UserId = 10, StationId = 2, Start = t.AddDays(1), End = t.AddDays(1).AddHours(1), ChargedKwh = 3 }
        );
        await _db.SaveChangesAsync();

        var result = await BuildController().GetAvailableMonths();
        var ok = Assert.IsType<OkObjectResult>(result);
        var list = Assert.IsAssignableFrom<IEnumerable<object>>(ok.Value).ToList();
        Assert.Single(list);
    }

    [Fact]
    public async Task GetMySessions_InvalidToken_ReturnsUnauthorized()
    {
        var result = await BuildController(userId: null).GetMySessions();
        Assert.IsType<UnauthorizedResult>(result);
    }

    [Fact]
    public async Task GetMySessions_NoChargingUserId_ReturnsForbid()
    {
        _db.Users.Add(new User { Id = 1, Email = "a@test.com", Name = "A", GoogleId = "g1" });
        await _db.SaveChangesAsync();

        var result = await BuildController().GetMySessions();
        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task GetMySessions_InvalidMonth_ReturnsBadRequest()
    {
        _db.Users.Add(new User { Id = 1, Email = "a@test.com", Name = "A", GoogleId = "g1", ChargingUserId = 10 });
        await _db.SaveChangesAsync();

        var result = await BuildController().GetMySessions(year: 2024, month: 13);
        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task GetMySessions_ReturnsPagedSessions()
    {
        _db.Users.Add(new User { Id = 1, Email = "a@test.com", Name = "A", GoogleId = "g1", ChargingUserId = 10 });
        var t = DateTime.UtcNow.AddDays(-1);
        _db.ChargingSessions.Add(new ChargingSession { UserId = 10, StationId = 1, Start = t, End = t.AddHours(1), ChargedKwh = 4 });
        await _db.SaveChangesAsync();

        var result = await BuildController().GetMySessions(page: 1, pageSize: 10);
        var ok = Assert.IsType<OkObjectResult>(result);
        var json = System.Text.Json.JsonSerializer.Serialize(ok.Value);
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        Assert.Equal(1, doc.RootElement.GetProperty("pagination").GetProperty("totalSessions").GetInt32());
    }
}
