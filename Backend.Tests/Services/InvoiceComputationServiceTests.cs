using Backend.Data;
using Backend.Models;
using Backend.Services;
using Backend.Tests.Services;
using Microsoft.EntityFrameworkCore;

namespace Backend.Tests.Services;

public class InvoiceComputationServiceTests : IDisposable
{
    private readonly ApplicationDbContext _db;
    private readonly InvoiceComputationService _service;

    public InvoiceComputationServiceTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new ApplicationDbContext(options);
        _service = new InvoiceComputationService(_db, new DbBackedCombinedInvoiceSessionSource(_db));
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task BuildCombinedInvoiceData_UsesPerMonthRates()
    {
        const int appUserId = 2;
        _db.Users.Add(new User { Id = appUserId, Email = "u@test.com", Name = "User", GoogleId = "g2", Role = UserRole.Base });
        var cid = new ChargeCard { Value = "combined-card" };
        _db.ChargeCards.Add(cid);
        await _db.SaveChangesAsync();
        _db.UserChargeCards.Add(new UserChargeCard { UserId = appUserId, ChargeCardId = cid.Id });

        var april = new DateTime(2026, 4, 10, 10, 0, 0, DateTimeKind.Utc);
        var may = new DateTime(2026, 5, 10, 10, 0, 0, DateTimeKind.Utc);
        _db.ChargingSessions.Add(TestData.Session(cid.Id, 1, april, april.AddHours(1), 10));
        _db.ChargingSessions.Add(TestData.Session(cid.Id, 1, may, may.AddHours(1), 20));
        _db.EnergyPrices.Add(new EnergyPrice { Year = 2026, Month = 4, PricePerKwh = 0.20m });
        _db.EnergyPrices.Add(new EnergyPrice { Year = 2026, Month = 5, PricePerKwh = 0.30m });
        await _db.SaveChangesAsync();

        var data = await _service.BuildCombinedInvoiceDataForAppUserAsync(appUserId, 2026, 4, 2026, 5);

        Assert.NotNull(data);
        Assert.Equal("INV-2026-04-05-U2", data!.InvoiceNumber);
        Assert.Equal("April – Mei 2026", data.MonthName);
        Assert.True(data.UsePaymentRequestNotice);
        Assert.Null(data.PaymentUrl);
        Assert.Equal(30m, data.TotalKwh);
        Assert.Equal(2m, data.Sessions[0].Amount);
        Assert.Equal(6m, data.Sessions[1].Amount);
        Assert.Equal(8m, data.TotalAmount);
    }

    [Fact]
    public async Task BuildCombinedInvoiceData_SingleMonthInRange_ReturnsInvoice()
    {
        const int appUserId = 3;
        _db.Users.Add(new User { Id = appUserId, Email = "u2@test.com", Name = "User2", GoogleId = "g3", Role = UserRole.Base });
        var cid = new ChargeCard { Value = "combined-card-2" };
        _db.ChargeCards.Add(cid);
        await _db.SaveChangesAsync();
        _db.UserChargeCards.Add(new UserChargeCard { UserId = appUserId, ChargeCardId = cid.Id });

        var april = new DateTime(2026, 4, 15, 10, 0, 0, DateTimeKind.Utc);
        _db.ChargingSessions.Add(TestData.Session(cid.Id, 1, april, april.AddHours(1), 5));
        _db.EnergyPrices.Add(new EnergyPrice { Year = 2026, Month = 4, PricePerKwh = 0.25m });
        await _db.SaveChangesAsync();

        var data = await _service.BuildCombinedInvoiceDataForAppUserAsync(appUserId, 2026, 4, 2026, 5);

        Assert.NotNull(data);
        Assert.Equal(5m, data!.TotalKwh);
        Assert.Equal(1.25m, data.TotalAmount);
    }

    [Fact]
    public async Task BuildCombinedInvoiceData_NoSessions_ReturnsNull()
    {
        const int appUserId = 4;
        _db.Users.Add(new User { Id = appUserId, Email = "u3@test.com", Name = "User3", GoogleId = "g4", Role = UserRole.Base });
        var cid = new ChargeCard { Value = "empty-card" };
        _db.ChargeCards.Add(cid);
        await _db.SaveChangesAsync();
        _db.UserChargeCards.Add(new UserChargeCard { UserId = appUserId, ChargeCardId = cid.Id });
        await _db.SaveChangesAsync();

        var data = await _service.BuildCombinedInvoiceDataForAppUserAsync(appUserId, 2026, 4, 2026, 5);

        Assert.Null(data);
    }

