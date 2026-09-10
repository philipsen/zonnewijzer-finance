using Backend;
using Backend.Contracts;
using Backend.Controllers;
using Backend.Data;
using Backend.Models;
using Backend.Services;
using Backend.Services.GnuCash;
using Backend.Tests.Services;
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
    private readonly Mock<IEmailService> _emailMock;

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
        _invoiceComputation = new InvoiceComputationService(_db, new DbBackedCombinedInvoiceSessionSource(_db));
        _gnuCashSyncMock = new Mock<IGnuCashSyncClient>();
        _emailMock = new Mock<IEmailService>();
        _emailMock.Setup(e => e.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<EmailAttachment?>(), It.IsAny<string?>()))
            .Returns(Task.CompletedTask);
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
            _emailMock.Object,
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
    public async Task DownloadMonthOverviewCsv_NonAdmin_ReturnsForbid()
    {
        await SeedAdminAsync();
        _db.Users.Add(new User { Id = 2, Email = "u@test.com", Name = "U", GoogleId = "g2", Role = UserRole.Base });
        await _db.SaveChangesAsync();

        var result = await BuildController(userId: "2").DownloadMonthOverviewCsv(2024, 4, CancellationToken.None);

        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task DownloadMonthOverviewCsv_NoSessions_ReturnsNotFound()
    {
        await SeedAdminAsync();

        var result = await BuildController().DownloadMonthOverviewCsv(2024, 1, CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task DownloadMonthOverviewCsv_WithSessions_ReturnsCsvWithVatColumns()
    {
        await SeedAdminAsync();
        var appUserId = 2;
        _db.Users.Add(new User { Id = appUserId, Email = "cardholder@test.com", Name = "Card Holder", GoogleId = "g2", Role = UserRole.Base });
        var cid = await AddChargeCardAsync("inv-csv");
        _db.UserChargeCards.Add(new UserChargeCard { UserId = appUserId, ChargeCardId = cid });
        var t = new DateTime(2024, 4, 8, 9, 0, 0, DateTimeKind.Utc);
        _db.ChargingSessions.Add(TestData.Session(cid, 1, t, t.AddHours(1), 10));
        _db.EnergyPrices.Add(new EnergyPrice { Year = 2024, Month = 4, PricePerKwh = 0.30m });
        await _db.SaveChangesAsync();

        var result = await BuildController().DownloadMonthOverviewCsv(2024, 4, CancellationToken.None);

        var file = Assert.IsType<FileContentResult>(result);
        Assert.Equal("text/csv; charset=utf-8", file.ContentType);
        Assert.Equal("InvoiceOverview_2024-04.csv", file.FileDownloadName);
        var csv = System.Text.Encoding.UTF8.GetString(file.FileContents);
        Assert.Contains("Factuurnummer;Gebruiker;kWh;Bedrag excl. BTW;BTW;Bedrag incl. BTW", csv);
        Assert.Contains("INV-2024-04-U2", csv);
        Assert.Contains("Card Holder", csv);
        Assert.Contains("10,00", csv); // kWh
        Assert.Contains("3,00", csv);  // incl. 10 * 0.30
        Assert.Contains("TOTAAL", csv);
    }

    [Fact]
    public async Task GenerateInvoicePdf_NoSessions_ReturnsNotFound()
    {
        await SeedAdminAsync();

        var result = await BuildController().GenerateInvoicePdf(99, 2024, 1, CancellationToken.None);

        var notFound = Assert.IsType<NotFoundObjectResult>(result);
        Assert.Contains("No charging sessions", notFound.Value!.ToString());
    }

    [Fact]
    public async Task GenerateInvoicePdf_WithSessions_ReturnsPdfFile()
    {
        await SeedAdminAsync();
        var appUserId = 2;
        _db.Users.Add(new User { Id = appUserId, Email = "cardholder@test.com", Name = "Card Holder", GoogleId = "g2", Role = UserRole.Base });
        var cid = await AddChargeCardAsync("inv-pdf");
        _db.UserChargeCards.Add(new UserChargeCard { UserId = appUserId, ChargeCardId = cid });
        var t = new DateTime(2024, 4, 8, 9, 0, 0, DateTimeKind.Utc);
        _db.ChargingSessions.Add(TestData.Session(cid, 1, t, t.AddHours(1), 8));
        _db.EnergyPrices.Add(new EnergyPrice { Year = 2024, Month = 4, PricePerKwh = 0.25m });
        _db.MolliePayments.Add(new MolliePayment
        {
            AppUserId = appUserId,
            Year = 2024,
            Month = 4,
            MolliePaymentId = "tr_existing",
            CheckoutUrl = "https://checkout",
            Amount = 2,
            Status = "paid"
        });
        await _db.SaveChangesAsync();

        _pdfMock.Setup(p => p.GenerateInvoicePdf(It.IsAny<InvoiceData>())).Returns(new byte[] { 0x25, 0x50, 0x44, 0x46 });

        var result = await BuildController().GenerateInvoicePdf(appUserId, 2024, 4, CancellationToken.None);

        var file = Assert.IsType<FileContentResult>(result);
        Assert.Equal("application/pdf", file.ContentType);
        Assert.NotEmpty(file.FileContents);
        _pdfMock.Verify(p => p.GenerateInvoicePdf(It.Is<InvoiceData>(d => d.UsePaymentRequestNotice)), Times.Once);
        _mollieMock.Verify(m => m.CreatePaymentAsync(It.IsAny<decimal>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task GenerateInvoicePdf_WithOnlinePaymentLinksEnabled_UsesMollieCheckoutUrl()
    {
        await SeedAdminAsync();
        var appUserId = 2;
        _db.Users.Add(new User { Id = appUserId, Email = "pay@test.com", Name = "Pay", GoogleId = "g2", Role = UserRole.Base });
        var cid = await AddChargeCardAsync("inv-pdf-pay");
        _db.UserChargeCards.Add(new UserChargeCard { UserId = appUserId, ChargeCardId = cid });
        var t = new DateTime(2024, 4, 8, 9, 0, 0, DateTimeKind.Utc);
        _db.ChargingSessions.Add(TestData.Session(cid, 1, t, t.AddHours(1), 8));
        _db.EnergyPrices.Add(new EnergyPrice { Year = 2024, Month = 4, PricePerKwh = 0.25m });
        await _db.SaveChangesAsync();

        _configMock.Setup(c => c["Invoices:EnableOnlinePaymentLinks"]).Returns("true");
        _mollieMock.Setup(m => m.CreatePaymentAsync(It.IsAny<decimal>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(new MolliePaymentResult("tr_new", "https://checkout", "open"));
        _pdfMock.Setup(p => p.GenerateInvoicePdf(It.IsAny<InvoiceData>())).Returns(new byte[] { 0x25, 0x50, 0x44, 0x46 });

        var result = await BuildController().GenerateInvoicePdf(appUserId, 2024, 4, CancellationToken.None);

        Assert.IsType<FileContentResult>(result);
        _pdfMock.Verify(p => p.GenerateInvoicePdf(It.Is<InvoiceData>(d => !d.UsePaymentRequestNotice && d.PaymentUrl == "https://checkout")), Times.Once);
        _mollieMock.Verify(m => m.CreatePaymentAsync(It.IsAny<decimal>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task SendInvoiceEmail_NonAdmin_ReturnsForbid()
    {
        await SeedAdminAsync();
        var appUserId = 2;
        _db.Users.Add(new User { Id = appUserId, Email = "u@test.com", Name = "U", GoogleId = "g2", Role = UserRole.Base });
        await _db.SaveChangesAsync();

        var result = await BuildController(userId: "2").SendInvoiceEmail(appUserId, 2024, 4, CancellationToken.None);

        Assert.IsType<ForbidResult>(result);
        _emailMock.Verify(e => e.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<EmailAttachment?>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public async Task SendInvoiceEmail_WithSessions_SendsPdfAttachment()
    {
        await SeedAdminAsync();
        var appUserId = 2;
        _db.Users.Add(new User { Id = appUserId, Email = "cardholder@test.com", Name = "Card Holder", GoogleId = "g2", Role = UserRole.Base });
        var cid = await AddChargeCardAsync("inv-email");
        _db.UserChargeCards.Add(new UserChargeCard { UserId = appUserId, ChargeCardId = cid });
        var t = new DateTime(2024, 4, 8, 9, 0, 0, DateTimeKind.Utc);
        _db.ChargingSessions.Add(TestData.Session(cid, 1, t, t.AddHours(1), 8));
        _db.EnergyPrices.Add(new EnergyPrice { Year = 2024, Month = 4, PricePerKwh = 0.25m });
        await _db.SaveChangesAsync();

        _pdfMock.Setup(p => p.GenerateInvoicePdf(It.IsAny<InvoiceData>())).Returns(new byte[] { 0x25, 0x50, 0x44, 0x46 });
        _configMock.Setup(c => c["Smtp:InvoiceBcc"]).Returns("zonnewijzer@warmoestuin.nl,jan@deelstroomdelft.nl");

        var result = await BuildController().SendInvoiceEmail(appUserId, 2024, 4, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        _emailMock.Verify(e => e.SendAsync(
            "cardholder@test.com",
            It.Is<string>(s => s.Contains("Factuur")),
            It.IsAny<string>(),
            It.Is<EmailAttachment>(a => a.Content.Length == 4 && a.FileName.EndsWith(".pdf")),
            "zonnewijzer@warmoestuin.nl,jan@deelstroomdelft.nl"), Times.Once);
        var log = await _db.EventLogs.SingleAsync();
        Assert.Contains("Invoice emailed", log.Action);
        Assert.NotNull(ok.Value);
    }

    [Fact]
    public async Task SendInvoiceEmail_NoSessions_ReturnsNotFound()
    {
        await SeedAdminAsync();
        _db.Users.Add(new User { Id = 2, Email = "u@test.com", Name = "U", GoogleId = "g2", Role = UserRole.Base });
        await _db.SaveChangesAsync();

        var result = await BuildController().SendInvoiceEmail(2, 2024, 1, CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task SendInvoiceEmail_OngoingMonth_ReturnsBadRequest()
    {
        await SeedAdminAsync();
        var appUserId = 2;
        _db.Users.Add(new User { Id = appUserId, Email = "u@test.com", Name = "U", GoogleId = "g2", Role = UserRole.Base });
        await _db.SaveChangesAsync();

        var (year, month) = BrusselsTime.CurrentBrusselsYearMonth();
        var result = await BuildController().SendInvoiceEmail(appUserId, year, month, CancellationToken.None);

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Contains("current month", bad.Value?.ToString(), StringComparison.OrdinalIgnoreCase);
        _emailMock.Verify(e => e.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<EmailAttachment?>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public async Task SendMonthInvoiceEmails_NonAdmin_ReturnsForbid()
    {
        await SeedAdminAsync();
        var regular = new User { Id = 2, Email = "u@test.com", Name = "U", GoogleId = "g2", Role = UserRole.Base };
        _db.Users.Add(regular);
        await _db.SaveChangesAsync();

        var result = await BuildController(userId: "2").SendMonthInvoiceEmails(
            new SendMonthInvoicesBody { Year = 2024, Month = 4 }, CancellationToken.None);

        Assert.IsType<ForbidResult>(result);
        _emailMock.Verify(e => e.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<EmailAttachment?>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public async Task SendMonthInvoiceEmails_OngoingMonth_ReturnsBadRequest()
    {
        await SeedAdminAsync();
        var (year, month) = BrusselsTime.CurrentBrusselsYearMonth();

        var result = await BuildController().SendMonthInvoiceEmails(
            new SendMonthInvoicesBody { Year = year, Month = month }, CancellationToken.None);

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Contains("current month", bad.Value?.ToString(), StringComparison.OrdinalIgnoreCase);
        _emailMock.Verify(e => e.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<EmailAttachment?>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public async Task SendMonthInvoiceEmails_NoSessions_ReturnsNotFound()
    {
        await SeedAdminAsync();

        var result = await BuildController().SendMonthInvoiceEmails(
            new SendMonthInvoicesBody { Year = 2024, Month = 1 }, CancellationToken.None);

        var notFound = Assert.IsType<NotFoundObjectResult>(result);
        Assert.Contains("No charging sessions", notFound.Value!.ToString());
    }

    [Fact]
    public async Task SendMonthInvoiceEmails_SendsAllUsersWithSessions()
    {
        await SeedAdminAsync();
        _db.Users.Add(new User { Id = 2, Email = "one@test.com", Name = "One", GoogleId = "g2", Role = UserRole.Base });
        _db.Users.Add(new User { Id = 3, Email = "two@test.com", Name = "Two", GoogleId = "g3", Role = UserRole.Base });
        var cid1 = await AddChargeCardAsync("bulk-email-1");
        var cid2 = await AddChargeCardAsync("bulk-email-2");
        _db.UserChargeCards.Add(new UserChargeCard { UserId = 2, ChargeCardId = cid1 });
        _db.UserChargeCards.Add(new UserChargeCard { UserId = 3, ChargeCardId = cid2 });
        var t = new DateTime(2024, 4, 8, 9, 0, 0, DateTimeKind.Utc);
        _db.ChargingSessions.Add(TestData.Session(cid1, 1, t, t.AddHours(1), 8));
        _db.ChargingSessions.Add(TestData.Session(cid2, 1, t, t.AddHours(1), 5));
        _db.EnergyPrices.Add(new EnergyPrice { Year = 2024, Month = 4, PricePerKwh = 0.25m });
        await _db.SaveChangesAsync();

        _pdfMock.Setup(p => p.GenerateInvoicePdf(It.IsAny<InvoiceData>())).Returns(new byte[] { 0x25, 0x50, 0x44, 0x46 });

        var result = await BuildController().SendMonthInvoiceEmails(
            new SendMonthInvoicesBody { Year = 2024, Month = 4 }, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        _emailMock.Verify(e => e.SendAsync(
            "one@test.com",
            It.Is<string>(s => s.Contains("Factuur")),
            It.IsAny<string>(),
            It.Is<EmailAttachment>(a => a.FileName.EndsWith(".pdf")),
            It.IsAny<string?>()), Times.Once);
        _emailMock.Verify(e => e.SendAsync(
            "two@test.com",
            It.Is<string>(s => s.Contains("Factuur")),
            It.IsAny<string>(),
            It.Is<EmailAttachment>(a => a.FileName.EndsWith(".pdf")),
            It.IsAny<string?>()), Times.Once);

        var json = System.Text.Json.JsonSerializer.Serialize(ok.Value);
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        Assert.Equal(2, doc.RootElement.GetProperty("sent").GetInt32());
        Assert.Equal(0, doc.RootElement.GetProperty("failed").GetInt32());
        Assert.Contains(_db.EventLogs, l => l.Action.Contains("Invoices emailed for"));
    }

    [Fact]
    public async Task SendMonthInvoiceEmails_SkipsUsersWithoutEmail()
    {
        await SeedAdminAsync();
        _db.Users.Add(new User { Id = 2, Email = "has@test.com", Name = "Has", GoogleId = "g2", Role = UserRole.Base });
        _db.Users.Add(new User { Id = 3, Email = " ", Name = "NoMail", GoogleId = "g3", Role = UserRole.Base });
        var cid1 = await AddChargeCardAsync("bulk-skip-1");
        var cid2 = await AddChargeCardAsync("bulk-skip-2");
        _db.UserChargeCards.Add(new UserChargeCard { UserId = 2, ChargeCardId = cid1 });
        _db.UserChargeCards.Add(new UserChargeCard { UserId = 3, ChargeCardId = cid2 });
        var t = new DateTime(2024, 4, 8, 9, 0, 0, DateTimeKind.Utc);
        _db.ChargingSessions.Add(TestData.Session(cid1, 1, t, t.AddHours(1), 8));
        _db.ChargingSessions.Add(TestData.Session(cid2, 1, t, t.AddHours(1), 5));
        _db.EnergyPrices.Add(new EnergyPrice { Year = 2024, Month = 4, PricePerKwh = 0.25m });
        await _db.SaveChangesAsync();

        _pdfMock.Setup(p => p.GenerateInvoicePdf(It.IsAny<InvoiceData>())).Returns(new byte[] { 0x25, 0x50, 0x44, 0x46 });

        var result = await BuildController().SendMonthInvoiceEmails(
            new SendMonthInvoicesBody { Year = 2024, Month = 4 }, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        _emailMock.Verify(e => e.SendAsync(
            "has@test.com",
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<EmailAttachment?>(),
            It.IsAny<string?>()), Times.Once);
        _emailMock.Verify(e => e.SendAsync(
            It.Is<string>(addr => addr != "has@test.com"),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<EmailAttachment?>(),
            It.IsAny<string?>()), Times.Never);

        var json = System.Text.Json.JsonSerializer.Serialize(ok.Value);
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        Assert.Equal(1, doc.RootElement.GetProperty("sent").GetInt32());
        Assert.Equal(1, doc.RootElement.GetProperty("skippedNoEmail").GetInt32());
    }

    [Fact]
    public async Task CreatePaymentLink_OngoingMonth_ReturnsBadRequest()
    {
        await SeedAdminAsync();
        var appUserId = 2;
        _db.Users.Add(new User { Id = appUserId, Email = "p@test.com", Name = "P", GoogleId = "g2", Role = UserRole.Base });
        await _db.SaveChangesAsync();

        var (year, month) = BrusselsTime.CurrentBrusselsYearMonth();
        var result = await BuildController().CreatePaymentLink(appUserId, year, month, CancellationToken.None);

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Contains("current month", bad.Value?.ToString(), StringComparison.OrdinalIgnoreCase);
        _mollieMock.Verify(m => m.CreatePaymentAsync(It.IsAny<decimal>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task CreatePaymentLink_WhenExists_ReturnsExistingWithoutMollieCreate()
    {
        await SeedAdminAsync();
        var appUserId = 2;
        _db.Users.Add(new User { Id = appUserId, Email = "p@test.com", Name = "P", GoogleId = "g2", Role = UserRole.Base });
        var cid = await AddChargeCardAsync("inv-pay-exists");
        _db.UserChargeCards.Add(new UserChargeCard { UserId = appUserId, ChargeCardId = cid });
        _db.MolliePayments.Add(new MolliePayment
        {
            AppUserId = appUserId,
            Year = 2024,
            Month = 3,
            MolliePaymentId = "tr_old",
            CheckoutUrl = "https://old",
            Amount = 10,
            Status = "open"
        });
        await _db.SaveChangesAsync();
        _mollieMock.Setup(m => m.GetPaymentStatusAsync("tr_old")).ReturnsAsync("open");

        var result = await BuildController().CreatePaymentLink(appUserId, 2024, 3, CancellationToken.None);

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
        var appUserId = 2;
        _db.Users.Add(new User { Id = appUserId, Email = "p2@test.com", Name = "P2", GoogleId = "g2", Role = UserRole.Base });
        var cid = await AddChargeCardAsync("inv-pay-exp");
        _db.UserChargeCards.Add(new UserChargeCard { UserId = appUserId, ChargeCardId = cid });
        var t = new DateTime(2024, 3, 10, 0, 0, 0, DateTimeKind.Utc);
        _db.ChargingSessions.Add(TestData.Session(cid, 1, t, t.AddHours(1), 4));
        _db.MolliePayments.Add(new MolliePayment
        {
            AppUserId = appUserId,
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

        var result = await BuildController().CreatePaymentLink(appUserId, 2024, 3, CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal(1, await _db.MolliePayments.CountAsync());
        var payment = await _db.MolliePayments.FirstAsync();
        Assert.Equal("tr_new_after_expired", payment.MolliePaymentId);
    }

    [Fact]
    public async Task CreatePaymentLink_NoSessions_ReturnsNotFound()
    {
        await SeedAdminAsync();
        var appUserId = 2;
        _db.Users.Add(new User { Id = appUserId, Email = "n@test.com", Name = "N", GoogleId = "g2", Role = UserRole.Base });
        var cid = await AddChargeCardAsync("inv-pay-none");
        _db.UserChargeCards.Add(new UserChargeCard { UserId = appUserId, ChargeCardId = cid });
        await _db.SaveChangesAsync();

        var result = await BuildController().CreatePaymentLink(appUserId, 2024, 3, CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task CreatePaymentLink_New_CallsMollieAndPersists()
    {
        await SeedAdminAsync();
        var appUserId = 2;
        _db.Users.Add(new User { Id = appUserId, Email = "n2@test.com", Name = "N2", GoogleId = "g2", Role = UserRole.Base });
        var cid = await AddChargeCardAsync("inv-pay-new");
        _db.UserChargeCards.Add(new UserChargeCard { UserId = appUserId, ChargeCardId = cid });
        var t = new DateTime(2024, 3, 10, 0, 0, 0, DateTimeKind.Utc);
        _db.ChargingSessions.Add(TestData.Session(cid, 1, t, t.AddHours(1), 4));
        await _db.SaveChangesAsync();

        _configMock.Setup(c => c["Mollie:RedirectUrl"]).Returns((string?)null);
        _configMock.Setup(c => c["Mollie:BaseUrl"]).Returns((string?)null);

        _mollieMock
            .Setup(m => m.CreatePaymentAsync(It.IsAny<decimal>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(new MolliePaymentResult("tr_new", "https://new", "open"));

        var result = await BuildController().CreatePaymentLink(appUserId, 2024, 3, CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal(1, await _db.MolliePayments.CountAsync());
        var p = await _db.MolliePayments.FirstAsync();
        Assert.Equal("tr_new", p.MolliePaymentId);
    }

    [Fact]
    public async Task GetPaymentLink_NotFound_WhenMissing()
    {
        await SeedAdminAsync();

        var result = await BuildController().GetPaymentLink(1, 2024, 1, CancellationToken.None);
        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task GetPaymentLink_RefreshesOpenStatus()
    {
        await SeedAdminAsync();
        var appUserId = 2;
        _db.Users.Add(new User { Id = appUserId, Email = "g@test.com", Name = "G", GoogleId = "g2", Role = UserRole.Base });
        await _db.SaveChangesAsync();
        _ = await AddChargeCardAsync("inv-getpay");
        _db.MolliePayments.Add(new MolliePayment
        {
            AppUserId = appUserId,
            Year = 2024,
            Month = 1,
            MolliePaymentId = "tr_open",
            CheckoutUrl = "https://c",
            Amount = 5,
            Status = "open"
        });
        await _db.SaveChangesAsync();

        _mollieMock.Setup(m => m.GetPaymentStatusAsync("tr_open")).ReturnsAsync("paid");

        var result = await BuildController().GetPaymentLink(appUserId, 2024, 1, CancellationToken.None);
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
        _db.MolliePayments.Add(new MolliePayment
        {
            AppUserId = 1,
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
        _db.MolliePayments.Add(new MolliePayment
        {
            AppUserId = 1,
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
        _db.Users.Add(new User { Id = 1, Email = "a@test.com", Name = "A", GoogleId = "g1", Role = UserRole.Admin });
        _db.Users.Add(new User { Id = 2, Email = "u@test.com", Name = "U", GoogleId = "g2", Role = UserRole.Base });
        _db.ChargeCards.Add(new ChargeCard { Value = "inv-del-nc" });
        await _db.SaveChangesAsync();
        _db.MolliePayments.Add(new MolliePayment
        {
            AppUserId = 1,
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
        _db.MolliePayments.Add(new MolliePayment
        {
            AppUserId = 1,
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
        _db.Users.Add(new User { Id = 1, Email = "wh@test.com", Name = "Wh", GoogleId = "g1", Role = UserRole.Base });
        _db.MolliePayments.Add(new MolliePayment
        {
            AppUserId = 1,
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
        _db.Users.Add(new User { Id = 2, Email = "x@test.com", Name = "X", GoogleId = "g2", Role = UserRole.Base });
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
        _db.Users.Add(new User { Id = 2, Email = "x@test.com", Name = "X User", GoogleId = "g2", Role = UserRole.Base });
        await _db.SaveChangesAsync();
        _db.UserChargeCards.Add(new UserChargeCard { UserId = 2, ChargeCardId = cardId });
        await _db.SaveChangesAsync();

        var expectedCu = "CU-USER-2";
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
        _db.Users.Add(new User { Id = 2, Email = "x@test.com", Name = "X", GoogleId = "g2", Role = UserRole.Base });
        await _db.SaveChangesAsync();
        _db.UserChargeCards.Add(new UserChargeCard { UserId = 2, ChargeCardId = cardId });
        await _db.SaveChangesAsync();

        var result = await BuildController().EnsureGnucashCustomer(2, false, CancellationToken.None);
        Assert.IsType<ForbidResult>(result);
    }

    private async Task SeedCombinedInvoiceUserAsync(int appUserId = 2)
    {
        _db.Users.Add(new User { Id = appUserId, Email = "combined@test.com", Name = "Combined User", GoogleId = "gc", Role = UserRole.Base });
        var cid = await AddChargeCardAsync("combined-inv");
        _db.UserChargeCards.Add(new UserChargeCard { UserId = appUserId, ChargeCardId = cid });
        var april = new DateTime(2026, 4, 10, 10, 0, 0, DateTimeKind.Utc);
        _db.ChargingSessions.Add(TestData.Session(cid, 1, april, april.AddHours(1), 10));
        _db.EnergyPrices.Add(new EnergyPrice { Year = 2026, Month = 4, PricePerKwh = 0.25m });
        await _db.SaveChangesAsync();
    }

    [Fact]
    public async Task GetCombinedInvoices_NonAdmin_ReturnsForbid()
    {
        await SeedAdminAsync();
        var regular = new User { Id = 2, Email = "u@test.com", Name = "U", GoogleId = "g2", Role = UserRole.Base };
        _db.Users.Add(regular);
        await _db.SaveChangesAsync();

        var result = await BuildController(userId: "2").GetCombinedInvoices(2026, 4, 2026, 5, CancellationToken.None);
        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task GetCombinedInvoices_Admin_ReturnsUsers()
    {
        await SeedAdminAsync();
        await SeedCombinedInvoiceUserAsync();

        var result = await BuildController().GetCombinedInvoices(2026, 4, 2026, 5, CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(result);
        var json = System.Text.Json.JsonSerializer.Serialize(ok.Value);
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        Assert.Equal("April – Mei 2026", doc.RootElement.GetProperty("PeriodLabel").GetString());
        Assert.Equal(1, doc.RootElement.GetProperty("Users").GetArrayLength());
    }

    [Fact]
    public async Task GenerateCombinedInvoicePdf_WithSessions_ReturnsPdfFile()
    {
        await SeedAdminAsync();
        await SeedCombinedInvoiceUserAsync();
        _pdfMock.Setup(p => p.GenerateInvoicePdf(It.IsAny<InvoiceData>())).Returns(new byte[] { 0x25, 0x50, 0x44, 0x46 });

        var result = await BuildController().GenerateCombinedInvoicePdf(2, 2026, 4, 2026, 5, CancellationToken.None);

        var file = Assert.IsType<FileContentResult>(result);
        Assert.Equal("application/pdf", file.ContentType);
        _pdfMock.Verify(p => p.GenerateInvoicePdf(It.Is<InvoiceData>(d => d.UsePaymentRequestNotice)), Times.Once);
    }

    [Fact]
    public async Task GenerateCombinedInvoicePdf_NoSessions_ReturnsNotFound()
    {
        await SeedAdminAsync();

        var result = await BuildController().GenerateCombinedInvoicePdf(99, 2026, 4, 2026, 5, CancellationToken.None);
        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task InsertCombinedGnucashInvoices_Admin_ReturnsForbid()
    {
        await SeedAdminAsync();
        await SeedCombinedInvoiceUserAsync();
        _configMock.Setup(c => c["GnuCashSync:BaseUrl"]).Returns("http://127.0.0.1:8787");

        var result = await BuildController().InsertCombinedGnucashInvoices(new GnuCashCombinedInsertBody
        {
            StartYear = 2026,
            StartMonth = 4,
            EndYear = 2026,
            EndMonth = 5,
            AppUserId = 2
        }, CancellationToken.None);

        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task InsertCombinedGnucashInvoices_Owner_CallsPython()
    {
        await SeedOwnerAsync();
        await SeedCombinedInvoiceUserAsync();
        _configMock.Setup(c => c["GnuCashSync:BaseUrl"]).Returns("http://127.0.0.1:8787");
        _gnuCashSyncMock
            .Setup(g => g.InsertInvoicesAsync(It.IsAny<GnuCashInsertRequest>(), It.IsAny<string?>(), false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GnuCashInsertResponse
            {
                Results = [new GnuCashInsertResultItem { InvoiceId = "INV-2026-04-05-U2", Status = "created" }]
            });

        var result = await BuildController().InsertCombinedGnucashInvoices(new GnuCashCombinedInsertBody
        {
            StartYear = 2026,
            StartMonth = 4,
            EndYear = 2026,
            EndMonth = 5,
            AppUserId = 2
        }, CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        _gnuCashSyncMock.Verify(
            g => g.InsertInvoicesAsync(
                It.Is<GnuCashInsertRequest>(r => r.Invoices.Count == 1 && r.Invoices[0].InvoiceId == "INV-2026-04-05-U2"),
                It.IsAny<string?>(),
                false,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
