using Backend;
using Backend.Data;
using Backend.Models;
using Backend.Services;
using Microsoft.EntityFrameworkCore;

namespace Backend.Tests.Services;

public class DbBackedCombinedInvoiceSessionSourceTests : IDisposable
{
    private readonly ApplicationDbContext _db;
    private readonly DbBackedCombinedInvoiceSessionSource _source;

    public DbBackedCombinedInvoiceSessionSourceTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new ApplicationDbContext(options);
        _source = new DbBackedCombinedInvoiceSessionSource(_db);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task FetchSessionsForRange_ExcludesMaySessionEndingInJune()
    {
        const int appUserId = 2;
        _db.Users.Add(new User { Id = appUserId, Email = "u@test.com", Name = "User", GoogleId = "g2", Role = UserRole.Base });
        var cid = new ChargeCard { Value = "card-1" };
        _db.ChargeCards.Add(cid);
        await _db.SaveChangesAsync();
        _db.UserChargeCards.Add(new UserChargeCard { UserId = appUserId, ChargeCardId = cid.Id });

        var mayStart = new DateTime(2026, 5, 28, 10, 0, 0, DateTimeKind.Utc);
        var mayEnd = new DateTime(2026, 5, 30, 12, 0, 0, DateTimeKind.Utc);
        var juneEnd = new DateTime(2026, 6, 2, 8, 0, 0, DateTimeKind.Utc);

        _db.ChargingSessions.Add(TestData.Session(cid.Id, 1, mayStart, mayEnd, 10));
        _db.ChargingSessions.Add(TestData.Session(cid.Id, 1, mayStart.AddDays(-1), juneEnd, 20));
        await _db.SaveChangesAsync();

        var result = await _source.FetchSessionsForRangeAsync(2026, 4, 2026, 5);

        Assert.Single(result.Sessions);
        Assert.Equal(10m, result.Sessions[0].ChargedKwh);
    }
}
