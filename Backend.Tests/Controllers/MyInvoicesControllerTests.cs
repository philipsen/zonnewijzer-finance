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

public class MyInvoicesControllerTests : IDisposable
{
    private readonly ApplicationDbContext _db;
    private readonly Mock<IJwtService> _jwtMock;
    private readonly Mock<IMollieService> _mollieMock;

    public MyInvoicesControllerTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new ApplicationDbContext(options);
        _jwtMock = new Mock<IJwtService>();
        _mollieMock = new Mock<IMollieService>();
    }

    public void Dispose() => _db.Dispose();

    private MyInvoicesController BuildController(string? userId = "1")
    {
        _jwtMock.Setup(j => j.ValidateToken(It.IsAny<string>())).Returns(userId);
        var controller = new MyInvoicesController(_db, _jwtMock.Object, _mollieMock.Object, NullLogger<MyInvoicesController>.Instance);
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers.Authorization = "Bearer t";
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        return controller;
    }

    [Fact]
    public async Task GetMyInvoices_InvalidToken_ReturnsUnauthorized()
    {
        var result = await BuildController(userId: null).GetMyInvoices();
        Assert.IsType<UnauthorizedResult>(result);
    }

    [Fact]
    public async Task GetMyInvoices_UserMissing_ReturnsUnauthorized()
    {
        _jwtMock.Setup(j => j.ValidateToken(It.IsAny<string>())).Returns("99");
        var result = await BuildController().GetMyInvoices();
        Assert.IsType<UnauthorizedResult>(result);
    }

    [Fact]
    public async Task GetMyInvoices_NoChargingUser_ReturnsEmptyMonths()
    {
        _db.Users.Add(new User { Id = 1, Email = "a@test.com", Name = "Sam", GoogleId = "g1" });
        await _db.SaveChangesAsync();

        var result = await BuildController().GetMyInvoices();
        var ok = Assert.IsType<OkObjectResult>(result);
        var json = System.Text.Json.JsonSerializer.Serialize(ok.Value);
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        Assert.Equal("Sam", doc.RootElement.GetProperty("userName").GetString());
        Assert.Equal(0, doc.RootElement.GetProperty("months").GetArrayLength());
        _mollieMock.Verify(m => m.GetPaymentStatusAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task GetMyInvoices_WithSessions_BuildsMonthRows()
    {
        _db.Users.Add(new User { Id = 1, Email = "a@test.com", Name = "Sam", GoogleId = "g1", ChargingUserId = 20 });
        var t = new DateTime(2024, 6, 10, 12, 0, 0, DateTimeKind.Utc);
        _db.ChargingSessions.Add(new ChargingSession { UserId = 20, StationId = 1, Start = t, End = t.AddHours(1), ChargedKwh = 10 });
        _db.EnergyPrices.Add(new EnergyPrice { Year = 2024, Month = 6, PricePerKwh = 0.5m });
        await _db.SaveChangesAsync();

        var result = await BuildController().GetMyInvoices();
        var ok = Assert.IsType<OkObjectResult>(result);
        var json = System.Text.Json.JsonSerializer.Serialize(ok.Value);
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        Assert.Equal(20, doc.RootElement.GetProperty("chargingUserId").GetInt32());
        var months = doc.RootElement.GetProperty("months");
        Assert.Equal(1, months.GetArrayLength());
        Assert.Equal(2024, months[0].GetProperty("Year").GetInt32());
        Assert.Equal(6, months[0].GetProperty("Month").GetInt32());
    }

    [Fact]
    public async Task GetMyInvoices_RefreshesOpenPaymentFromMollie()
    {
        _db.Users.Add(new User { Id = 1, Email = "a@test.com", Name = "Sam", GoogleId = "g1", ChargingUserId = 20 });
        var t = new DateTime(2024, 5, 1, 8, 0, 0, DateTimeKind.Utc);
        _db.ChargingSessions.Add(new ChargingSession { UserId = 20, StationId = 1, Start = t, End = t.AddHours(1), ChargedKwh = 2 });
        _db.MolliePayments.Add(new MolliePayment
        {
            ChargingUserId = 20,
            Year = 2024,
            Month = 5,
            MolliePaymentId = "tr_x",
            CheckoutUrl = "https://pay.test",
            Amount = 1,
            Status = "open"
        });
        await _db.SaveChangesAsync();

        _mollieMock.Setup(m => m.GetPaymentStatusAsync("tr_x")).ReturnsAsync("paid");

        var result = await BuildController().GetMyInvoices();
        Assert.IsType<OkObjectResult>(result);

        var payment = await _db.MolliePayments.FirstAsync();
        Assert.Equal("paid", payment.Status);
    }
}