    [Fact]
    public async Task BuildInvoiceDataForAppUser_SessionSpanningMonths_UsesEndMonth()
    {
        const int appUserId = 5;
        _db.Users.Add(new User { Id = appUserId, Email = "span@test.com", Name = "Span", GoogleId = "g5", Role = UserRole.Base });
        var cid = new ChargeCard { Value = "span-card" };
        _db.ChargeCards.Add(cid);
        await _db.SaveChangesAsync();
        _db.UserChargeCards.Add(new UserChargeCard { UserId = appUserId, ChargeCardId = cid.Id });

        var brusselsTz = TimeZoneInfo.FindSystemTimeZoneById(
            OperatingSystem.IsWindows() ? "W. Europe Standard Time" : "Europe/Brussels");
        var startLocal = new DateTime(2026, 6, 30, 22, 0, 0, DateTimeKind.Unspecified);
        var endLocal = new DateTime(2026, 7, 1, 2, 0, 0, DateTimeKind.Unspecified);
        var startUtc = TimeZoneInfo.ConvertTimeToUtc(startLocal, brusselsTz);
        var endUtc = TimeZoneInfo.ConvertTimeToUtc(endLocal, brusselsTz);

        _db.ChargingSessions.Add(TestData.Session(cid.Id, 1, startUtc, endUtc, 12));
        _db.EnergyPrices.Add(new EnergyPrice { Year = 2026, Month = 6, PricePerKwh = 0.20m });
        _db.EnergyPrices.Add(new EnergyPrice { Year = 2026, Month = 7, PricePerKwh = 0.30m });
        await _db.SaveChangesAsync();

        var juneInvoice = await _service.BuildInvoiceDataForAppUserAsync(appUserId, 2026, 6);
        var julyInvoice = await _service.BuildInvoiceDataForAppUserAsync(appUserId, 2026, 7);

        Assert.Null(juneInvoice);
        Assert.NotNull(julyInvoice);
        Assert.Equal(12m, julyInvoice!.TotalKwh);
        Assert.Equal(0.30m, julyInvoice.RatePerKwh);
        Assert.Equal(3.60m, julyInvoice.TotalAmount);
        Assert.Single(julyInvoice.Sessions);
    }

    [Fact]
    public async Task BuildInvoiceDataForAppUser_OngoingSession_ExcludedUntilEnded()
    {
        const int appUserId = 6;
        _db.Users.Add(new User { Id = appUserId, Email = "ongoing@test.com", Name = "Ongoing", GoogleId = "g6", Role = UserRole.Base });
        var cid = new ChargeCard { Value = "ongoing-card" };
        _db.ChargeCards.Add(cid);
        await _db.SaveChangesAsync();
        _db.UserChargeCards.Add(new UserChargeCard { UserId = appUserId, ChargeCardId = cid.Id });

        var june = new DateTime(2026, 6, 15, 10, 0, 0, DateTimeKind.Utc);
        _db.ChargingSessions.Add(TestData.Session(cid.Id, 1, june, endUtc: null, 5));
        _db.EnergyPrices.Add(new EnergyPrice { Year = 2026, Month = 6, PricePerKwh = 0.25m });
        await _db.SaveChangesAsync();

        var juneInvoice = await _service.BuildInvoiceDataForAppUserAsync(appUserId, 2026, 6);

        Assert.Null(juneInvoice);
    }

    [Fact]
    public async Task GetAppUserIdsForRange_ReturnsUsersWithSessionsInWindow()
    {
        var cid = new ChargeCard { Value = "range-card" };
        _db.ChargeCards.Add(cid);
        await _db.SaveChangesAsync();
        _db.UserChargeCards.Add(new UserChargeCard { UserId = 10, ChargeCardId = cid.Id });

        var april = new DateTime(2026, 4, 5, 10, 0, 0, DateTimeKind.Utc);
        _db.ChargingSessions.Add(TestData.Session(cid.Id, 1, april, april.AddHours(1), 3));
        await _db.SaveChangesAsync();

        var ids = await _service.GetAppUserIdsForRangeAsync(2026, 4, 2026, 5, null);

        Assert.Single(ids);
        Assert.Equal(10, ids[0]);
    }
}
