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
    public async Task GetMyInvoices_NoChargeCard_ReturnsEmptyMonths()
    {
        _db.Users.Add(new User { Id = 1, Email = "a@test.com", Name = "Sam", GoogleId = "g1" });
        await _db.SaveChangesAsync();

        var result = await BuildController().GetMyInvoices();
        var ok = Assert.IsType<OkObjectResult>(result);
        var json = System.Text.Json.JsonSerializer.Serialize(ok.Value);
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        Assert.Equal("Sam", doc.RootElement.GetProperty("userName").GetString());
        Assert.Equal(0, doc.RootElement.GetProperty("months").GetArrayLength());
        Assert.Equal(0, doc.RootElement.GetProperty("chargeCardIds").GetArrayLength());
        _mollieMock.Verify(m => m.GetPaymentStatusAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task GetMyInvoices_WithSessions_BuildsMonthRows()
    {
        _db.ChargeCards.Add(new ChargeCard { Value = "RF-INV-1" });
        await _db.SaveChangesAsync();
        var cid = _db.ChargeCards.Single().Id;
        _db.Users.Add(new User { Id = 1, Email = "a@test.com", Name = "Sam", GoogleId = "g1" });
        await _db.SaveChangesAsync();
        _db.UserChargeCards.Add(new UserChargeCard { UserId = 1, ChargeCardId = cid });
        var t = new DateTime(2024, 6, 10, 12, 0, 0, DateTimeKind.Utc);
        _db.ChargingSessions.Add(TestData.Session(cid, 1, t, t.AddHours(1), 10));
        _db.EnergyPrices.Add(new EnergyPrice { Year = 2024, Month = 6, PricePerKwh = 0.5m });
        await _db.SaveChangesAsync();

        var result = await BuildController().GetMyInvoices();
        var ok = Assert.IsType<OkObjectResult>(result);
        var json = System.Text.Json.JsonSerializer.Serialize(ok.Value);
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var cardIds = doc.RootElement.GetProperty("chargeCardIds");
        Assert.Equal(1, cardIds.GetArrayLength());
        Assert.Equal(cid, cardIds[0].GetInt32());
        var months = doc.RootElement.GetProperty("months");
        Assert.Equal(1, months.GetArrayLength());
        Assert.Equal(1, months[0].GetProperty("appUserId").GetInt32());
        Assert.Equal(2024, months[0].GetProperty("Year").GetInt32());
        Assert.Equal(6, months[0].GetProperty("Month").GetInt32());
        Assert.Equal("INV-2024-06-U1", months[0].GetProperty("InvoiceNumber").GetString());
    }

    [Fact]
    public async Task GetMyInvoices_RefreshesOpenPaymentFromMollie()
    {
        _db.ChargeCards.Add(new ChargeCard { Value = "RF-INV-2" });
        await _db.SaveChangesAsync();
        var cid = _db.ChargeCards.Single().Id;
        _db.Users.Add(new User { Id = 1, Email = "a@test.com", Name = "Sam", GoogleId = "g1" });
        await _db.SaveChangesAsync();
        _db.UserChargeCards.Add(new UserChargeCard { UserId = 1, ChargeCardId = cid });
        var t = new DateTime(2024, 5, 1, 8, 0, 0, DateTimeKind.Utc);
        _db.ChargingSessions.Add(TestData.Session(cid, 1, t, t.AddHours(1), 2));
        _db.MolliePayments.Add(new MolliePayment
        {
            AppUserId = 1,
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
