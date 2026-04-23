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

    public async Task<IReadOnlyList<int>> GetChargeCardIdsForMonthAsync(int year, int month, int? chargeCardIdFilter, CancellationToken cancellationToken = default)
    {
        var (startMs, endMs) = BrusselsTime.MonthRangeUnixMilliseconds(year, month);
        var q = _context.ChargingSessions
            .Where(cs => cs.FromTimestamp >= startMs && cs.FromTimestamp < endMs);
        if (chargeCardIdFilter.HasValue)
            q = q.Where(cs => cs.ChargeCardId == chargeCardIdFilter.Value);

        return await q.Select(cs => cs.ChargeCardId).Distinct().OrderBy(id => id).ToListAsync(cancellationToken);
    }

    public async Task<InvoiceData?> BuildInvoiceDataReadOnlyAsync(int chargeCardId, int year, int month, CancellationToken cancellationToken = default)
    {
        var (startMs, endMs) = BrusselsTime.MonthRangeUnixMilliseconds(year, month);
        var chargingSessions = await _context.ChargingSessions
            .Where(cs => cs.ChargeCardId == chargeCardId && cs.FromTimestamp >= startMs && cs.FromTimestamp < endMs)
            .ToListAsync(cancellationToken);

        if (chargingSessions.Count == 0)
            return null;

        var totalKwh = chargingSessions.Sum(ChargingSessionEnergy.ChargedKwh);
        var ratePerKwh = await _context.EnergyPrices
            .Where(p => p.Year < year || (p.Year == year && p.Month <= month))
            .OrderByDescending(p => p.Year).ThenByDescending(p => p.Month)
            .Select(p => (decimal?)p.PricePerKwh)
            .FirstOrDefaultAsync(cancellationToken) ?? 0.22m;

        var totalAmount = totalKwh * ratePerKwh;

        var linkedUser = await _context.Users
            .FirstOrDefaultAsync(u => u.ChargeCardId == chargeCardId, cancellationToken);
        var profile = await _context.ChargeCards
            .FirstOrDefaultAsync(p => p.Id == chargeCardId, cancellationToken);
        var userName = linkedUser?.Name ?? profile?.Comment ?? $"Charge card {chargeCardId}";

        var molliePayment = await _context.MolliePayments
            .FirstOrDefaultAsync(p => p.ChargeCardId == chargeCardId && p.Year == year && p.Month == month, cancellationToken);

        return new InvoiceData
        {
            UserId = chargeCardId,
            UserName = userName,
            Street = profile?.Street,
            HouseNumber = profile?.HouseNumber,
            PostalCode = profile?.PostalCode,
            PlaceName = profile?.PlaceName,
            Year = year,
            Month = month,
            MonthName = new DateTime(year, month, 1).ToString("MMMM yyyy"),
            InvoiceNumber = $"INV-{year:D4}-{month:D2}-{chargeCardId:D4}",
            TotalKwh = Math.Round(totalKwh, 2),
            RatePerKwh = ratePerKwh,
            TotalAmount = Math.Round(totalAmount, 2),
            PaymentUrl = molliePayment?.CheckoutUrl,
            IsPaid = molliePayment?.Status == "paid",
            Sessions = chargingSessions.Select(cs => new SessionData
            {
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
