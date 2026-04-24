using Backend;
using Backend.Controllers;
using Backend.Data;
using Backend.Models;
using Backend.Services;
using Backend.Services.Smappee;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace Backend.Tests.Controllers;

public class ChargingSessionsControllerTests : IDisposable
{
    private readonly ApplicationDbContext _db;
    private readonly Mock<IJwtService> _jwtMock;
    private readonly Mock<ISmappeeCloudClient> _smappeeMock;

    public ChargingSessionsControllerTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new ApplicationDbContext(options);
        _jwtMock = new Mock<IJwtService>();
        _smappeeMock = new Mock<ISmappeeCloudClient>();
    }

    public void Dispose() => _db.Dispose();

    private (ChargingSessionsController Controller, ISmappeeChargingSyncService Sync) BuildController(
        string? userId = "1",
        SmappeeOptions? smappeeOptions = null,
        Action<Mock<ISmappeeCloudClient>>? configureSmappee = null)
    {
        _jwtMock.Setup(j => j.ValidateToken(It.IsAny<string>())).Returns(userId);
        _smappeeMock.Reset();
        if (configureSmappee != null)
            configureSmappee(_smappeeMock);
        else
        {
            _smappeeMock
                .Setup(s => s.GetWhitelistedRfidsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Array.Empty<SmappeeRfidDto>());
            _smappeeMock
                .Setup(s => s.GetChargingSessionsAsync(
                    It.IsAny<int>(),
                    It.IsAny<long>(),
                    It.IsAny<long?>(),
                    It.IsAny<bool?>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(Array.Empty<SmappeeSessionDto>());
        }

        var opts = smappeeOptions ?? new SmappeeOptions
        {
            ServiceLocationId = 1,
            DefaultConnector = "1"
        };

        var sync = new SmappeeChargingSyncService(
            _db,
            _smappeeMock.Object,
            Options.Create(opts),
            NullLogger<SmappeeChargingSyncService>.Instance);
        if (!_db.Users.Any(u => u.Id == 1))
        {
            _db.Users.Add(new User
            {
                Id = 1,
                Email = "admin-test@local",
                Name = "Admin",
                Role = UserRole.Admin
            });
            _db.SaveChanges();
        }

        var controller = new ChargingSessionsController(
            _db,
            _jwtMock.Object,
            sync,
            NullLogger<ChargingSessionsController>.Instance);
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers.Authorization = "Bearer valid-token";
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        return (controller, sync);
    }

    private async Task<int> SeedChargeCardAsync(string value = "RFID1", string? comment = "User1")
    {
        var c = new ChargeCard { Value = value, Comment = comment };
        _db.ChargeCards.Add(c);
        await _db.SaveChangesAsync();
        return c.Id;
    }

    private ChargingSession MakeSession(int chargeCardId = 1, int stationId = 10, decimal kwh = 5.0m, DateTime? start = null)
    {
        var s = start ?? DateTime.UtcNow.AddHours(-1);
        return TestData.Session(chargeCardId, stationId, s, s.AddHours(1), kwh);
    }

    // ── AddSession ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task AddSession_WithValidData_ReturnsOkAndPersists()
    {
        var cardId = await SeedChargeCardAsync();
        var (controller, _) = BuildController();
        var request = new AddSessionRequest
        {
            Start = DateTime.UtcNow.AddHours(-2),
            End = DateTime.UtcNow.AddHours(-1),
            ChargeCardId = cardId,
            StationId = 5,
            ChargedKwh = 10.5m
        };

        var result = await controller.AddSession(request);

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal(1, await _db.ChargingSessions.CountAsync());
        var row = await _db.ChargingSessions.SingleAsync();
        Assert.Equal(cardId, row.ChargeCardId);
        Assert.Equal(10.5m, ChargingSessionEnergy.ChargedKwh(row));
    }

    [Fact]
    public async Task AddSession_WhenEndBeforeStart_ReturnsBadRequest()
    {
        var cardId = await SeedChargeCardAsync();
        var (controller, _) = BuildController();
        var request = new AddSessionRequest
        {
            Start = DateTime.UtcNow,
            End = DateTime.UtcNow.AddHours(-1),
            ChargeCardId = cardId,
            StationId = 5,
            ChargedKwh = 5m
        };

        var result = await controller.AddSession(request);

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Contains("End time", bad.Value!.ToString());
    }

    [Fact]
    public async Task AddSession_WhenEndNull_Ongoing_ReturnsOk()
    {
        var cardId = await SeedChargeCardAsync();
        var (controller, _) = BuildController();
        var start = DateTime.UtcNow.AddHours(-1);
        var request = new AddSessionRequest
        {
            Start = start,
            End = null,
            ChargeCardId = cardId,
            StationId = 5,
            ChargedKwh = 2.5m
        };

        var result = await controller.AddSession(request);

        Assert.IsType<OkObjectResult>(result);
        var row = await _db.ChargingSessions.SingleAsync();
        Assert.Null(row.ToTimestamp);
        Assert.Equal(new DateTimeOffset(start.ToUniversalTime()).ToUnixTimeMilliseconds(), row.FromTimestamp);
    }

    [Fact]
    public async Task AddSession_WhenSameStartUserStation_ReplacesExistingRow()
    {
        var cardId = await SeedChargeCardAsync("RFID7", "U7");
        var (controller, _) = BuildController();
        var start = new DateTime(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc);
        var first = new AddSessionRequest
        {
            Start = start,
            End = start.AddHours(1),
            ChargeCardId = cardId,
            StationId = 11,
            ChargedKwh = 1m
        };
        Assert.IsType<OkObjectResult>(await controller.AddSession(first));
        var id = (await _db.ChargingSessions.SingleAsync()).Id;

        var second = new AddSessionRequest
        {
            Start = start,
            End = start.AddHours(2),
            ChargeCardId = cardId,
            StationId = 11,
            ChargedKwh = 99m
        };
        Assert.IsType<OkObjectResult>(await controller.AddSession(second));

        Assert.Equal(1, await _db.ChargingSessions.CountAsync());
        var row = await _db.ChargingSessions.SingleAsync();
        Assert.Equal(id, row.Id);
        Assert.Equal(99m, ChargingSessionEnergy.ChargedKwh(row));
        Assert.Equal(new DateTimeOffset(start.AddHours(2).ToUniversalTime()).ToUnixTimeMilliseconds(), row.ToTimestamp);
    }

    [Fact]
    public async Task AddSession_WithNegativeKwh_ReturnsBadRequest()
    {
        var cardId = await SeedChargeCardAsync();
        var (controller, _) = BuildController();
        var request = new AddSessionRequest
        {
            Start = DateTime.UtcNow.AddHours(-1),
            End = DateTime.UtcNow,
            ChargeCardId = cardId,
            StationId = 5,
            ChargedKwh = -1m
        };

        var result = await controller.AddSession(request);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task AddSession_WithSerialAndConnector_DerivesStationId()
    {
        var cardId = await SeedChargeCardAsync();
        var (controller, _) = BuildController();
        var request = new AddSessionRequest
        {
            Start = DateTime.UtcNow.AddHours(-2),
            End = DateTime.UtcNow.AddHours(-1),
            ChargeCardId = cardId,
            StationId = 999,
            StationSerialNumber = "6531001272",
            Connector = "2",
            ChargedKwh = 3m
        };

        var result = await controller.AddSession(request);

        Assert.IsType<OkObjectResult>(result);
        var session = await _db.ChargingSessions.FirstAsync();
        Assert.Equal(ChargingPointIds.ChargingPointIdFromSerialAndConnector("6531001272", "2"), session.StationId);
        Assert.Equal("6531001272", session.SerialNumber);
    }

    // ── SyncSmappee ────────────────────────────────────────────────────────────

    [Fact]
    public async Task SyncSmappee_WhenServiceLocationNotConfigured_ReturnsBadRequest()
    {
        var (controller, _) = BuildController(smappeeOptions: new SmappeeOptions { ServiceLocationId = 0 });
        var result = await controller.SyncSmappee(
            new SyncSmappeeRequest { From = new DateOnly(2026, 1, 1), To = new DateOnly(2026, 1, 2) },
            CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task SyncSmappee_WhenToBeforeFrom_ReturnsBadRequest()
    {
        var (controller, _) = BuildController();
        var result = await controller.SyncSmappee(
            new SyncSmappeeRequest { From = new DateOnly(2026, 2, 5), To = new DateOnly(2026, 2, 4) },
            CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task SyncSmappee_UpsertsWhitelistAndSessions()
    {
        var day = new DateOnly(2026, 5, 10);
        var rangeStart = BrusselsTime.StartOfCalendarDayUnixMilliseconds(day);
        var fromMs = rangeStart + 3_600_000;
        var toMs = rangeStart + 7_200_000;

        var (controller, _) = BuildController(configureSmappee: m =>
        {
            m.Setup(s => s.GetWhitelistedRfidsAsync(1, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<SmappeeRfidDto>
                {
                    new()
                    {
                        Id = 42,
                        Value = "047D9322B62290 (Test User)",
                        Comment = "Test User"
                    }
                });
            m.Setup(s => s.GetChargingSessionsAsync(
                    1,
                    rangeStart,
                    BrusselsTime.StartOfCalendarDayUnixMilliseconds(day.AddDays(1)),
                    It.IsAny<bool?>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<SmappeeSessionDto>
                {
                    new()
                    {
                        Id = 9001,
                        SerialNumber = "6531001272",
                        AuthenticationType = "RFID",
                        Rfid = "047D9322B62290",
                        From = fromMs,
                        To = toMs,
                        Status = "STOPPED",
                        StartReading = 100m,
                        StopReading = 104.25m
                    }
                });
        });
        var result = await controller.SyncSmappee(new SyncSmappeeRequest { From = day, To = day }, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(ok.Value);

        var card = await _db.ChargeCards.SingleAsync(c => c.SmappeeWhitelistId == 42);
        Assert.Equal("047D9322B62290", card.Value);

        var sess = await _db.ChargingSessions.SingleAsync(s => s.SmappeeSessionId == 9001);
        Assert.Equal(card.Id, sess.ChargeCardId);
        Assert.Equal(4.25m, ChargingSessionEnergy.ChargedKwh(sess));
        Assert.Equal("6531001272", sess.SerialNumber);
    }

    [Fact]
    public async Task SyncSmappee_OngoingSession_SecondsTimestamp_NoStopReading()
    {
        var day = new DateOnly(2026, 4, 22);
        var rangeStart = BrusselsTime.StartOfCalendarDayUnixMilliseconds(day);
        const long fromUnixSeconds = 1_776_936_253L;

        var (controller, _) = BuildController(configureSmappee: m =>
        {
            m.Setup(s => s.GetWhitelistedRfidsAsync(1, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<SmappeeRfidDto>
                {
                    new() { Id = 1, Value = "047D9322B62290", Comment = "Card" }
                });
            m.Setup(s => s.GetChargingSessionsAsync(
                    1,
                    rangeStart,
                    BrusselsTime.StartOfCalendarDayUnixMilliseconds(day.AddDays(1)),
                    It.IsAny<bool?>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<SmappeeSessionDto>
                {
                    new()
                    {
                        Id = 28_480_919,
                        SerialNumber = "6531001272",
                        Rfid = "047D9322B62290",
                        From = fromUnixSeconds,
                        To = null,
                        Status = "CHARGING",
                        StartReading = 178.972m,
                        StopReading = null
                    }
                });
        });

        var result = await controller.SyncSmappee(new SyncSmappeeRequest { From = day, To = day }, CancellationToken.None);
        Assert.IsType<OkObjectResult>(result);

        var sess = await _db.ChargingSessions.SingleAsync(s => s.SmappeeSessionId == 28_480_919);
        Assert.Equal(fromUnixSeconds * 1000L, sess.FromTimestamp);
        Assert.Null(sess.ToTimestamp);
        Assert.Equal(178.972m, sess.StartReading);
        Assert.Null(sess.StopReading);
        Assert.Equal(0m, ChargingSessionEnergy.ChargedKwh(sess));
    }

    [Fact]
    public async Task SyncSmappee_IdempotentBySmappeeSessionId()
    {
        var day = new DateOnly(2026, 6, 1);
        var rangeStart = BrusselsTime.StartOfCalendarDayUnixMilliseconds(day);
        var dto = new SmappeeSessionDto
        {
            Id = 777,
            SerialNumber = "6531001272",
            Rfid = "AAAABBBB",
            From = rangeStart + 1_000,
            To = rangeStart + 2_000,
            Status = "STOPPED",
            StartReading = 0m,
            StopReading = 1.5m
        };

        var (controller, _) = BuildController(configureSmappee: m =>
        {
            m.Setup(s => s.GetWhitelistedRfidsAsync(1, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Array.Empty<SmappeeRfidDto>());
            m.Setup(s => s.GetChargingSessionsAsync(
                    1,
                    rangeStart,
                    BrusselsTime.StartOfCalendarDayUnixMilliseconds(day.AddDays(1)),
                    It.IsAny<bool?>(),
                    It.IsAny<CancellationToken>()))
                .Returns(() => Task.FromResult<IReadOnlyList<SmappeeSessionDto>>(new List<SmappeeSessionDto> { dto }));
        });

        Assert.IsType<OkObjectResult>(await controller.SyncSmappee(new SyncSmappeeRequest { From = day, To = day }, CancellationToken.None));
        Assert.Equal(1, await _db.ChargingSessions.CountAsync());
        Assert.Equal(1.5m, ChargingSessionEnergy.ChargedKwh(await _db.ChargingSessions.SingleAsync()));

        dto.StopReading = 9m;
        Assert.IsType<OkObjectResult>(await controller.SyncSmappee(new SyncSmappeeRequest { From = day, To = day }, CancellationToken.None));
        Assert.Equal(1, await _db.ChargingSessions.CountAsync());
        Assert.Equal(9m, ChargingSessionEnergy.ChargedKwh(await _db.ChargingSessions.SingleAsync()));
    }

    // ── GetAllSessions ─────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAllSessions_WithInvalidToken_ReturnsUnauthorized()
    {
        var (c, _) = BuildController(userId: null);
        var result = await c.GetAllSessions();
        Assert.IsType<UnauthorizedResult>(result);
    }

    [Fact]
    public async Task GetAllSessions_ReturnsPaginatedResults()
    {
        var cardId = await SeedChargeCardAsync();
        _db.ChargingSessions.AddRange(Enumerable.Range(1, 5).Select(i =>
            MakeSession(cardId, start: DateTime.UtcNow.AddHours(-i))));
        await _db.SaveChangesAsync();

        var (c, _) = BuildController();
        var result = await c.GetAllSessions(page: 1, pageSize: 3);

        var ok = Assert.IsType<OkObjectResult>(result);
        var json = System.Text.Json.JsonSerializer.Serialize(ok.Value);
        var doc = System.Text.Json.JsonDocument.Parse(json);
        var sessions = doc.RootElement.GetProperty("sessions").EnumerateArray();
        Assert.Equal(3, sessions.Count());
    }

    [Fact]
    public async Task GetAllSessions_WithMonthFilter_ReturnsOnlyMatchingSessions()
    {
        var cardId = await SeedChargeCardAsync();
        var jan = new DateTime(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc);
        var feb = new DateTime(2026, 2, 10, 12, 0, 0, DateTimeKind.Utc);
        _db.ChargingSessions.AddRange(
            MakeSession(cardId, start: jan),
            MakeSession(cardId, start: feb)
        );
        await _db.SaveChangesAsync();

        var (c, _) = BuildController();
        var result = await c.GetAllSessions(year: 2026, month: 1);

        var ok = Assert.IsType<OkObjectResult>(result);
        var json = System.Text.Json.JsonSerializer.Serialize(ok.Value);
        var doc = System.Text.Json.JsonDocument.Parse(json);
        var sessions = doc.RootElement.GetProperty("sessions").EnumerateArray();
        Assert.Single(sessions);
    }

    [Fact]
    public async Task GetAllSessions_WithInvalidMonth_ReturnsBadRequest()
    {
        var (c, _) = BuildController();
        var result = await c.GetAllSessions(year: 2026, month: 13);
        Assert.IsType<BadRequestObjectResult>(result);
    }

    // ── GetAvailableMonths ─────────────────────────────────────────────────────

    [Fact]
    public async Task GetAvailableMonths_WithInvalidToken_ReturnsUnauthorized()
    {
        var (c, _) = BuildController(userId: null);
        var result = await c.GetAvailableMonths();
        Assert.IsType<UnauthorizedResult>(result);
    }

    [Fact]
    public async Task GetAvailableMonths_ReturnsDistinctMonths()
    {
        var cardId = await SeedChargeCardAsync();
        var jan = new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc);
        var feb = new DateTime(2026, 2, 5, 0, 0, 0, DateTimeKind.Utc);
        _db.ChargingSessions.AddRange(
            MakeSession(cardId, start: jan),
            MakeSession(cardId, start: jan.AddDays(5)),
            MakeSession(cardId, start: feb)
        );
        await _db.SaveChangesAsync();

        var (c, _) = BuildController();
        var result = await c.GetAvailableMonths();

        var ok = Assert.IsType<OkObjectResult>(result);
        var list = Assert.IsAssignableFrom<IEnumerable<object>>(ok.Value).ToList();
        Assert.Equal(2, list.Count);
    }

    // ── UpdateSession ──────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateSession_WhenNotFound_ReturnsNotFound()
    {
        var cardId = await SeedChargeCardAsync();
        var (controller, _) = BuildController();
        var request = new UpdateSessionRequest
        {
            Start = DateTime.UtcNow.AddHours(-2),
            End = DateTime.UtcNow.AddHours(-1),
            ChargeCardId = cardId,
            StationId = 5,
            ChargedKwh = 5m
        };

        var result = await controller.UpdateSession(999, request);
        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task UpdateSession_WithValidData_UpdatesAndReturnsOk()
    {
        var cardId = await SeedChargeCardAsync();
        var cardId2 = await SeedChargeCardAsync("RFID2", "U2");
        var session = MakeSession(cardId);
        _db.ChargingSessions.Add(session);
        await _db.SaveChangesAsync();

        var (controller, _) = BuildController();
        var request = new UpdateSessionRequest
        {
            Start = ChargingSessionEnergy.StartUtc(session.FromTimestamp),
            End = ChargingSessionEnergy.EndUtc(session.ToTimestamp),
            ChargeCardId = cardId2,
            StationId = 99,
            ChargedKwh = 20m
        };

        var result = await controller.UpdateSession(session.Id, request);

        Assert.IsType<OkObjectResult>(result);
        var updated = await _db.ChargingSessions.FindAsync(session.Id);
        Assert.Equal(20m, ChargingSessionEnergy.ChargedKwh(updated!));
        Assert.Equal(99, updated!.StationId);
        Assert.Equal(cardId2, updated.ChargeCardId);
    }

    [Fact]
    public async Task UpdateSession_WhenEndBeforeStart_ReturnsBadRequest()
    {
        var cardId = await SeedChargeCardAsync();
        var (controller, _) = BuildController();
        var request = new UpdateSessionRequest
        {
            Start = DateTime.UtcNow,
            End = DateTime.UtcNow.AddHours(-1),
            ChargeCardId = cardId,
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
        var (c, _) = BuildController();
        var result = await c.DeleteSession(999);
        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task DeleteSession_WhenExists_RemovesAndReturnsOk()
    {
        var cardId = await SeedChargeCardAsync();
        var session = MakeSession(cardId);
        _db.ChargingSessions.Add(session);
        await _db.SaveChangesAsync();

        var (c, _) = BuildController();
        var result = await c.DeleteSession(session.Id);

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal(0, await _db.ChargingSessions.CountAsync());
    }
}
