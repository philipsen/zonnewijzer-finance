using System.Text.Json;
using Backend;
using Backend.Controllers;
using Backend.Data;
using Backend.Models;
using Backend.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Backend.Services.Smappee;

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
        var smappeeLive = new Mock<ISmappeeChargingLiveService>(MockBehavior.Loose);
        var smappeeCloud = new Mock<ISmappeeCloudClient>(MockBehavior.Loose);
        var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var smappeeOpt = Microsoft.Extensions.Options.Options.Create(new SmappeeOptions { ServiceLocationId = 0 });
        var controller = new ChargingStationsController(
            _db,
            _jwtMock.Object,
            smappeeLive.Object,
            smappeeCloud.Object,
            memoryCache,
            smappeeOpt,
            NullLogger<ChargingStationsController>.Instance);
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
    public async Task GetAll_Admin_GroupsSessionsBySerialNumber()
    {
        _db.Users.Add(new User { Id = 1, Email = "a@test.com", Name = "Admin", GoogleId = "g1", Role = UserRole.Admin });
        _db.ChargeCards.Add(new ChargeCard { Value = "RF-STATION-TEST" });
        await _db.SaveChangesAsync();
        var cid = _db.ChargeCards.Single().Id;

        var t1 = new DateTime(2026, 3, 1, 10, 0, 0, DateTimeKind.Utc);
        var t2 = new DateTime(2026, 3, 2, 10, 0, 0, DateTimeKind.Utc);
        var sidA = ChargingPointIds.ChargingPointIdFromSerialAndConnector("6531001272", "1");
        var sidB = ChargingPointIds.ChargingPointIdFromSerialAndConnector("6531001273", "1");
        _db.ChargingSessions.AddRange(
            TestData.Session(cid, sidA, t1, t1.AddHours(1), 5, "6531001272"),
            TestData.Session(cid, sidA, t2, t2.AddHours(1), 3, "6531001272"),
            TestData.Session(cid, sidB, t1, t1.AddHours(1), 2, "6531001273")
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
        _db.ChargeCards.Add(new ChargeCard { Value = "RF-LEGACY" });
        await _db.SaveChangesAsync();
        var cid = _db.ChargeCards.Single().Id;
        var t = new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        _db.ChargingSessions.Add(new ChargingSession
        {
            ChargeCardId = cid,
            StationId = 99,
            FromTimestamp = TestData.UtcMs(t),
            ToTimestamp = TestData.UtcMs(t.AddHours(1)),
            StartReading = 0,
            StopReading = 1m,
            SerialNumber = null,
            Rfid = "r",
            Status = "STOPPED"
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

    [Fact]
    public async Task GetSmappeeChargingStationDisplayNames_FiltersTypeAndCapsAtFive()
    {
        var now = DateTime.UtcNow;
        _db.SmappeeServiceLocations.Add(new SmappeeServiceLocation
        {
            ServiceLocationId = 1,
            UpdatedAtUtc = now,
            AppliancesJson =
                """[{"id":1,"name":"Paal A","type":"Charging station","sourceType":"CT"},{"id":2,"name":"PV","type":"Solar","sourceType":"x"}]"""
        });
        _db.SmappeeServiceLocations.Add(new SmappeeServiceLocation
        {
            ServiceLocationId = 2,
            UpdatedAtUtc = now,
            AppliancesJson = """[{"id":3,"name":"Paal B","type":"Charging station","sourceType":"CT"}]"""
        });
        await _db.SaveChangesAsync();

        var c = BuildController();
        var result = await c.GetSmappeeChargingStationDisplayNames(default);
        var ok = Assert.IsType<OkObjectResult>(result);
        var json = JsonSerializer.Serialize(ok.Value);
        using var doc = JsonDocument.Parse(json);
        var names = doc.RootElement.GetProperty("names");
        Assert.Equal(2, names.GetArrayLength());
        Assert.Equal("Paal A", names[0].GetString());
        Assert.Equal("Paal B", names[1].GetString());
    }

    [Fact]
    public async Task GetSmappeeChargingStationDisplayNames_OrdersByNameCaseInsensitive()
    {
        var now = DateTime.UtcNow;
        _db.SmappeeServiceLocations.Add(new SmappeeServiceLocation
        {
            ServiceLocationId = 2,
            UpdatedAtUtc = now,
            AppliancesJson = """[{"id":1,"name":"zebra","type":"Charging station","sourceType":"CT"}]"""
        });
        _db.SmappeeServiceLocations.Add(new SmappeeServiceLocation
        {
            ServiceLocationId = 1,
            UpdatedAtUtc = now,
            AppliancesJson = """[{"id":2,"name":"Alpha","type":"Charging station","sourceType":"CT"}]"""
        });
        await _db.SaveChangesAsync();

        var c = BuildController();
        var result = await c.GetSmappeeChargingStationDisplayNames(default);
        var ok = Assert.IsType<OkObjectResult>(result);
        var json = JsonSerializer.Serialize(ok.Value);
        using var doc = JsonDocument.Parse(json);
        var names = doc.RootElement.GetProperty("names");
        Assert.Equal(2, names.GetArrayLength());
        Assert.Equal("Alpha", names[0].GetString());
        Assert.Equal("zebra", names[1].GetString());
    }
}
