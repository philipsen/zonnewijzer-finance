using System.Text.Json;
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

public class ChargingStationsControllerTests : IDisposable
{
    private readonly ApplicationDbContext _db;
    private readonly Mock<IJwtService> _jwtMock;

    public ChargingStationsControllerTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new ApplicationDbContext(options);
        _jwtMock = new Mock<IJwtService>();
    }

    public void Dispose() => _db.Dispose();

    private ChargingStationsController BuildController(string? userId = "1")
    {
        _jwtMock.Setup(j => j.ValidateToken(It.IsAny<string>())).Returns(userId);
        var controller = new ChargingStationsController(_db, _jwtMock.Object, NullLogger<ChargingStationsController>.Instance);
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers.Authorization = "Bearer t";
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        return controller;
    }

    [Fact]
    public async Task GetAll_InvalidToken_ReturnsUnauthorized()
    {
        var result = await BuildController(userId: null).GetAll();
        Assert.IsType<UnauthorizedResult>(result);
    }

    [Fact]
    public async Task GetAll_NonAdmin_ReturnsForbid()
    {
        _db.Users.Add(new User { Id = 1, Email = "u@test.com", Name = "U", GoogleId = "g1", Role = UserRole.Base });
        await _db.SaveChangesAsync();

        var result = await BuildController().GetAll();
        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task GetAll_Admin_ReturnsStationsAggregatedBySerialAndConnector()
    {
        _db.Users.Add(new User { Id = 1, Email = "a@test.com", Name = "Admin", GoogleId = "g1", Role = UserRole.Admin });
        var t1 = new DateTime(2026, 3, 1, 10, 0, 0, DateTimeKind.Utc);
        var t2 = new DateTime(2026, 3, 2, 10, 0, 0, DateTimeKind.Utc);
        _db.ChargingSessions.AddRange(
            new ChargingSession
            {
                UserId = 10,
                StationId = 1,
                Start = t1,
                End = t1.AddHours(1),
                ChargedKwh = 5,
                StationSerialNumber = "6531001272",
                Connector = "1",
                ChargingStationName = "Paal 2"
            },
            new ChargingSession
            {
                UserId = 10,
                StationId = 1,
                Start = t2,
                End = t2.AddHours(1),
                ChargedKwh = 3,
                StationSerialNumber = "6531001272",
                Connector = "1",
                ChargingStationName = "Paal 2"
            },
            new ChargingSession
            {
                UserId = 10,
                StationId = 2,
                Start = t1,
                End = t1.AddHours(1),
                ChargedKwh = 2,
                StationSerialNumber = "6531001272",
                Connector = "2",
                ChargingStationName = "Paal 2"
            }
        );
        await _db.SaveChangesAsync();

        var result = await BuildController().GetAll();

        var ok = Assert.IsType<OkObjectResult>(result);
        var json = JsonSerializer.Serialize(ok.Value, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        using var doc = JsonDocument.Parse(json);
        var stations = doc.RootElement.GetProperty("stations");
        Assert.Equal(2, stations.GetArrayLength());
    }

    [Fact]
    public async Task GetAll_Admin_IncludesLegacyBucket()
    {
        _db.Users.Add(new User { Id = 1, Email = "a@test.com", Name = "Admin", GoogleId = "g1", Role = UserRole.Admin });
        var t = new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        _db.ChargingSessions.Add(new ChargingSession
        {
            UserId = 5,
            StationId = 99,
            Start = t,
            End = t.AddHours(1),
            ChargedKwh = 1m
        });
        await _db.SaveChangesAsync();

        var result = await BuildController().GetAll();

        var ok = Assert.IsType<OkObjectResult>(result);
        var json = JsonSerializer.Serialize(ok.Value, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        using var doc = JsonDocument.Parse(json);
        var first = doc.RootElement.GetProperty("stations").EnumerateArray().First();
        Assert.True(first.GetProperty("isLegacy").GetBoolean());
        Assert.Equal(99, first.GetProperty("stationId").GetInt32());
    }
}
