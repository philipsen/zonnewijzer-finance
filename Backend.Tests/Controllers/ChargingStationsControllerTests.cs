using System.Text.Json;
using Backend;
using Backend.Controllers;
using Backend.Data;
using Backend.Models;
using Backend.Services;
using Backend.Services.IntegrationDaemon;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Backend.Tests.Controllers;

public class ChargingStationsControllerTests : IDisposable
{
    private readonly ApplicationDbContext _db;
    private readonly Mock<IJwtService> _jwtMock;
    private readonly Mock<IIntegrationDaemonClient> _daemonMock;

    public ChargingStationsControllerTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new ApplicationDbContext(options);
        _jwtMock = new Mock<IJwtService>();
        _daemonMock = new Mock<IIntegrationDaemonClient>();
    }

    public void Dispose() => _db.Dispose();

    private ChargingStationsController BuildController(string? userId = "1")
    {
        _jwtMock.Setup(j => j.ValidateToken(It.IsAny<string>())).Returns(userId);
        var controller = new ChargingStationsController(
            _db,
            _jwtMock.Object,
            _daemonMock.Object,
            Options.Create(new Backend.Services.Smappee.SmappeeOptions()),
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
        var json = JsonSerializer.Serialize(ok.Value, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        using var doc = JsonDocument.Parse(json);
        var squares = doc.RootElement.GetProperty("squares");
        Assert.Equal(2, squares.GetArrayLength());
        Assert.Equal("Paal A", squares[0].GetProperty("name").GetString());
        Assert.Equal("Paal B", squares[1].GetProperty("name").GetString());
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
        var json = JsonSerializer.Serialize(ok.Value, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        using var doc = JsonDocument.Parse(json);
        var squares = doc.RootElement.GetProperty("squares");
        Assert.Equal(2, squares.GetArrayLength());
        Assert.Equal("Alpha", squares[0].GetProperty("name").GetString());
        Assert.Equal("zebra", squares[1].GetProperty("name").GetString());
    }

    [Fact]
    public async Task GetSmappeeChargingStationDisplayNames_UsesLiveSnapshotByNormalizedStationName()
    {
        var now = DateTime.UtcNow;
        _db.SmappeeServiceLocations.Add(new SmappeeServiceLocation
        {
            ServiceLocationId = 1,
            UpdatedAtUtc = now,
            AppliancesJson = """[{"id":1,"name":"Paal 4","type":"Charging station","sourceType":"CT"}]"""
        });
        _db.SmappeeStatusSnapshots.Add(new SmappeeStatusSnapshot
        {
            ServiceLocationId = 1,
            FetchedAtUtc = now,
            IsSuccess = true,
            PayloadJson = """
                          {
                            "FetchedAt":"2026-04-27T14:00:00Z",
                            "ServiceLocationId":1,
                            "Points":[
                              {
                                "SerialNumber":"6531001274",
                                "StationName":"Paal 4 - 1",
                                "UiState":"charging"
                              }
                            ],
                            "RangeStartMs":0,
                            "RangeEndMsExclusive":0
                          }
                          """
        });
        await _db.SaveChangesAsync();

        var result = await BuildController().GetSmappeeChargingStationDisplayNames(default);

        var ok = Assert.IsType<OkObjectResult>(result);
        var json = JsonSerializer.Serialize(ok.Value, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        using var doc = JsonDocument.Parse(json);
        var square = Assert.Single(doc.RootElement.GetProperty("squares").EnumerateArray());
        Assert.Equal("Paal 4", square.GetProperty("name").GetString());
        Assert.Equal(ChargingStationHomeSquareDto.LiveStateActive, square.GetProperty("liveState").GetString());
    }

    [Fact]
    public async Task GetSmappeeChargingStationDisplayNames_PrefersActiveWhenDuplicateLiveNamesExist()
    {
        var now = DateTime.UtcNow;
        _db.SmappeeServiceLocations.Add(new SmappeeServiceLocation
        {
            ServiceLocationId = 1,
            UpdatedAtUtc = now,
            AppliancesJson = """[{"id":1,"name":"Paal 4","type":"Charging station","sourceType":"CT"}]"""
        });
        _db.SmappeeStatusSnapshots.Add(new SmappeeStatusSnapshot
        {
            ServiceLocationId = 1,
            FetchedAtUtc = now,
            IsSuccess = true,
            PayloadJson = """
                          {
                            "FetchedAt":"2026-04-27T14:00:00Z",
                            "ServiceLocationId":1,
                            "Points":[
                              {
                                "SerialNumber":"6531001274",
                                "StationName":"Paal 4",
                                "UiState":"available"
                              },
                              {
                                "SerialNumber":"6531001274",
                                "StationName":"Paal 4 - 1",
                                "UiState":"charging"
                              }
                            ],
                            "RangeStartMs":0,
                            "RangeEndMsExclusive":0
                          }
                          """
        });
        await _db.SaveChangesAsync();

        var result = await BuildController().GetSmappeeChargingStationDisplayNames(default);

        var ok = Assert.IsType<OkObjectResult>(result);
        var json = JsonSerializer.Serialize(ok.Value, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        using var doc = JsonDocument.Parse(json);
        var square = Assert.Single(doc.RootElement.GetProperty("squares").EnumerateArray());
        Assert.Equal(ChargingStationHomeSquareDto.LiveStateActive, square.GetProperty("liveState").GetString());
    }

    [Fact]
    public async Task GetSmappeeChargingStationDisplayNames_InUseAndPowerKwAreReturned()
    {
        var now = DateTime.UtcNow;
        var started = now.AddHours(-2);
        _db.SmappeeServiceLocations.Add(new SmappeeServiceLocation
        {
            ServiceLocationId = 1,
            UpdatedAtUtc = now,
            AppliancesJson = """[{"id":1,"name":"Paal 4","type":"Charging station","sourceType":"CT"}]"""
        });
        _db.SmappeeStatusSnapshots.Add(new SmappeeStatusSnapshot
        {
            ServiceLocationId = 1,
            FetchedAtUtc = now,
            IsSuccess = true,
            PayloadJson = $$"""
                          {
                            "FetchedAt":"{{now:O}}",
                            "ServiceLocationId":1,
                            "Points":[
                              {
                                "SerialNumber":"6531001274",
                                "StationName":"Paal 4",
                                "UiState":"charging",
                                "EnergyKwh":14,
                                "StartedAtUtc":"{{started:O}}"
                              }
                            ],
                            "RangeStartMs":0,
                            "RangeEndMsExclusive":0
                          }
                          """
        });
        await _db.SaveChangesAsync();

        var result = await BuildController().GetSmappeeChargingStationDisplayNames(default);

        var ok = Assert.IsType<OkObjectResult>(result);
        var json = JsonSerializer.Serialize(ok.Value, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        using var doc = JsonDocument.Parse(json);
        var square = Assert.Single(doc.RootElement.GetProperty("squares").EnumerateArray());
        Assert.True(square.GetProperty("inUse").GetBoolean());
        Assert.Equal(7m, square.GetProperty("powerKw").GetDecimal());
    }

    [Fact]
    public async Task GetSmappeeChargingStationDisplayNames_DeduplicatesByNameAndKeepsStrongestLiveState()
    {
        var now = DateTime.UtcNow;
        _db.SmappeeServiceLocations.Add(new SmappeeServiceLocation
        {
            ServiceLocationId = 1,
            UpdatedAtUtc = now,
            AppliancesJson = """
                             [
                               {"id":1,"name":"Paal 4","type":"Charging station","sourceType":"CT"},
                               {"id":2,"name":"Paal 4 - 1","type":"Charging station","sourceType":"CT"}
                             ]
                             """
        });
        _db.SmappeeStatusSnapshots.Add(new SmappeeStatusSnapshot
        {
            ServiceLocationId = 1,
            FetchedAtUtc = now,
            IsSuccess = true,
            PayloadJson = """
                          {
                            "FetchedAt":"2026-04-27T14:00:00Z",
                            "ServiceLocationId":1,
                            "Points":[
                              {
                                "SerialNumber":"6531001274",
                                "StationName":"Paal 4",
                                "UiState":"charging",
                                "EnergyKwh":10,
                                "StartedAtUtc":"2026-04-27T12:00:00Z"
                              }
                            ],
                            "RangeStartMs":0,
                            "RangeEndMsExclusive":0
                          }
                          """
        });
        await _db.SaveChangesAsync();

        var result = await BuildController().GetSmappeeChargingStationDisplayNames(default);

        var ok = Assert.IsType<OkObjectResult>(result);
        var json = JsonSerializer.Serialize(ok.Value, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        using var doc = JsonDocument.Parse(json);
        var squares = doc.RootElement.GetProperty("squares");
        Assert.Equal(1, squares.GetArrayLength());
        var square = squares[0];
        Assert.Equal("Paal 4", square.GetProperty("name").GetString());
        Assert.Equal(ChargingStationHomeSquareDto.LiveStateActive, square.GetProperty("liveState").GetString());
        Assert.True(square.GetProperty("inUse").GetBoolean());
    }

    [Fact]
    public async Task GetSmappeeChargingStationDisplayNames_UsesConfiguredNameMapWhenSnapshotNameMissing()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        using var db = new ApplicationDbContext(options);
        var jwt = new Mock<IJwtService>();
        jwt.Setup(j => j.ValidateToken(It.IsAny<string>())).Returns("1");
        var daemon = new Mock<IIntegrationDaemonClient>();
        var controller = new ChargingStationsController(
            db,
            jwt.Object,
            daemon.Object,
            Options.Create(new Backend.Services.Smappee.SmappeeOptions
            {
                StationDisplayNamesBySerial = new Dictionary<string, string>
                {
                    ["5821063919"] = "Paal 4"
                }
            }),
            NullLogger<ChargingStationsController>.Instance);
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers.Authorization = "Bearer t";
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var now = DateTime.UtcNow;
        db.SmappeeServiceLocations.Add(new SmappeeServiceLocation
        {
            ServiceLocationId = 1,
            UpdatedAtUtc = now,
            AppliancesJson = """[{"id":1,"name":"Paal 4 - 1","type":"Charging station","sourceType":"CT"}]"""
        });
        db.SmappeeStatusSnapshots.Add(new SmappeeStatusSnapshot
        {
            ServiceLocationId = 1,
            FetchedAtUtc = now,
            IsSuccess = true,
            PayloadJson = """
                          {
                            "FetchedAt":"2026-04-27T14:00:00Z",
                            "ServiceLocationId":1,
                            "Points":[
                              {
                                "SerialNumber":"5821063919",
                                "StationName":null,
                                "UiState":"charging",
                                "EnergyKwh":11.82,
                                "StartedAtUtc":"2026-04-27T13:22:59Z"
                              }
                            ],
                            "RangeStartMs":0,
                            "RangeEndMsExclusive":0
                          }
                          """
        });
        await db.SaveChangesAsync();

        var result = await controller.GetSmappeeChargingStationDisplayNames(default);
        var ok = Assert.IsType<OkObjectResult>(result);
        var json = JsonSerializer.Serialize(ok.Value, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        using var doc = JsonDocument.Parse(json);
        var square = Assert.Single(doc.RootElement.GetProperty("squares").EnumerateArray());
        Assert.Equal("Paal 4", square.GetProperty("name").GetString());
        Assert.True(square.GetProperty("inUse").GetBoolean());
        Assert.Equal(ChargingStationHomeSquareDto.LiveStateActive, square.GetProperty("liveState").GetString());
    }

    [Fact]
    public async Task PostSmappeeServiceLocationsRefresh_WhenDaemonFails_ReturnsBadGateway()
    {
        _db.Users.Add(new User { Id = 1, Email = "a@test.com", Name = "Admin", GoogleId = "g1", Role = UserRole.Admin });
        await _db.SaveChangesAsync();
        _daemonMock
            .Setup(x => x.TriggerSmappeeServiceLocationsRefreshAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SmappeeUpdateTriggerResult(false, true, null, "daemon failed"));

        var result = await BuildController().PostSmappeeServiceLocationsRefresh();

        var badGateway = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status502BadGateway, badGateway.StatusCode);
    }

    [Fact]
    public async Task PostSmappeeServiceLocationsRefresh_WhenDaemonSucceeds_ReturnsStoredData()
    {
        _db.Users.Add(new User { Id = 1, Email = "a@test.com", Name = "Admin", GoogleId = "g1", Role = UserRole.Admin });
        _db.SmappeeServiceLocationDevices.Add(new SmappeeServiceLocationDevice
        {
            ServiceLocationId = 307785,
            DeviceSerialNumber = "6531001272",
            Name = "Paal 2",
            ServiceLocationUuid = "loc-uuid",
            UpdatedAtUtc = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();
        _daemonMock
            .Setup(x => x.TriggerSmappeeServiceLocationsRefreshAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SmappeeUpdateTriggerResult(true, true, DateTimeOffset.UtcNow, null));
        _daemonMock
            .Setup(x => x.GetSmappeeServiceLocationsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SmappeeServiceLocationsResult(
                Success: true,
                Enabled: true,
                Error: null,
                ServiceLocations: new List<SmappeeServiceLocationListItemResponse>
                {
                    new()
                    {
                        ServiceLocationId = 307785,
                        Name = "Paal 2",
                        ServiceLocationUuid = "loc-uuid",
                        DeviceSerialNumber = "6531001272"
                    }
                },
                FetchedAtUtc: DateTimeOffset.UtcNow));

        var result = await BuildController().PostSmappeeServiceLocationsRefresh();

        var ok = Assert.IsType<OkObjectResult>(result);
        var json = JsonSerializer.Serialize(ok.Value, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        using var doc = JsonDocument.Parse(json);
        var rows = doc.RootElement.GetProperty("serviceLocations");
        Assert.Equal(1, rows.GetArrayLength());
        Assert.Equal(307785, rows[0].GetProperty("serviceLocationId").GetInt32());
    }
}
