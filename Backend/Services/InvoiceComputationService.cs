using Backend.Data;
using Microsoft.EntityFrameworkCore;

namespace Backend.Services;

public class InvoiceComputationService : IInvoiceComputationService
{
    private readonly ApplicationDbContext _context;

    public InvoiceComputationService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<int>> GetAppUserIdsForMonthAsync(
        int year,
        int month,
        int? appUserIdFilter,
        CancellationToken cancellationToken = default)
    {
        var (startMs, endMs) = BrusselsTime.MonthRangeUnixMilliseconds(year, month);
        var cardIdsInMonth = await _context.ChargingSessions
            .Where(cs => cs.FromTimestamp >= startMs && cs.FromTimestamp < endMs)
            .Select(cs => cs.ChargeCardId)
            .Distinct()
            .ToListAsync(cancellationToken);

        if (cardIdsInMonth.Count == 0)
            return Array.Empty<int>();

        var q = _context.UserChargeCards
            .AsNoTracking()
            .Where(ucc => cardIdsInMonth.Contains(ucc.ChargeCardId))
            .Select(ucc => ucc.UserId)
            .Distinct();

        if (appUserIdFilter.HasValue)
            q = q.Where(id => id == appUserIdFilter.Value);

        return await q.OrderBy(id => id).ToListAsync(cancellationToken);
    }

    public async Task<InvoiceData?> BuildInvoiceDataForAppUserAsync(
        int appUserId,
        int year,
        int month,
        CancellationToken cancellationToken = default)
    {
        var myCards = await UserChargeCardQuery.GetChargeCardIdsForUserAsync(_context, appUserId, cancellationToken);
        if (myCards.Count == 0)
            return null;

        var (startMs, endMs) = BrusselsTime.MonthRangeUnixMilliseconds(year, month);
        var chargingSessions = await _context.ChargingSessions
            .Where(cs => myCards.Contains(cs.ChargeCardId) && cs.FromTimestamp >= startMs && cs.FromTimestamp < endMs)
            .OrderBy(cs => cs.FromTimestamp)
            .ToListAsync(cancellationToken);

        if (chargingSessions.Count == 0)
            return null;

        var appUser = await _context.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == appUserId, cancellationToken);
        if (appUser == null)
            return null;

        var cardProfiles = await _context.ChargeCards
            .Where(c => myCards.Contains(c.Id))
            .OrderBy(c => c.Id)
            .ToListAsync(cancellationToken);
        var profile = cardProfiles.FirstOrDefault(c =>
                          !string.IsNullOrWhiteSpace(c.Street) || !string.IsNullOrWhiteSpace(c.PostalCode))
                      ?? cardProfiles.FirstOrDefault();

        var totalKwh = chargingSessions.Sum(ChargingSessionEnergy.ChargedKwh);
        var ratePerKwh = await _context.EnergyPrices
            .Where(p => p.Year < year || (p.Year == year && p.Month <= month))
            .OrderByDescending(p => p.Year).ThenByDescending(p => p.Month)
            .Select(p => (decimal?)p.PricePerKwh)
            .FirstOrDefaultAsync(cancellationToken) ?? 0.22m;

        var totalAmount = totalKwh * ratePerKwh;

        var molliePayment = await _context.MolliePayments
            .AsNoTracking()
            .FirstOrDefaultAsync(
                p => p.AppUserId == appUserId && p.Year == year && p.Month == month,
                cancellationToken);

        return new InvoiceData
        {
            UserId = appUserId,
            UserName = appUser.Name,
            Street = profile?.Street,
            HouseNumber = profile?.HouseNumber,
            PostalCode = profile?.PostalCode,
            PlaceName = profile?.PlaceName,
            Year = year,
            Month = month,
            MonthName = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Unspecified)
                .ToString("MMMM yyyy"),
            InvoiceNumber = $"INV-{year:D4}-{month:D2}-U{appUserId}",
            TotalKwh = Math.Round(totalKwh, 2),
            RatePerKwh = ratePerKwh,
            TotalAmount = Math.Round(totalAmount, 2),
            PaymentUrl = molliePayment?.CheckoutUrl,
            IsPaid = molliePayment?.Status == "paid",
            Sessions = chargingSessions.Select(cs => new SessionData
            {
                ChargeCardId = cs.ChargeCardId,
                Start = ChargingSessionEnergy.StartUtc(cs.FromTimestamp),
                End = ChargingSessionEnergy.EndUtc(cs.ToTimestamp),
                StationId = cs.StationId,
                StationSerialNumber = cs.SerialNumber,
                Connector = null,
                ChargingStationName = null,
                ChargedKwh = Math.Round(ChargingSessionEnergy.ChargedKwh(cs), 2),
                Amount = Math.Round(ChargingSessionEnergy.ChargedKwh(cs) * ratePerKwh, 2)
            }).ToList(),
            GeneratedAt = DateTime.UtcNow
        };
    }
}
