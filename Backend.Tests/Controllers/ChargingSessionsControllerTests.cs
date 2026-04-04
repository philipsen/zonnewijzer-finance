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

public class ChargingSessionsControllerTests : IDisposable
{
    private readonly ApplicationDbContext _db;
    private readonly Mock<IJwtService> _jwtMock;

    public ChargingSessionsControllerTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new ApplicationDbContext(options);
        _jwtMock = new Mock<IJwtService>();
    }

    public void Dispose() => _db.Dispose();

    private ChargingSessionsController BuildController(string? userId = "1")
    {
        _jwtMock.Setup(j => j.ValidateToken(It.IsAny<string>())).Returns(userId);
        var controller = new ChargingSessionsController(_db, _jwtMock.Object, NullLogger<ChargingSessionsController>.Instance);
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers.Authorization = "Bearer valid-token";
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        return controller;
    }

    private ChargingSession MakeSession(int userId = 1, int stationId = 10, decimal kwh = 5.0m, DateTime? start = null)
    {
        var s = start ?? DateTime.UtcNow.AddHours(-1);
        return new ChargingSession
        {
            Start = s,
            End = s.AddHours(1),
            UserId = userId,
            StationId = stationId,
            ChargedKwh = kwh
        };
    }

    // ── AddSession ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task AddSession_WithValidData_ReturnsOkAndPersists()
    {
        var controller = BuildController();
        var request = new AddSessionRequest
        {
            Start = DateTime.UtcNow.AddHours(-2),
            End = DateTime.UtcNow.AddHours(-1),
            UserId = 1,
            StationId = 5,
            ChargedKwh = 10.5m
        };

        var result = await controller.AddSession(request);

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal(1, await _db.ChargingSessions.CountAsync());
    }

    [Fact]
    public async Task AddSession_WhenEndBeforeStart_ReturnsBadRequest()
    {
        var controller = BuildController();
        var request = new AddSessionRequest
        {
            Start = DateTime.UtcNow,
            End = DateTime.UtcNow.AddHours(-1),
            UserId = 1,
            StationId = 5,
            ChargedKwh = 5m
        };

        var result = await controller.AddSession(request);

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Contains("End time", bad.Value!.ToString());
    }

    [Fact]
    public async Task AddSession_WithNegativeKwh_ReturnsBadRequest()
    {
        var controller = BuildController();
        var request = new AddSessionRequest
        {
            Start = DateTime.UtcNow.AddHours(-1),
            End = DateTime.UtcNow,
            UserId = 1,
            StationId = 5,
            ChargedKwh = -1m
        };

        var result = await controller.AddSession(request);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    // ── GetAllSessions ─────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAllSessions_WithInvalidToken_ReturnsUnauthorized()
    {
        var result = await BuildController(userId: null).GetAllSessions();
        Assert.IsType<UnauthorizedResult>(result);
    }

    [Fact]
    public async Task GetAllSessions_ReturnsPaginatedResults()
    {
        _db.ChargingSessions.AddRange(Enumerable.Range(1, 5).Select(i =>
            MakeSession(userId: i, start: DateTime.UtcNow.AddHours(-i))));
        await _db.SaveChangesAsync();

        var result = await BuildController().GetAllSessions(page: 1, pageSize: 3);

        var ok = Assert.IsType<OkObjectResult>(result);
        var json = System.Text.Json.JsonSerializer.Serialize(ok.Value);
        var doc = System.Text.Json.JsonDocument.Parse(json);
        var sessions = doc.RootElement.GetProperty("sessions").EnumerateArray();
        Assert.Equal(3, sessions.Count());
    }

    [Fact]
    public async Task GetAllSessions_WithMonthFilter_ReturnsOnlyMatchingSessions()
    {
        var jan = new DateTime(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc);
        var feb = new DateTime(2026, 2, 10, 12, 0, 0, DateTimeKind.Utc);
        _db.ChargingSessions.AddRange(
            MakeSession(start: jan),
            MakeSession(start: feb)
        );
        await _db.SaveChangesAsync();

        var result = await BuildController().GetAllSessions(year: 2026, month: 1);

        var ok = Assert.IsType<OkObjectResult>(result);
        var json = System.Text.Json.JsonSerializer.Serialize(ok.Value);
        var doc = System.Text.Json.JsonDocument.Parse(json);
        var sessions = doc.RootElement.GetProperty("sessions").EnumerateArray();
        Assert.Single(sessions);
    }

    [Fact]
    public async Task GetAllSessions_WithInvalidMonth_ReturnsBadRequest()
    {
        var result = await BuildController().GetAllSessions(year: 2026, month: 13);
        Assert.IsType<BadRequestObjectResult>(result);
    }

    // ── GetAvailableMonths ─────────────────────────────────────────────────────

    [Fact]
    public async Task GetAvailableMonths_WithInvalidToken_ReturnsUnauthorized()
    {
        var result = await BuildController(userId: null).GetAvailableMonths();
        Assert.IsType<UnauthorizedResult>(result);
    }

    [Fact]
    public async Task GetAvailableMonths_ReturnsDistinctMonths()
    {
        var jan = new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc);
        var feb = new DateTime(2026, 2, 5, 0, 0, 0, DateTimeKind.Utc);
        _db.ChargingSessions.AddRange(
            MakeSession(start: jan),
            MakeSession(start: jan.AddDays(5)), // same month
            MakeSession(start: feb)
        );
        await _db.SaveChangesAsync();

        var result = await BuildController().GetAvailableMonths();

        var ok = Assert.IsType<OkObjectResult>(result);
        var list = Assert.IsAssignableFrom<IEnumerable<object>>(ok.Value).ToList();
        Assert.Equal(2, list.Count);
    }

    // ── UpdateSession ──────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateSession_WhenNotFound_ReturnsNotFound()
    {
        var controller = BuildController();
        var request = new UpdateSessionRequest
        {
            Start = DateTime.UtcNow.AddHours(-2),
            End = DateTime.UtcNow.AddHours(-1),
            UserId = 1,
            StationId = 5,
            ChargedKwh = 5m
        };

        var result = await controller.UpdateSession(999, request);
        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task UpdateSession_WithValidData_UpdatesAndReturnsOk()
    {
        var session = MakeSession();
        _db.ChargingSessions.Add(session);
        await _db.SaveChangesAsync();

        var controller = BuildController();
        var request = new UpdateSessionRequest
        {
            Start = session.Start,
            End = session.End,
            UserId = 2,
            StationId = 99,
            ChargedKwh = 20m
        };

        var result = await controller.UpdateSession(session.Id, request);

        Assert.IsType<OkObjectResult>(result);
        var updated = await _db.ChargingSessions.FindAsync(session.Id);
        Assert.Equal(20m, updated!.ChargedKwh);
        Assert.Equal(99, updated.StationId);
    }

    [Fact]
    public async Task UpdateSession_WhenEndBeforeStart_ReturnsBadRequest()
    {
        var controller = BuildController();
        var request = new UpdateSessionRequest
        {
            Start = DateTime.UtcNow,
            End = DateTime.UtcNow.AddHours(-1),
            UserId = 1,
            StationId = 5,
            ChargedKwh = 5m
        };

        var result = await controller.UpdateSession(1, request);
        Assert.IsType<BadRequestObjectResult>(result);
    }

    // ── DeleteSession ──────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteSession_WhenNotFound_ReturnsNotFound()
    {
        var result = await BuildController().DeleteSession(999);
        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task DeleteSession_WhenExists_RemovesAndReturnsOk()
    {
        var session = MakeSession();
        _db.ChargingSessions.Add(session);
        await _db.SaveChangesAsync();

        var result = await BuildController().DeleteSession(session.Id);

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal(0, await _db.ChargingSessions.CountAsync());
    }
}
