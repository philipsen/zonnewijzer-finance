using Backend.Data;
using Backend.Models;
using Microsoft.EntityFrameworkCore;

namespace Backend.Services.Bank;

public sealed class InvoicePaymentCatalog
{
    private readonly ApplicationDbContext _context;

    public InvoicePaymentCatalog(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<InvoiceCandidate>> LoadAsync(CancellationToken cancellationToken = default)
    {
        var sessions = await _context.ChargingSessions
            .AsNoTracking()
            .Where(session => session.ToTimestamp.HasValue)
            .Select(session => new
            {
                session.ChargeCardId,
                ToTimestamp = session.ToTimestamp!.Value,
                session.StartReading,
                session.StopReading
            })
            .ToListAsync(cancellationToken);

        var corrections = await _context.InvoiceCorrections
            .AsNoTracking()
            .Select(correction => new { correction.UserId, correction.Date, correction.Amount })
            .ToListAsync(cancellationToken);

        if (sessions.Count == 0 && corrections.Count == 0)
            return Array.Empty<InvoiceCandidate>();

        var links = await _context.UserChargeCards
            .AsNoTracking()
            .Select(link => new { link.UserId, link.ChargeCardId })
            .ToListAsync(cancellationToken);
        var cardsByUser = links
            .GroupBy(link => link.UserId)
            .ToDictionary(group => group.Key, group => group.Select(link => link.ChargeCardId).ToHashSet());

        var users = await _context.Users
            .AsNoTracking()
            .Select(user => new { user.Id, user.Name })
            .ToListAsync(cancellationToken);
        var names = users.ToDictionary(user => user.Id, user => user.Name);

        var prices = await _context.EnergyPrices
            .AsNoTracking()
            .OrderByDescending(price => price.Year)
            .ThenByDescending(price => price.Month)
            .ToListAsync(cancellationToken);

        var paid = new HashSet<(int UserId, int Year, int Month)>();
        var molliePaid = await _context.MolliePayments
            .AsNoTracking()
            .Where(payment => payment.Status == "paid")
            .Select(payment => new { payment.AppUserId, payment.Year, payment.Month })
            .ToListAsync(cancellationToken);
        foreach (var payment in molliePaid)
            paid.Add((payment.AppUserId, payment.Year, payment.Month));

        var bankPaid = await _context.InvoiceBankMatches
            .AsNoTracking()
            .Select(match => new { match.AppUserId, match.Year, match.Month, match.Amount })
            .ToListAsync(cancellationToken);
        var bankPaidByInvoice = bankPaid
            .GroupBy(match => (match.AppUserId, match.Year, match.Month))
            .ToDictionary(group => group.Key, group => group.Sum(match => match.Amount));

        var kwhByUserMonth = new Dictionary<(int UserId, int Year, int Month), decimal>();
        foreach (var session in sessions)
        {
            var (year, month) = BrusselsTime.BrusselsYearMonthFromUnixMilliseconds(session.ToTimestamp);
            var kwh = ChargingSessionEnergy.TryDeltaKwhFromReadings(session.StartReading, session.StopReading) ?? 0m;
            foreach (var (userId, cards) in cardsByUser)
            {
                if (!cards.Contains(session.ChargeCardId))
                    continue;
                var key = (userId, year, month);
                kwhByUserMonth[key] = kwhByUserMonth.GetValueOrDefault(key) + kwh;
            }
        }

        var correctionTotals = new Dictionary<(int UserId, int Year, int Month), decimal>();
        foreach (var correction in corrections)
        {
            var key = (correction.UserId, correction.Date.Year, correction.Date.Month);
            correctionTotals[key] = correctionTotals.GetValueOrDefault(key) + correction.Amount;
        }

        var invoiceKeys = kwhByUserMonth.Keys.Concat(correctionTotals.Keys).Distinct().ToList();
        var candidates = new List<InvoiceCandidate>();
        foreach (var userGroup in invoiceKeys.GroupBy(key => key.UserId))
        {
            if (!names.TryGetValue(userGroup.Key, out var name))
                continue;

            var billedMonths = userGroup.Select(key => (key.Year, key.Month)).ToList();
            foreach (var key in userGroup)
            {
                var (userId, year, month) = key;
                var rate = RateFor(prices, year, month);
                var index = InvoiceNumbers.IndexForMonth(billedMonths, year, month);
                var invoiceNumber = InvoiceNumbers.UsesLegacyFormat(year, month)
                    ? InvoiceNumbers.Legacy(year, month, userId)
                    : InvoiceNumbers.Format(year, month, userId, index);
                var energyAmount = Math.Round(kwhByUserMonth.GetValueOrDefault(key) * rate, 2);
                var totalAmount = energyAmount + correctionTotals.GetValueOrDefault(key);
                var bankAmount = bankPaidByInvoice.GetValueOrDefault((userId, year, month));
                candidates.Add(new InvoiceCandidate(
                    userId,
                    name,
                    year,
                    month,
                    invoiceNumber,
                    totalAmount,
                    paid.Contains((userId, year, month)) || (bankAmount > 0 && bankAmount >= totalAmount)));
            }
        }

        return candidates;
    }

    private static decimal RateFor(IReadOnlyList<EnergyPrice> prices, int year, int month) =>
        prices
            .Where(price => price.Year < year || (price.Year == year && price.Month <= month))
            .Select(price => (decimal?)price.PricePerKwh)
            .FirstOrDefault() ?? 0.22m;
}
