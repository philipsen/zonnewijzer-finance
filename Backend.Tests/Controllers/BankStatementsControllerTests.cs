using System.Text;
using Backend.Controllers;
using Backend.Data;
using Backend.Models;
using Backend.Services;
using Backend.Services.Bank;
using Backend.Tests.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Backend.Tests.Controllers;

public class BankStatementsControllerTests : IDisposable
{
    private const string Csv =
        "\"06-10-2026\",\"NL40TRIO0321247604\",\"44,47\",\"Credit\",\"R. en M. Hendriks B.V.\",\"RABONL2U NL22RABO0129901962\",\"ET\",\"59 20016328  INV-2026-08-U14 INV-2026-08-U14 Laadkosten auto T. van Leeuwen - Aug-2026 2627 AN Delft\",\"1.592,97\"";

    private readonly ApplicationDbContext _db;
    private readonly Mock<IJwtService> _jwt = new();

    public BankStatementsControllerTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new ApplicationDbContext(options);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task Upload_MatchesLegacyAugustInvoice_AndConfirmMarksItPaid()
    {
        _db.Users.Add(new User { Id = 1, Email = "admin@test.com", Name = "Admin", GoogleId = "g1", Role = UserRole.Admin });
        _db.Users.Add(new User { Id = 14, Email = "t@test.com", Name = "T. van Leeuwen", GoogleId = "g14", Role = UserRole.Base });
        var card = new ChargeCard { Value = "card-14" };
        _db.ChargeCards.Add(card);
        await _db.SaveChangesAsync();
        _db.UserChargeCards.Add(new UserChargeCard { UserId = 14, ChargeCardId = card.Id });
        var when = new DateTime(2026, 8, 10, 10, 0, 0, DateTimeKind.Utc);
        _db.ChargingSessions.Add(TestData.Session(card.Id, 1, when, when.AddHours(1), 44.47m));
        _db.EnergyPrices.Add(new EnergyPrice { Year = 2026, Month = 8, PricePerKwh = 1m });
        await _db.SaveChangesAsync();

        var upload = await Controller().Upload(CsvFile(Csv), CancellationToken.None);
        var uploadOk = Assert.IsType<OkObjectResult>(upload);
        var uploaded = System.Text.Json.JsonSerializer.Serialize(uploadOk.Value);
        using var doc = System.Text.Json.JsonDocument.Parse(uploaded);
        Assert.Equal(1, doc.RootElement.GetProperty("Imported").GetInt32());
        var open = doc.RootElement.GetProperty("Review").GetProperty("Open");
        Assert.Equal(1, open.GetArrayLength());
        Assert.Equal("Certain", open[0].GetProperty("SuggestionKind").GetString());
        Assert.Equal(14, open[0].GetProperty("AppUserId").GetInt32());
        Assert.Equal(8, open[0].GetProperty("Month").GetInt32());
        var id = open[0].GetProperty("Id").GetInt32();

        var confirm = await Controller().Confirm(id, new ConfirmBankMatchRequest
        {
            AppUserId = 14,
            Year = 2026,
            Month = 8
        }, CancellationToken.None);
        Assert.IsType<OkObjectResult>(confirm);

        var again = await Controller().Upload(CsvFile(Csv), CancellationToken.None);
        var againOk = Assert.IsType<OkObjectResult>(again);
        using var againDoc = System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(againOk.Value));
        Assert.Equal(0, againDoc.RootElement.GetProperty("Imported").GetInt32());
        Assert.Equal(1, againDoc.RootElement.GetProperty("Duplicates").GetInt32());
        Assert.Equal(0, againDoc.RootElement.GetProperty("Review").GetProperty("Open").GetArrayLength());
        Assert.Equal(1, againDoc.RootElement.GetProperty("Review").GetProperty("Confirmed").GetArrayLength());

