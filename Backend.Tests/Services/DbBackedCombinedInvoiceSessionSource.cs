using Backend;
using Backend.Data;
using Backend.Models;
using Backend.Services;
using Microsoft.EntityFrameworkCore;

namespace Backend.Tests.Services;

/// <summary>Test double: maps seeded DB charging sessions to combined invoice Smappee fetch results.</summary>
internal sealed class DbBackedCombinedInvoiceSessionSource : ISmappeeCombinedInvoiceSessionSource
{
    private readonly ApplicationDbContext _db;

    public DbBackedCombinedInvoiceSessionSource(ApplicationDbContext db) => _db = db;

    public async Task<CombinedInvoiceSmappeeFetchResult> FetchSessionsForRangeAsync(
        int startYear,
        int startMonth,
        int endYear,
        int endMonth,
        CancellationToken cancellationToken = default)
    {
        var (startMs, endMs) = InvoiceComputationService.RangeUnixMilliseconds(startYear, startMonth, endYear, endMonth);
        var dbSessions = await _db.ChargingSessions
            .Where(cs => cs.FromTimestamp >= startMs && cs.FromTimestamp < endMs)
            .OrderBy(cs => cs.FromTimestamp)
            .ToListAsync(cancellationToken);

        var userLinks = await _db.UserChargeCards.AsNoTracking().ToListAsync(cancellationToken);
        var userIdByCardId = userLinks.ToDictionary(x => x.ChargeCardId, x => x.UserId);

        var mapped = dbSessions
            .Where(cs => userIdByCardId.ContainsKey(cs.ChargeCardId)
                && CombinedInvoiceSessionRules.IncludeSession(cs.FromTimestamp, cs.ToTimestamp, startMs, endMs))
            .Select(cs => new CombinedInvoiceSourceSession
            {
                AppUserId = userIdByCardId[cs.ChargeCardId],
                ChargeCardId = cs.ChargeCardId,
                FromTimestampMs = cs.FromTimestamp,
                ToTimestampMs = cs.ToTimestamp,
                StationId = cs.StationId,
                SerialNumber = cs.SerialNumber,
                Connector = null,
                ChargedKwh = Math.Round(ChargingSessionEnergy.ChargedKwh(cs), 2)
            })
            .ToList();

        var byUser = mapped
            .GroupBy(s => s.AppUserId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<CombinedInvoiceSourceSession>)g.ToList());

        return new CombinedInvoiceSmappeeFetchResult
        {
            Sessions = mapped,
            ByAppUserId = byUser,
            SkippedUnmapped = 0,
            SkippedInvalid = 0
        };
    }
}
