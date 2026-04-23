using Backend;
using Backend.Contracts;
using Backend.Controllers;
using Backend.Data;
using Backend.Models;
using Backend.Services;
using Backend.Services.GnuCash;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Primitives;
using Moq;
using System.Threading;

namespace Backend.Tests.Controllers;

public class InvoicesControllerTests : IDisposable
{
    private readonly ApplicationDbContext _db;
    private readonly Mock<IJwtService> _jwtMock;
    private readonly Mock<IPdfService> _pdfMock;
    private readonly Mock<IMollieService> _mollieMock;
    private readonly Mock<IConfiguration> _configMock;
    private readonly InvoiceComputationService _invoiceComputation;
    private readonly Mock<IGnuCashSyncClient> _gnuCashSyncMock;

    public InvoicesControllerTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new ApplicationDbContext(options);
        _jwtMock = new Mock<IJwtService>();
        _pdfMock = new Mock<IPdfService>();
        _mollieMock = new Mock<IMollieService>();
        _configMock = new Mock<IConfiguration>();
        _invoiceComputation = new InvoiceComputationService(_db);
        _gnuCashSyncMock = new Mock<IGnuCashSyncClient>();
    }

    public void Dispose() => _db.Dispose();

    private InvoicesController BuildController(string? userId = "1", HttpContext? httpContext = null)
    {
        _jwtMock.Setup(j => j.ValidateToken(It.IsAny<string>())).Returns(userId);
        var ctx = httpContext ?? new DefaultHttpContext();
        ctx.Request.Headers.Authorization = "Bearer t";
        ctx.Request.Scheme = "http";
        ctx.Request.Host = new HostString("localhost", 5293);

        var controller = new InvoicesController(
            _db,
            _jwtMock.Object,
            _pdfMock.Object,
            _mollieMock.Object,
            _invoiceComputation,
            _gnuCashSyncMock.Object,
            _configMock.Object,
            NullLogger<InvoicesController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = ctx }
        };
        return controller;
    }

    private async Task<int> AddChargeCardAsync(string value, string? street = null, string? houseNumber = null)
    {
        var c = new ChargeCard { Value = value, Street = street, HouseNumber = houseNumber };
        _db.ChargeCards.Add(c);
        await _db.SaveChangesAsync();
        return c.Id;
    }

    private async Task SeedAdminAsync()
    {
        _db.Users.Add(new User { Id = 1, Email = "admin@test.com", Name = "Admin", GoogleId = "g1", Role = UserRole.Admin });
        await _db.SaveChangesAsync();
    }

    private async Task SeedOwnerAsync()
    {
        _db.Users.Add(new User { Id = 1, Email = "owner@test.com", Name = "Owner", GoogleId = "g1", Role = UserRole.Owner });
        await _db.SaveChangesAsync();
    }

    [Fact]
    public async Task GetInvoicesGrid_InvalidToken_ReturnsUnauthorized()
    {
        var result = await BuildController(userId: null).GetInvoicesGrid(CancellationToken.None);
        Assert.IsType<UnauthorizedResult>(result);
    }

    [Fact]
    public async Task GetInvoicesGrid_NonAdmin_ReturnsForbid()
    {
        await SeedAdminAsync();
        var regular = new User { Id = 2, Email = "u@test.com", Name = "U", GoogleId = "g2", Role = UserRole.Base };
        _db.Users.Add(regular);
        await _db.SaveChangesAsync();

        var result = await BuildController(userId: "2").GetInvoicesGrid(CancellationToken.None);
        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task GetInvoicesGrid_Admin_ReturnsOkWithGrid()
    {
        await SeedAdminAsync();
        var cid = await AddChargeCardAsync("inv-grid");
        var t = new DateTime(2024, 2, 5, 10, 0, 0, DateTimeKind.Utc);
        _db.ChargingSessions.Add(TestData.Session(cid, 1, t, t.AddHours(1), 12));
        _db.EnergyPrices.Add(new EnergyPrice { Year = 2024, Month = 2, PricePerKwh = 0.3m });
        await _db.SaveChangesAsync();

        var result = await BuildController().GetInvoicesGrid(CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(result);
        var json = System.Text.Json.JsonSerializer.Serialize(ok.Value);
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        Assert.True(doc.RootElement.TryGetProperty("GridData", out _));
        Assert.True(doc.RootElement.TryGetProperty("Months", out _));
    }

    [Fact]
    public async Task GenerateInvoicePdf_NoSessions_ReturnsNotFound()
    {
        await SeedAdminAsync();

        var result = await BuildController().GenerateInvoicePdf(99, 2024, 1);

        var notFound = Assert.IsType<NotFoundObjectResult>(result);
        Assert.Contains("No charging sessions", notFound.Value!.ToString());
    }

    [Fact]
    public async Task GenerateInvoicePdf_WithSessions_ReturnsPdfFile()
    {
        await SeedAdminAsync();
        var cid = await AddChargeCardAsync("inv-pdf");
        var t = new DateTime(2024, 4, 8, 9, 0, 0, DateTimeKind.Utc);
        _db.ChargingSessions.Add(TestData.Session(cid, 1, t, t.AddHours(1), 8));
        _db.EnergyPrices.Add(new EnergyPrice { Year = 2024, Month = 4, PricePerKwh = 0.25m });
        _db.MolliePayments.Add(new MolliePayment
        {
            ChargeCardId = cid,
            Year = 2024,
            Month = 4,
            MolliePaymentId = "tr_existing",
            CheckoutUrl = "https://checkout",
            Amount = 2,
            Status = "paid"
        });
        await _db.SaveChangesAsync();

        _pdfMock.Setup(p => p.GenerateInvoicePdf(It.IsAny<InvoiceData>())).Returns(new byte[] { 0x25, 0x50, 0x44, 0x46 });

        var result = await BuildController().GenerateInvoicePdf(cid, 2024, 4);

        var file = Assert.IsType<FileContentResult>(result);
        Assert.Equal("application/pdf", file.ContentType);
        Assert.NotEmpty(file.FileContents);
        _mollieMock.Verify(m => m.CreatePaymentAsync(It.IsAny<decimal>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task CreatePaymentLink_WhenExists_ReturnsExistingWithoutMollieCreate()
    {
        await SeedAdminAsync();
        var cid = await AddChargeCardAsync("inv-pay-exists");
        _db.MolliePayments.Add(new MolliePayment
        {
            ChargeCardId = cid,
            Year = 2024,
            Month = 3,
            MolliePaymentId = "tr_old",
            CheckoutUrl = "https://old",
            Amount = 10,
            Status = "open"
        });
        await _db.SaveChangesAsync();
        _mollieMock.Setup(m => m.GetPaymentStatusAsync("tr_old")).ReturnsAsync("open");

        var result = await BuildController().CreatePaymentLink(cid, 2024, 3);

        var ok = Assert.IsType<OkObjectResult>(result);
        _mollieMock.Verify(m => m.CreatePaymentAsync(It.IsAny<decimal>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        var json = System.Text.Json.JsonSerializer.Serialize(ok.Value);
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        Assert.Equal("tr_old", doc.RootElement.GetProperty("paymentId").GetString());
    }

    [Fact]
    public async Task CreatePaymentLink_WhenExistingExpired_CreatesFreshPayment()
    {
        await SeedAdminAsync();
        var cid = await AddChargeCardAsync("inv-pay-exp");
        var t = new DateTime(2024, 3, 10, 0, 0, 0, DateTimeKind.Utc);
        _db.ChargingSessions.Add(TestData.Session(cid, 1, t, t.AddHours(1), 4));
        _db.MolliePayments.Add(new MolliePayment
        {
            ChargeCardId = cid,
            Year = 2024,
            Month = 3,
            MolliePaymentId = "tr_old_expired",
            CheckoutUrl = "https://old",
            Amount = 10,
            Status = "expired"
        });
        await _db.SaveChangesAsync();

        _configMock.Setup(c => c["Mollie:RedirectUrl"]).Returns((string?)null);
        _configMock.Setup(c => c["Mollie:BaseUrl"]).Returns((string?)null);
        _mollieMock.Setup(m => m.GetPaymentStatusAsync("tr_old_expired")).ReturnsAsync("expired");
        _mollieMock
            .Setup(m => m.CreatePaymentAsync(It.IsAny<decimal>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(new MolliePaymentResult("tr_new_after_expired", "https://new", "open"));

        var result = await BuildController().CreatePaymentLink(cid, 2024, 3);

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal(1, await _db.MolliePayments.CountAsync());
        var payment = await _db.MolliePayments.FirstAsync();
        Assert.Equal("tr_new_after_expired", payment.MolliePaymentId);
    }

    [Fact]
    public async Task CreatePaymentLink_NoSessions_ReturnsNotFound()
    {
        await SeedAdminAsync();
        var cid = await AddChargeCardAsync("inv-pay-none");

        var result = await BuildController().CreatePaymentLink(cid, 2024, 3);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task CreatePaymentLink_New_CallsMollieAndPersists()
    {
        await SeedAdminAsync();
        var cid = await AddChargeCardAsync("inv-pay-new");
        var t = new DateTime(2024, 3, 10, 0, 0, 0, DateTimeKind.Utc);
        _db.ChargingSessions.Add(TestData.Session(cid, 1, t, t.AddHours(1), 4));
        await _db.SaveChangesAsync();

        _configMock.Setup(c => c["Mollie:RedirectUrl"]).Returns((string?)null);
        _configMock.Setup(c => c["Mollie:BaseUrl"]).Returns((string?)null);

        _mollieMock
            .Setup(m => m.CreatePaymentAsync(It.IsAny<decimal>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(new MolliePaymentResult("tr_new", "https://new", "open"));

        var result = await BuildController().CreatePaymentLink(cid, 2024, 3);

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal(1, await _db.MolliePayments.CountAsync());
        var p = await _db.MolliePayments.FirstAsync();
        Assert.Equal("tr_new", p.MolliePaymentId);
    }

    [Fact]
    public async Task GetPaymentLink_NotFound_WhenMissing()
    {
        await SeedAdminAsync();

        var result = await BuildController().GetPaymentLink(1, 2024, 1);
        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task GetPaymentLink_RefreshesOpenStatus()
    {
        await SeedAdminAsync();
        var cid = await AddChargeCardAsync("inv-getpay");
        _db.MolliePayments.Add(new MolliePayment
        {
            ChargeCardId = cid,
            Year = 2024,
            Month = 1,
            MolliePaymentId = "tr_open",
            CheckoutUrl = "https://c",
            Amount = 5,
            Status = "open"
        });
        await _db.SaveChangesAsync();

        _mollieMock.Setup(m => m.GetPaymentStatusAsync("tr_open")).ReturnsAsync("paid");

        var result = await BuildController().GetPaymentLink(cid, 2024, 1);
        Assert.IsType<OkObjectResult>(result);

        var p = await _db.MolliePayments.FirstAsync();
        Assert.Equal("paid", p.Status);
    }

    [Fact]
    public async Task GetAllPayments_NonAdmin_ReturnsForbid()
    {
        _db.Users.Add(new User { Id = 2, Email = "u@test.com", Name = "U", GoogleId = "g2", Role = UserRole.Base });
        await _db.SaveChangesAsync();

        var result = await BuildController(userId: "2").GetAllPayments();
        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task GetAllPayments_Admin_ReturnsPayments()
    {
        await SeedAdminAsync();
        var cid = await AddChargeCardAsync("inv-all-pay");
        _db.MolliePayments.Add(new MolliePayment
        {
            ChargeCardId = cid,
            Year = 2024,
            Month = 1,
            MolliePaymentId = "tr_p",
            CheckoutUrl = "https://x",
            Amount = 1,
            Status = "paid"
        });
        await _db.SaveChangesAsync();

        var result = await BuildController().GetAllPayments();
        var ok = Assert.IsType<OkObjectResult>(result);
        var list = Assert.IsAssignableFrom<IEnumerable<object>>(ok.Value).ToList();
        Assert.Single(list);
    }

    [Fact]
    public async Task GetAllPayments_RefreshesExpiredStatus()
    {
        await SeedAdminAsync();
        var cid = await AddChargeCardAsync("inv-all-exp");
        _db.MolliePayments.Add(new MolliePayment
        {
            ChargeCardId = cid,
            Year = 2024,
            Month = 1,
            MolliePaymentId = "tr_expired",
            CheckoutUrl = "https://x",
            Amount = 1,
            Status = "expired"
        });
        await _db.SaveChangesAsync();
        _mollieMock.Setup(m => m.GetPaymentStatusAsync("tr_expired")).ReturnsAsync("paid");

        var result = await BuildController().GetAllPayments();

        Assert.IsType<OkObjectResult>(result);
        var payment = await _db.MolliePayments.FirstAsync();
        Assert.Equal("paid", payment.Status);
    }

    [Fact]
    public async Task DeletePayment_NonAdmin_ReturnsForbid()
    {
        _db.Users.Add(new User { Id = 2, Email = "u@test.com", Name = "U", GoogleId = "g2", Role = UserRole.Base });
        _db.ChargeCards.Add(new ChargeCard { Value = "inv-del-nc" });
        await _db.SaveChangesAsync();
        var payCid = _db.ChargeCards.Single().Id;
        _db.MolliePayments.Add(new MolliePayment
        {
            ChargeCardId = payCid,
            Year = 2024,
            Month = 1,
            MolliePaymentId = "tr_del",
            CheckoutUrl = "https://x",
            Amount = 1,
            Status = "paid"
        });
        await _db.SaveChangesAsync();

        var result = await BuildController(userId: "2").DeletePayment((await _db.MolliePayments.FirstAsync()).Id);
        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task DeletePayment_Admin_RemovesPayment()
    {
        await SeedAdminAsync();
        var cid = await AddChargeCardAsync("inv-del-adm");
        _db.MolliePayments.Add(new MolliePayment
        {
            ChargeCardId = cid,
            Year = 2024,
            Month = 2,
            MolliePaymentId = "tr_del2",
            CheckoutUrl = "https://x",
            Amount = 1,
            Status = "paid"
        });
        await _db.SaveChangesAsync();
        var paymentId = (await _db.MolliePayments.FirstAsync()).Id;

        var result = await BuildController().DeletePayment(paymentId);

        Assert.IsType<NoContentResult>(result);
        Assert.Equal(0, await _db.MolliePayments.CountAsync());
    }

    [Fact]
    public async Task MollieWebhook_MissingId_ReturnsBadRequest()
    {
        var form = new FormCollection(new Dictionary<string, StringValues>());
        var ctx = new DefaultHttpContext();
        ctx.Features.Set<IFormFeature>(new FormFeature(form));

        var result = await BuildController(httpContext: ctx).MollieWebhook();
        Assert.IsType<BadRequestResult>(result);
    }

    [Fact]
    public async Task MollieWebhook_UnknownPayment_ReturnsOk()
    {
        var form = new FormCollection(new Dictionary<string, StringValues> { { "id", "tr_unknown" } });
        var ctx = new DefaultHttpContext();
        ctx.Features.Set<IFormFeature>(new FormFeature(form));

        var result = await BuildController(httpContext: ctx).MollieWebhook();
        Assert.IsType<OkResult>(result);
        _mollieMock.Verify(m => m.GetPaymentStatusAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task MollieWebhook_UpdatesPaymentStatus()
    {
        _db.ChargeCards.Add(new ChargeCard { Value = "inv-wh" });
        await _db.SaveChangesAsync();
        var cid = _db.ChargeCards.Single().Id;
        _db.MolliePayments.Add(new MolliePayment
        {
            ChargeCardId = cid,
            Year = 2024,
            Month = 6,
            MolliePaymentId = "tr_wh",
            CheckoutUrl = "https://c",
            Amount = 3,
            Status = "open"
        });
        await _db.SaveChangesAsync();

        _mollieMock.Setup(m => m.GetPaymentStatusAsync("tr_wh")).ReturnsAsync("paid");

        var form = new FormCollection(new Dictionary<string, StringValues> { { "id", "tr_wh" } });
        var ctx = new DefaultHttpContext();
        ctx.Features.Set<IFormFeature>(new FormFeature(form));

        var result = await BuildController(httpContext: ctx).MollieWebhook();
        Assert.IsType<OkResult>(result);

        var p = await _db.MolliePayments.FirstAsync();
        Assert.Equal("paid", p.Status);
    }

    [Fact]
    public async Task EnsureGnucashCustomer_NoChargingLink_ReturnsBadRequest()
    {
        await SeedOwnerAsync();
        _configMock.Setup(c => c["GnuCashSync:BaseUrl"]).Returns("http://127.0.0.1:8787");
        _db.Users.Add(new User { Id = 2, Email = "x@test.com", Name = "X", GoogleId = "g2", Role = UserRole.Base, ChargeCardId = null });
        await _db.SaveChangesAsync();

        var result = await BuildController().EnsureGnucashCustomer(2, false, CancellationToken.None);
        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Contains("charge card", bad.Value!.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EnsureGnucashCustomer_WithLink_CallsPython()
    {
        await SeedOwnerAsync();
        _configMock.Setup(c => c["GnuCashSync:BaseUrl"]).Returns("http://127.0.0.1:8787");
        var cardId = await AddChargeCardAsync("inv-gc-py", street: "Main", houseNumber: "1");
        _db.Users.Add(new User { Id = 2, Email = "x@test.com", Name = "X User", GoogleId = "g2", Role = UserRole.Base, ChargeCardId = cardId });
        await _db.SaveChangesAsync();

        var expectedCu = $"CU-{cardId}";
        _gnuCashSyncMock
            .Setup(g => g.EnsureCustomerAsync(It.IsAny<GnuCashEnsureCustomerRequest>(), It.IsAny<string?>(), false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GnuCashEnsureCustomerResponse { CustomerId = expectedCu, Status = "created" });

        var result = await BuildController().EnsureGnucashCustomer(2, false, CancellationToken.None);
        Assert.IsType<OkObjectResult>(result);
        _gnuCashSyncMock.Verify(
            g => g.EnsureCustomerAsync(
                It.Is<GnuCashEnsureCustomerRequest>(r => r.CustomerId == expectedCu && r.CustomerName == "X User" && r.Street == "Main"),
                It.IsAny<string?>(),
                false,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task EnsureGnucashCustomer_Admin_ReturnsForbid()
    {
        await SeedAdminAsync();
        _configMock.Setup(c => c["GnuCashSync:BaseUrl"]).Returns("http://127.0.0.1:8787");
        var cardId = await AddChargeCardAsync("inv-gc-adm");
        _db.Users.Add(new User { Id = 2, Email = "x@test.com", Name = "X", GoogleId = "g2", Role = UserRole.Base, ChargeCardId = cardId });
        await _db.SaveChangesAsync();

        var result = await BuildController().EnsureGnucashCustomer(2, false, CancellationToken.None);
        Assert.IsType<ForbidResult>(result);
    }
}