        var invoice = new InvoiceComputationService(_db, new DbBackedCombinedInvoiceSessionSource(_db));
        var data = await invoice.BuildInvoiceDataForAppUserAsync(14, 2026, 8);
        Assert.True(data!.IsPaid);
    }

    [Fact]
    public async Task Confirm_AllowsSeveralPaymentsUntilTheInvoiceIsCovered()
    {
        _db.Users.Add(new User { Id = 1, Email = "admin@test.com", Name = "Admin", GoogleId = "g1", Role = UserRole.Admin });
        _db.Users.Add(new User { Id = 14, Email = "t@test.com", Name = "T. van Leeuwen", GoogleId = "g14", Role = UserRole.Base });
        var card = new ChargeCard { Value = "card-14" };
        _db.ChargeCards.Add(card);
        await _db.SaveChangesAsync();
        _db.UserChargeCards.Add(new UserChargeCard { UserId = 14, ChargeCardId = card.Id });
        var when = new DateTime(2026, 8, 10, 10, 0, 0, DateTimeKind.Utc);
        _db.ChargingSessions.Add(TestData.Session(card.Id, 1, when, when.AddHours(1), 44.47m));
        _db.EnergyPrices.Add(new EnergyPrice { Year = 2026, Month = 8, PricePerKwh = 1m });
        await _db.SaveChangesAsync();

        const string first =
            "\"06-10-2026\",\"NL40TRIO0321247604\",\"20,00\",\"Credit\",\"R. en M. Hendriks B.V.\",\"RABONL2U NL22RABO0129901962\",\"ET\",\"INV-2026-08-U14 deel 1\",\"1.500,00\"";
        const string second =
            "\"07-10-2026\",\"NL40TRIO0321247604\",\"24,47\",\"Credit\",\"R. en M. Hendriks B.V.\",\"RABONL2U NL22RABO0129901962\",\"ET\",\"INV-2026-08-U14 deel 2\",\"1.524,47\"";
        const string extra =
            "\"08-10-2026\",\"NL40TRIO0321247604\",\"1,00\",\"Credit\",\"R. en M. Hendriks B.V.\",\"RABONL2U NL22RABO0129901962\",\"ET\",\"INV-2026-08-U14 extra\",\"1.525,47\"";

        var upload = await Controller().Upload(CsvFile(first + "\n" + second), CancellationToken.None);
        var uploadOk = Assert.IsType<OkObjectResult>(upload);
        using var doc = System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(uploadOk.Value));
        var open = doc.RootElement.GetProperty("Review").GetProperty("Open");
        Assert.Equal(2, open.GetArrayLength());
        Assert.Equal("Partial", open[0].GetProperty("SuggestionKind").GetString());
        Assert.Equal("Partial", open[1].GetProperty("SuggestionKind").GetString());

        var firstId = open.EnumerateArray().First(row => row.GetProperty("Amount").GetDecimal() == 20m).GetProperty("Id").GetInt32();
        var secondId = open.EnumerateArray().First(row => row.GetProperty("Amount").GetDecimal() == 24.47m).GetProperty("Id").GetInt32();
        var request = new ConfirmBankMatchRequest { AppUserId = 14, Year = 2026, Month = 8 };

        Assert.IsType<OkObjectResult>(await Controller().Confirm(firstId, request, CancellationToken.None));
        var invoice = new InvoiceComputationService(_db, new DbBackedCombinedInvoiceSessionSource(_db));
        Assert.False((await invoice.BuildInvoiceDataForAppUserAsync(14, 2026, 8))!.IsPaid);

        Assert.IsType<OkObjectResult>(await Controller().Confirm(secondId, request, CancellationToken.None));
        Assert.True((await invoice.BuildInvoiceDataForAppUserAsync(14, 2026, 8))!.IsPaid);
        Assert.Equal(2, await _db.InvoiceBankMatches.CountAsync());

        var extraUpload = await Controller().Upload(CsvFile(extra), CancellationToken.None);
        var extraOk = Assert.IsType<OkObjectResult>(extraUpload);
        using var extraDoc = System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(extraOk.Value));
        var extraOpen = extraDoc.RootElement.GetProperty("Review").GetProperty("Open");
        Assert.Equal("AlreadyPaid", extraOpen[0].GetProperty("SuggestionKind").GetString());
        var extraId = extraOpen[0].GetProperty("Id").GetInt32();

        var rejected = await Controller().Confirm(extraId, request, CancellationToken.None);
        Assert.IsType<ConflictObjectResult>(rejected);
    }

    [Fact]
    public async Task Confirm_SplitsOnePaymentAcrossTwoInvoices()
    {
        _db.Users.Add(new User { Id = 1, Email = "admin@test.com", Name = "Admin", GoogleId = "g1", Role = UserRole.Admin });
        _db.Users.Add(new User { Id = 14, Email = "t@test.com", Name = "T. van Leeuwen", GoogleId = "g14", Role = UserRole.Base });
        var card = new ChargeCard { Value = "card-14" };
        _db.ChargeCards.Add(card);
        await _db.SaveChangesAsync();
        _db.UserChargeCards.Add(new UserChargeCard { UserId = 14, ChargeCardId = card.Id });
        var july = new DateTime(2026, 7, 10, 10, 0, 0, DateTimeKind.Utc);
        var august = new DateTime(2026, 8, 10, 10, 0, 0, DateTimeKind.Utc);
        _db.ChargingSessions.Add(TestData.Session(card.Id, 1, july, july.AddHours(1), 24.47m));
        _db.ChargingSessions.Add(TestData.Session(card.Id, 1, august, august.AddHours(1), 20m));
        _db.EnergyPrices.Add(new EnergyPrice { Year = 2026, Month = 7, PricePerKwh = 1m });
        await _db.SaveChangesAsync();

        const string csv =
            "\"06-10-2026\",\"NL40TRIO0321247604\",\"44,47\",\"Credit\",\"R. en M. Hendriks B.V.\",\"RABONL2U NL22RABO0129901962\",\"ET\",\"INV-2026-07-U14 INV-2026-08-U14\",\"1.592,97\"";

        var upload = await Controller().Upload(CsvFile(csv), CancellationToken.None);
        var uploadOk = Assert.IsType<OkObjectResult>(upload);
        using var doc = System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(uploadOk.Value));
        var open = doc.RootElement.GetProperty("Review").GetProperty("Open");
        Assert.Equal("Split", open[0].GetProperty("SuggestionKind").GetString());
        Assert.Equal(2, open[0].GetProperty("Shares").GetArrayLength());
        var id = open[0].GetProperty("Id").GetInt32();

        var first = await Controller().Confirm(id, new ConfirmBankMatchRequest
        {
            AppUserId = 14,
            Year = 2026,
            Month = 7,
            Amount = 24.47m
        }, CancellationToken.None);
        var firstOk = Assert.IsType<OkObjectResult>(first);
        using var firstDoc = System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(firstOk.Value));
        Assert.Equal(1, firstDoc.RootElement.GetProperty("Open").GetArrayLength());
        Assert.Equal(20m, firstDoc.RootElement.GetProperty("Open")[0].GetProperty("UnallocatedAmount").GetDecimal());
        Assert.Equal(0, firstDoc.RootElement.GetProperty("Confirmed").GetArrayLength());

        var invoice = new InvoiceComputationService(_db, new DbBackedCombinedInvoiceSessionSource(_db));
        Assert.True((await invoice.BuildInvoiceDataForAppUserAsync(14, 2026, 7))!.IsPaid);
        Assert.False((await invoice.BuildInvoiceDataForAppUserAsync(14, 2026, 8))!.IsPaid);

        var second = await Controller().Confirm(id, new ConfirmBankMatchRequest
        {
            AppUserId = 14,
            Year = 2026,
            Month = 8,
            Amount = 20m
        }, CancellationToken.None);
        var secondOk = Assert.IsType<OkObjectResult>(second);
        using var secondDoc = System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(secondOk.Value));
        Assert.Equal(0, secondDoc.RootElement.GetProperty("Open").GetArrayLength());
        Assert.Equal(2, secondDoc.RootElement.GetProperty("Confirmed").GetArrayLength());
        Assert.True((await invoice.BuildInvoiceDataForAppUserAsync(14, 2026, 8))!.IsPaid);

        var matchId = secondDoc.RootElement.GetProperty("Confirmed")[0].GetProperty("MatchId").GetInt32();
        var reopened = await Controller().Reopen(id, matchId, CancellationToken.None);
        var reopenedOk = Assert.IsType<OkObjectResult>(reopened);
        using var reopenedDoc = System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(reopenedOk.Value));
        Assert.Equal(1, reopenedDoc.RootElement.GetProperty("Open").GetArrayLength());
        Assert.Equal(1, reopenedDoc.RootElement.GetProperty("Open")[0].GetProperty("Allocations").GetArrayLength());
    }

    [Fact]
    public async Task Upload_WithoutFile_ReturnsBadRequest()
    {
        _db.Users.Add(new User { Id = 1, Email = "admin@test.com", Name = "Admin", GoogleId = "g1", Role = UserRole.Admin });
        await _db.SaveChangesAsync();

        var result = await Controller().Upload(null, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    private BankStatementsController Controller()
    {
        _jwt.Setup(jwt => jwt.ValidateToken(It.IsAny<string>())).Returns("1");
        var http = new DefaultHttpContext();
        http.Request.Headers.Authorization = "Bearer t";
        return new BankStatementsController(
            _db,
            _jwt.Object,
            new BankStatementService(_db, new InvoicePaymentCatalog(_db)),
            NullLogger<BankStatementsController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = http }
        };
    }

    private static IFormFile CsvFile(string csv)
    {
        var bytes = Encoding.UTF8.GetBytes(csv);
        var stream = new MemoryStream(bytes);
        return new FormFile(stream, 0, bytes.Length, "file", "triodos.csv");
    }
}
