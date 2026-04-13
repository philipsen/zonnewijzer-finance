using System.Text;
using Backend;
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
    public async Task AddSession_WhenEndNull_Ongoing_ReturnsOk()
    {
        var controller = BuildController();
        var start = DateTime.UtcNow.AddHours(-1);
        var request = new AddSessionRequest
        {
            Start = start,
            End = null,
            UserId = 1,
            StationId = 5,
            ChargedKwh = 2.5m
        };

        var result = await controller.AddSession(request);

        Assert.IsType<OkObjectResult>(result);
        var row = await _db.ChargingSessions.SingleAsync();
        Assert.Null(row.End);
        Assert.Equal(start, row.Start);
    }

    [Fact]
    public async Task AddSession_WhenSameStartUserStation_ReplacesExistingRow()
    {
        var controller = BuildController();
        var start = new DateTime(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc);
        var first = new AddSessionRequest
        {
            Start = start,
            End = start.AddHours(1),
            UserId = 7,
            StationId = 11,
            ChargedKwh = 1m
        };
        Assert.IsType<OkObjectResult>(await controller.AddSession(first));
        var id = (await _db.ChargingSessions.SingleAsync()).Id;

        var second = new AddSessionRequest
        {
            Start = start,
            End = start.AddHours(2),
            UserId = 7,
            StationId = 11,
            ChargedKwh = 99m
        };
        Assert.IsType<OkObjectResult>(await controller.AddSession(second));

        Assert.Equal(1, await _db.ChargingSessions.CountAsync());
        var row = await _db.ChargingSessions.SingleAsync();
        Assert.Equal(id, row.Id);
        Assert.Equal(99m, row.ChargedKwh);
        Assert.Equal(start.AddHours(2), row.End);
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

    [Fact]
    public async Task AddSession_WithSerialAndConnector_DerivesStationId()
    {
        var controller = BuildController();
        var request = new AddSessionRequest
        {
            Start = DateTime.UtcNow.AddHours(-2),
            End = DateTime.UtcNow.AddHours(-1),
            UserId = 1,
            StationId = 999,
            StationSerialNumber = "6531001272",
            Connector = "2",
            ChargingStationName = "Paal test",
            ChargedKwh = 3m
        };

        var result = await controller.AddSession(request);

        Assert.IsType<OkObjectResult>(result);
        var session = await _db.ChargingSessions.FirstAsync();
        Assert.Equal(ChargingPointIds.ChargingPointIdFromSerialAndConnector("6531001272", "2"), session.StationId);
        Assert.Equal("6531001272", session.StationSerialNumber);
        Assert.Equal("2", session.Connector);
        Assert.Equal("Paal test", session.ChargingStationName);
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

    // ── UploadCsv ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task UploadCsv_NullFile_ReturnsBadRequest()
    {
        var result = await BuildController().UploadCsv(null!);
        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("No file uploaded", bad.Value);
    }

    [Fact]
    public async Task UploadCsv_NonCsvExtension_ReturnsBadRequest()
    {
        var stream = new MemoryStream(Encoding.UTF8.GetBytes("x"));
        var file = new FormFile(stream, 0, stream.Length, "file", "readme.txt");

        var result = await BuildController().UploadCsv(file);

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("File must be a CSV", bad.Value);
    }

    [Fact]
    public async Task UploadCsv_ValidExport_InsertsRow()
    {
        var csv = """
Station serial number,Charging station name,From,To,Duration [h:mm],kWh,Connector,Token
6531001272,Paal 2,2026-05-10 12:00:00,2026-05-10 13:00:00,01:00,"4,25",1,047D9322B62290 (Test User)
""";
        var bytes = Encoding.UTF8.GetBytes(csv.Trim());
        using var stream = new MemoryStream(bytes);
        var file = new FormFile(stream, 0, bytes.Length, "file", "sessions.csv");

        var result = await BuildController().UploadCsv(file);

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal(1, await _db.ChargingSessions.CountAsync());
        var session = await _db.ChargingSessions.FirstAsync();
        var expectedUserId = ChargingImportIds.ChargingUserIdFromTokenKey(
            ChargingImportIds.NormalizeTokenKey("047D9322B62290 (Test User)"));
        Assert.Equal(expectedUserId, session.UserId);
        Assert.Equal(4.25m, session.ChargedKwh);
        Assert.Equal("6531001272", session.StationSerialNumber);
        Assert.Equal("1", session.Connector);
        Assert.Equal("Paal 2", session.ChargingStationName);
        var expectedPointId = ChargingPointIds.ChargingPointIdFromSerialAndConnector("6531001272", "1");
        Assert.Equal(expectedPointId, session.StationId);

        var profile = await _db.ChargingUserProfiles.FirstOrDefaultAsync(p => p.Id == session.UserId);
        Assert.NotNull(profile);
        Assert.Equal("047D9322B62290", profile!.RfidTokenKey);
        Assert.Equal("Test User", profile.DisplayName);
    }

    [Fact]
    public async Task UploadCsv_WrongHeader_ReturnsBadRequest()
    {
        var csv = "a,b,c\n1,2,3";
        var bytes = Encoding.UTF8.GetBytes(csv);
        using var stream = new MemoryStream(bytes);
        var file = new FormFile(stream, 0, bytes.Length, "file", "bad.csv");

        var result = await BuildController().UploadCsv(file);

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Contains("Station serial number", bad.Value!.ToString());
    }

    [Fact]
    public async Task UploadCsv_ExportFormat_MapsTokensToUserIdsAndInserts()
    {
        var csv = """
Station serial number,Charging station name,From,To,Duration [h:mm],kWh,Connector,Token
6531001272,Paal 2,2026-04-13 16:14:07,2026-04-13 16:17:38,00:04,"0,388",1,047D9322B62290 (Cock van Bruggen)
6531001273,Paal 1,2026-04-14 10:00:00,2026-04-14 10:30:00,00:30,"5,500",2,048E8433C73391 (Jan de Vries)
6531001272,Paal 2,2026-04-15 08:00:00,2026-04-15 08:15:00,00:15,"1,200",1,047D9322B62290 (Cock van Bruggen)
""";
        var bytes = Encoding.UTF8.GetBytes(csv.Trim());
        using var stream = new MemoryStream(bytes);
        var file = new FormFile(stream, 0, bytes.Length, "file", "export.csv");

        var result = await BuildController().UploadCsv(file);

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal(3, await _db.ChargingSessions.CountAsync());
        var id1 = ChargingImportIds.ChargingUserIdFromTokenKey(ChargingImportIds.NormalizeTokenKey("047D9322B62290 (Cock van Bruggen)"));
        var id2 = ChargingImportIds.ChargingUserIdFromTokenKey(ChargingImportIds.NormalizeTokenKey("048E8433C73391 (Jan de Vries)"));
        var userIds = await _db.ChargingSessions.Select(s => s.UserId).Distinct().OrderBy(x => x).ToListAsync();
        Assert.Equal(2, userIds.Count);
        Assert.Contains(id1, userIds);
        Assert.Contains(id2, userIds);
        Assert.Equal(0.388m, await _db.ChargingSessions.Where(s => s.ChargedKwh < 1).Select(s => s.ChargedKwh).FirstAsync());
    }

    [Fact]
    public async Task UploadCsv_SameSerialDifferentConnectors_DifferentChargingPointIds()
    {
        var csv = """
Station serial number,Charging station name,From,To,Duration [h:mm],kWh,Connector,Token
6531001272,Paal 2,2026-07-01 10:00:00,2026-07-01 11:00:00,01:00,"1,0",1,047D9322B62290 (Test User)
6531001272,Paal 2,2026-07-02 10:00:00,2026-07-02 11:00:00,01:00,"1,0",2,047D9322B62290 (Test User)
""";
        var bytes = Encoding.UTF8.GetBytes(csv.Trim());
        using var stream = new MemoryStream(bytes);
        var file = new FormFile(stream, 0, bytes.Length, "file", "two-connectors.csv");

        var result = await BuildController().UploadCsv(file);

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal(2, await _db.ChargingSessions.CountAsync());
        var ids = await _db.ChargingSessions.Select(s => s.StationId).Distinct().OrderBy(x => x).ToListAsync();
        Assert.Equal(2, ids.Count);
    }

    [Fact]
    public async Task UploadCsv_DuplicateLinesInSameFile_SkipsSecond()
    {
        var line =
            "6531001272,Paal 2,2026-06-01 08:00:00,2026-06-01 08:45:00,00:45,\"3,50\",1,047D9322B62290 (Test User)";
        var csv =
            "Station serial number,Charging station name,From,To,Duration [h:mm],kWh,Connector,Token\n" +
            line + "\n" +
            line;
        var bytes = Encoding.UTF8.GetBytes(csv);
        using var stream = new MemoryStream(bytes);
        var file = new FormFile(stream, 0, bytes.Length, "file", "dup.csv");

        var result = await BuildController().UploadCsv(file);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(1, await _db.ChargingSessions.CountAsync());
        var json = System.Text.Json.JsonSerializer.Serialize(ok.Value);
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        Assert.Equal(1, doc.RootElement.GetProperty("added").GetInt32());
        Assert.Equal(0, doc.RootElement.GetProperty("updated").GetInt32());
        Assert.Equal(1, doc.RootElement.GetProperty("skipped").GetInt32());
    }

    [Fact]
    public async Task UploadCsv_SameKeyAsExistingRow_UpdatesInPlace()
    {
        var line =
            "6531001272,Paal 2,2026-08-01 09:00:00,2026-08-01 10:00:00,01:00,\"2,00\",1,047D9322B62290 (Test User)";
        var csv =
            "Station serial number,Charging station name,From,To,Duration [h:mm],kWh,Connector,Token\n" +
            line;
        var bytes = Encoding.UTF8.GetBytes(csv);
        using var stream = new MemoryStream(bytes);
        var file = new FormFile(stream, 0, bytes.Length, "file", "first.csv");

        var controller = BuildController();
        Assert.IsType<OkObjectResult>(await controller.UploadCsv(file));
        Assert.Equal(1, await _db.ChargingSessions.CountAsync());
        Assert.Equal(2.00m, (await _db.ChargingSessions.FirstAsync()).ChargedKwh);

        var lineUpdated =
            "6531001272,Paal 2 Renamed,2026-08-01 09:00:00,2026-08-01 11:00:00,02:00,\"9,75\",1,047D9322B62290 (Test User)";
        var csv2 =
            "Station serial number,Charging station name,From,To,Duration [h:mm],kWh,Connector,Token\n" +
            lineUpdated;
        var bytes2 = Encoding.UTF8.GetBytes(csv2);
        using var stream2 = new MemoryStream(bytes2);
        var file2 = new FormFile(stream2, 0, bytes2.Length, "file", "second.csv");

        var result2 = await controller.UploadCsv(file2);
        var ok2 = Assert.IsType<OkObjectResult>(result2);
        Assert.Equal(1, await _db.ChargingSessions.CountAsync());
        var row = await _db.ChargingSessions.FirstAsync();
        Assert.Equal(9.75m, row.ChargedKwh);
        Assert.Equal("Paal 2 Renamed", row.ChargingStationName);

        var json = System.Text.Json.JsonSerializer.Serialize(ok2.Value);
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        Assert.Equal(0, doc.RootElement.GetProperty("added").GetInt32());
        Assert.Equal(1, doc.RootElement.GetProperty("updated").GetInt32());
    }
}
