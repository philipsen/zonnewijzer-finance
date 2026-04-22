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

    public async Task<IReadOnlyList<int>> GetChargingUserIdsForMonthAsync(int year, int month, int? chargingUserIdFilter, CancellationToken cancellationToken = default)
    {
        var q = _context.ChargingSessions
            .Where(cs => cs.Start.Year == year && cs.Start.Month == month);
        if (chargingUserIdFilter.HasValue)
            q = q.Where(cs => cs.UserId == chargingUserIdFilter.Value);

        return await q.Select(cs => cs.UserId).Distinct().OrderBy(id => id).ToListAsync(cancellationToken);
    }

    public async Task<InvoiceData?> BuildInvoiceDataReadOnlyAsync(int chargingUserId, int year, int month, CancellationToken cancellationToken = default)
    {
        var chargingSessions = await _context.ChargingSessions
            .Where(cs => cs.UserId == chargingUserId && cs.Start.Year == year && cs.Start.Month == month)
            .ToListAsync(cancellationToken);

        if (chargingSessions.Count == 0)
            return null;

        var totalKwh = chargingSessions.Sum(cs => cs.ChargedKwh);
        var ratePerKwh = await _context.EnergyPrices
            .Where(p => p.Year < year || (p.Year == year && p.Month <= month))
            .OrderByDescending(p => p.Year).ThenByDescending(p => p.Month)
            .Select(p => (decimal?)p.PricePerKwh)
            .FirstOrDefaultAsync(cancellationToken) ?? 0.22m;

        var totalAmount = totalKwh * ratePerKwh;

        var linkedUser = await _context.Users
            .FirstOrDefaultAsync(u => u.ChargingUserId == chargingUserId, cancellationToken);
        var profile = await _context.ChargingUserProfiles
            .FirstOrDefaultAsync(p => p.Id == chargingUserId, cancellationToken);
        var userName = linkedUser?.Name ?? profile?.DisplayName ?? $"Charging User {chargingUserId}";

        var molliePayment = await _context.MolliePayments
            .FirstOrDefaultAsync(p => p.ChargingUserId == chargingUserId && p.Year == year && p.Month == month, cancellationToken);

        return new InvoiceData
        {
            UserId = chargingUserId,
            UserName = userName,
            Street = profile?.Street,
            HouseNumber = profile?.HouseNumber,
            PostalCode = profile?.PostalCode,
            PlaceName = profile?.PlaceName,
            Year = year,
            Month = month,
            MonthName = new DateTime(year, month, 1).ToString("MMMM yyyy"),
            InvoiceNumber = $"INV-{year:D4}-{month:D2}-{chargingUserId:D4}",
            TotalKwh = Math.Round(totalKwh, 2),
            RatePerKwh = ratePerKwh,
            TotalAmount = Math.Round(totalAmount, 2),
            PaymentUrl = molliePayment?.CheckoutUrl,
            IsPaid = molliePayment?.Status == "paid",
            Sessions = chargingSessions.Select(cs => new SessionData
            {
                Start = cs.Start,
                End = cs.End,
                StationId = cs.StationId,
                StationSerialNumber = cs.StationSerialNumber,
                Connector = cs.Connector,
                ChargingStationName = cs.ChargingStationName,
                ChargedKwh = Math.Round(cs.ChargedKwh, 2),
                Amount = Math.Round(cs.ChargedKwh * ratePerKwh, 2)
            }).ToList(),
            GeneratedAt = DateTime.UtcNow
        };
    }
}
