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
    public async Task BuildInvoiceDataForAppUser_ExcludesOngoingAndUsesEndMonth()
    {
        const int appUserId = 6;
        _db.Users.Add(new User { Id = appUserId, Email = "billing@test.com", Name = "Billing User", GoogleId = "g6", Role = UserRole.Base });
        var cid = new ChargeCard { Value = "billing-card" };
        _db.ChargeCards.Add(cid);
        await _db.SaveChangesAsync();
        _db.UserChargeCards.Add(new UserChargeCard { UserId = appUserId, ChargeCardId = cid.Id });

        var aprilStart = new DateTime(2026, 4, 28, 20, 0, 0, DateTimeKind.Utc);
        var mayEnd = new DateTime(2026, 5, 2, 8, 0, 0, DateTimeKind.Utc);
        _db.ChargingSessions.Add(TestData.Session(cid.Id, 1, aprilStart, null, 5));
        _db.ChargingSessions.Add(TestData.Session(cid.Id, 1, aprilStart, mayEnd, 12));
        _db.EnergyPrices.Add(new EnergyPrice { Year = 2026, Month = 4, PricePerKwh = 0.20m });
        _db.EnergyPrices.Add(new EnergyPrice { Year = 2026, Month = 5, PricePerKwh = 0.30m });
        await _db.SaveChangesAsync();

        var aprilInvoice = await _service.BuildInvoiceDataForAppUserAsync(appUserId, 2026, 4);
        var mayInvoice = await _service.BuildInvoiceDataForAppUserAsync(appUserId, 2026, 5);

        Assert.Null(aprilInvoice);
        Assert.NotNull(mayInvoice);
        Assert.Single(mayInvoice!.Sessions);
        Assert.Equal(12m, mayInvoice.TotalKwh);
        Assert.Equal(3.6m, mayInvoice.TotalAmount);
    }

    [Fact]
    public async Task BuildInvoiceDataForAppUser_UsesBankTransferLayoutLikeCombined()
    {
        const int appUserId = 5;
        _db.Users.Add(new User { Id = appUserId, Email = "monthly@test.com", Name = "Monthly User", GoogleId = "g5", Role = UserRole.Base });
        var cid = new ChargeCard { Value = "monthly-card" };
        _db.ChargeCards.Add(cid);
        await _db.SaveChangesAsync();
        _db.UserChargeCards.Add(new UserChargeCard { UserId = appUserId, ChargeCardId = cid.Id });

        var april = new DateTime(2026, 4, 10, 10, 0, 0, DateTimeKind.Utc);
        _db.ChargingSessions.Add(TestData.Session(cid.Id, 1, april, april.AddHours(1), 10));
        _db.EnergyPrices.Add(new EnergyPrice { Year = 2026, Month = 4, PricePerKwh = 0.20m });
        _db.MolliePayments.Add(new MolliePayment
        {
            AppUserId = appUserId,
            Year = 2026,
            Month = 4,
            MolliePaymentId = "tr_monthly",
            CheckoutUrl = "https://checkout.example",
            Amount = 2,
            Status = "paid"
        });
        await _db.SaveChangesAsync();

        var data = await _service.BuildInvoiceDataForAppUserAsync(appUserId, 2026, 4);

        Assert.NotNull(data);
        Assert.Equal("April 2026", data!.MonthName);
        Assert.True(data.UsePaymentRequestNotice);
        Assert.Null(data.PaymentUrl);
        Assert.False(data.IsPaid);
    }

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
