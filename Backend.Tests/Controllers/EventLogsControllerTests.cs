using Backend.Controllers;
using Backend.Data;
using Backend.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Backend.Tests.Controllers;

public class EventLogsControllerTests : IDisposable
{
    private readonly ApplicationDbContext _db;

    public EventLogsControllerTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new ApplicationDbContext(options);
    }

    public void Dispose() => _db.Dispose();

    private EventLogsController BuildController()
    {
        var controller = new EventLogsController(_db, NullLogger<EventLogsController>.Instance);
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        return controller;
    }

    [Fact]
    public async Task GetEventLogs_WhenEmpty_ReturnsOkWithEmptyList()
    {
        var result = await BuildController().GetEventLogs();
        var ok = Assert.IsType<OkObjectResult>(result);
        var list = Assert.IsAssignableFrom<IEnumerable<object>>(ok.Value);
        Assert.Empty(list);
    }

    [Fact]
    public async Task GetEventLogs_ReturnsAllLogs()
    {
        _db.EventLogs.AddRange(
            new EventLog { Timestamp = DateTime.UtcNow.AddMinutes(-2), UserName = "Alice", Action = "Login" },
            new EventLog { Timestamp = DateTime.UtcNow.AddMinutes(-1), UserName = "Bob", Action = "Download" }
        );
        await _db.SaveChangesAsync();

        var result = await BuildController().GetEventLogs();
        var ok = Assert.IsType<OkObjectResult>(result);
        var list = Assert.IsAssignableFrom<IEnumerable<object>>(ok.Value).ToList();
        Assert.Equal(2, list.Count);
    }

    [Fact]
    public async Task GetEventLogs_ReturnsNewestFirst()
    {
        var older = new EventLog { Timestamp = DateTime.UtcNow.AddHours(-2), UserName = "Alice", Action = "First" };
        var newer = new EventLog { Timestamp = DateTime.UtcNow.AddHours(-1), UserName = "Bob", Action = "Second" };
        _db.EventLogs.AddRange(older, newer);
        await _db.SaveChangesAsync();

        var result = await BuildController().GetEventLogs();
        var ok = Assert.IsType<OkObjectResult>(result);

        var json = System.Text.Json.JsonSerializer.Serialize(ok.Value);
        var doc = System.Text.Json.JsonDocument.Parse(json);
        var first = doc.RootElement.EnumerateArray().First();
        Assert.Equal("Bob", first.GetProperty("userName").GetString());
    }
}
