using Backend.Data;
using Microsoft.EntityFrameworkCore;
using System.Globalization;

namespace Backend.Services;

public class InvoiceComputationService : IInvoiceComputationService
{
    private const decimal DefaultRatePerKwh = 0.22m;
    private const decimal DefaultMollieSurchargeEur = 0.32m;

    private readonly ApplicationDbContext _context;
    private readonly ISmappeeCombinedInvoiceSessionSource _smappeeSessions;

    public InvoiceComputationService(
        ApplicationDbContext context,
        ISmappeeCombinedInvoiceSessionSource smappeeSessions)
    {
        _context = context;
        _smappeeSessions = smappeeSessions;
    }

    public static (long StartMs, long EndMs) RangeUnixMilliseconds(int startYear, int startMonth, int endYear, int endMonth)
    {
        var (startMs, _) = BrusselsTime.MonthRangeUnixMilliseconds(startYear, startMonth);
        var (_, endMs) = BrusselsTime.MonthRangeUnixMilliseconds(endYear, endMonth);
        return (startMs, endMs);
    }

    public static string CombinedInvoiceNumber(int appUserId, int endYear, int endMonth, int index) =>
        InvoiceNumbers.Format(endYear, endMonth, appUserId, index);

    private static readonly CultureInfo DutchCulture = CultureInfo.GetCultureInfo("nl-NL");

    public static string CombinedPeriodLabel(int startYear, int startMonth, int endYear, int endMonth)
    {
        static string MonthName(int month) =>
            DutchCulture.TextInfo.ToTitleCase(DutchCulture.DateTimeFormat.GetMonthName(month));

        var startLabel = MonthName(startMonth);
        var endLabel = $"{MonthName(endMonth)} {endYear}";
        if (startYear == endYear && startMonth == endMonth)
            return endLabel;
        if (startYear == endYear)
            return $"{startLabel} – {endLabel}";
        return $"{startLabel} {startYear} – {endLabel}";
    }

    public async Task<IReadOnlyList<int>> GetAppUserIdsForMonthAsync(
        int year,
        int month,
        int? appUserIdFilter,
        CancellationToken cancellationToken = default)
    {
        var (startMs, endMs) = BrusselsTime.MonthRangeUnixMilliseconds(year, month);
        var fromSessions = await GetAppUserIdsInWindowAsync(startMs, endMs, appUserIdFilter, cancellationToken);
        var fromCorrections = await GetAppUserIdsWithCorrectionsAsync(year, month, appUserIdFilter, cancellationToken);
        return fromSessions.Union(fromCorrections).OrderBy(id => id).ToList();
    }

    private async Task<IReadOnlyList<int>> GetAppUserIdsWithCorrectionsAsync(
        int year,
        int month,
        int? appUserIdFilter,
        CancellationToken cancellationToken)
    {
        var start = new DateOnly(year, month, 1);
        var end = start.AddMonths(1);
        var query = _context.InvoiceCorrections
            .AsNoTracking()
            .Where(c => c.Date >= start && c.Date < end);
        if (appUserIdFilter.HasValue)
            query = query.Where(c => c.UserId == appUserIdFilter.Value);
        return await query.Select(c => c.UserId).Distinct().ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<int>> GetAppUserIdsForRangeAsync(
        int startYear,
        int startMonth,
        int endYear,
        int endMonth,
        int? appUserIdFilter,
        CancellationToken cancellationToken = default)
    {
        var fetch = await _smappeeSessions.FetchSessionsForRangeAsync(
            startYear, startMonth, endYear, endMonth, cancellationToken);
        var ids = fetch.ByAppUserId.Keys.OrderBy(id => id).ToList();
        if (appUserIdFilter.HasValue)
            ids = ids.Where(id => id == appUserIdFilter.Value).ToList();
        return ids;
    }

    public async Task<CombinedInvoiceSmappeeFetchResult> FetchCombinedSessionsFromSmappeeAsync(
        int startYear,
        int startMonth,
        int endYear,
        int endMonth,
        CancellationToken cancellationToken = default) =>
        await _smappeeSessions.FetchSessionsForRangeAsync(
            startYear, startMonth, endYear, endMonth, cancellationToken);

    private async Task<IReadOnlyList<int>> GetAppUserIdsInWindowAsync(
        long startMs,
        long endMs,
        int? appUserIdFilter,
        CancellationToken cancellationToken)
    {
        var cardIdsInWindow = await _context.ChargingSessions
            .Where(cs => cs.ToTimestamp.HasValue
                && cs.ToTimestamp >= startMs
                && cs.ToTimestamp < endMs)
            .Select(cs => cs.ChargeCardId)
            .Distinct()
            .ToListAsync(cancellationToken);

        if (cardIdsInWindow.Count == 0)
            return Array.Empty<int>();

        var q = _context.UserChargeCards
            .AsNoTracking()
            .Where(ucc => cardIdsInWindow.Contains(ucc.ChargeCardId))
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
        var corrections = await LoadCorrectionsForMonthAsync(appUserId, year, month, cancellationToken);

        var chargingSessions = new List<Models.ChargingSession>();
        if (myCards.Count > 0)
        {
            var (startMs, endMs) = BrusselsTime.MonthRangeUnixMilliseconds(year, month);
            chargingSessions = await _context.ChargingSessions
                .Where(cs => myCards.Contains(cs.ChargeCardId)
                    && cs.ToTimestamp.HasValue
                    && cs.ToTimestamp >= startMs
                    && cs.ToTimestamp < endMs)
                .OrderBy(cs => cs.FromTimestamp)
                .ToListAsync(cancellationToken);
        }

        if (chargingSessions.Count == 0 && corrections.Count == 0)
            return null;

        var appUser = await _context.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == appUserId, cancellationToken);
        if (appUser == null)
            return null;

        var profile = myCards.Count == 0
            ? null
            : await ResolveChargeCardProfileAsync(myCards, cancellationToken);
        var energyPrices = await LoadEnergyPricesAsync(cancellationToken);
        var ratePerKwh = GetRateForMonth(energyPrices, year, month);

        var totalKwh = chargingSessions.Sum(ChargingSessionEnergy.ChargedKwh);
        var energyAmount = Math.Round(totalKwh * ratePerKwh, 2);
        var correctionLines = corrections.Select(c => new InvoiceCorrectionLine
        {
            Date = c.Date,
            Description = c.Description,
            Amount = c.Amount
        }).ToList();
        var roundedTotal = energyAmount + correctionLines.Sum(c => c.Amount);

        var molliePayment = await _context.MolliePayments
            .AsNoTracking()
            .FirstOrDefaultAsync(
                p => p.AppUserId == appUserId && p.Year == year && p.Month == month,
                cancellationToken);
        var invoiceIndex = await InvoiceIndexAsync(appUserId, myCards, year, month, cancellationToken);
        var bankPaidAmount = await _context.InvoiceBankMatches
            .AsNoTracking()
            .Where(match => match.AppUserId == appUserId && match.Year == year && match.Month == month)
            .Select(match => (decimal?)match.Amount)
            .SumAsync(cancellationToken) ?? 0m;

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
            InvoiceNumber = InvoiceNumbers.Format(year, month, appUserId, invoiceIndex),
            TotalKwh = Math.Round(totalKwh, 2),
            RatePerKwh = ratePerKwh,
            EnergyAmount = energyAmount,
            TotalAmount = roundedTotal,
            Corrections = correctionLines,
            MollieSurchargeEur = GetMollieSurchargeForMonth(energyPrices, year, month),
            PaymentUrl = molliePayment?.CheckoutUrl,
            IsPaid = molliePayment?.Status == "paid" || (bankPaidAmount > 0 && bankPaidAmount >= roundedTotal),
            Sessions = chargingSessions.Select(cs => MapDbSession(cs, ratePerKwh)).ToList(),
            GeneratedAt = DateTime.UtcNow
        };
    }

    public async Task<InvoiceData?> BuildCombinedInvoiceDataForAppUserAsync(
        int appUserId,
        int startYear,
        int startMonth,
        int endYear,
        int endMonth,
        CancellationToken cancellationToken = default)
    {
        var fetch = await _smappeeSessions.FetchSessionsForRangeAsync(
            startYear, startMonth, endYear, endMonth, cancellationToken);
        if (!fetch.ByAppUserId.TryGetValue(appUserId, out var userSessions) || userSessions.Count == 0)
            return null;

        return await BuildCombinedInvoiceDataFromSessionsAsync(
            appUserId, userSessions, startYear, startMonth, endYear, endMonth, cancellationToken);
    }

    public async Task<InvoiceData?> BuildCombinedInvoiceDataFromSessionsAsync(
        int appUserId,
        IReadOnlyList<CombinedInvoiceSourceSession> userSessions,
        int startYear,
        int startMonth,
        int endYear,
        int endMonth,
        CancellationToken cancellationToken = default)
    {
        if (userSessions.Count == 0)
            return null;

        var appUser = await _context.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == appUserId, cancellationToken);
        if (appUser == null)
            return null;

        var myCards = await UserChargeCardQuery.GetChargeCardIdsForUserAsync(_context, appUserId, cancellationToken);
        if (myCards.Count == 0)
            return null;

        var profile = await ResolveChargeCardProfileAsync(myCards, cancellationToken);
        var energyPrices = await LoadEnergyPricesAsync(cancellationToken);

        var sessions = userSessions.Select(s =>
        {
            var (sessionYear, sessionMonth) = BrusselsTime.BrusselsYearMonthFromUnixMilliseconds(s.FromTimestampMs);
            var rate = GetRateForMonth(energyPrices, sessionYear, sessionMonth);
            return MapSmappeeSession(s, rate);
        }).ToList();

        var totalKwh = sessions.Sum(s => s.ChargedKwh);
        var totalAmount = sessions.Sum(s => s.Amount);
        var weightedRate = totalKwh > 0 ? totalAmount / totalKwh : DefaultRatePerKwh;
        var invoiceIndex = await InvoiceIndexAsync(appUserId, myCards, endYear, endMonth, cancellationToken);
        var roundedTotal = Math.Round(totalAmount, 2);

        return new InvoiceData
        {
            UserId = appUserId,
            UserName = appUser.Name,
            Street = profile?.Street,
            HouseNumber = profile?.HouseNumber,
            PostalCode = profile?.PostalCode,
            PlaceName = profile?.PlaceName,
            Year = endYear,
            Month = endMonth,
            MonthName = CombinedPeriodLabel(startYear, startMonth, endYear, endMonth),
            InvoiceNumber = CombinedInvoiceNumber(appUserId, endYear, endMonth, invoiceIndex),
            TotalKwh = Math.Round(totalKwh, 2),
            RatePerKwh = Math.Round(weightedRate, 4),
            EnergyAmount = roundedTotal,
            TotalAmount = roundedTotal,
            PaymentUrl = null,
            IsPaid = false,
            UsePaymentRequestNotice = true,
            Sessions = sessions,
            GeneratedAt = DateTime.UtcNow
        };
    }

    private async Task<List<Models.InvoiceCorrection>> LoadCorrectionsForMonthAsync(
        int appUserId,
        int year,
        int month,
        CancellationToken cancellationToken)
    {
        var start = new DateOnly(year, month, 1);
        var end = start.AddMonths(1);
        return await _context.InvoiceCorrections
            .AsNoTracking()
            .Where(c => c.UserId == appUserId && c.Date >= start && c.Date < end)
            .OrderBy(c => c.Date)
            .ThenBy(c => c.Id)
            .ToListAsync(cancellationToken);
    }

    private async Task<int> InvoiceIndexAsync(
        int appUserId,
        IReadOnlyList<int> cardIds,
        int year,
        int month,
        CancellationToken cancellationToken)
    {
        var billedMonths = new List<(int Year, int Month)>();
        if (cardIds.Count > 0)
        {
            var endTimestamps = await _context.ChargingSessions
                .AsNoTracking()
                .Where(cs => cardIds.Contains(cs.ChargeCardId) && cs.ToTimestamp.HasValue)
                .Select(cs => cs.ToTimestamp!.Value)
                .ToListAsync(cancellationToken);
            billedMonths.AddRange(endTimestamps.Select(BrusselsTime.BrusselsYearMonthFromUnixMilliseconds));
        }

        var correctionDates = await _context.InvoiceCorrections
            .AsNoTracking()
            .Where(c => c.UserId == appUserId)
            .Select(c => c.Date)
            .ToListAsync(cancellationToken);
        billedMonths.AddRange(correctionDates.Select(date => (date.Year, date.Month)));
        return InvoiceNumbers.IndexForMonth(billedMonths, year, month);
    }

    private async Task<List<Models.EnergyPrice>> LoadEnergyPricesAsync(CancellationToken cancellationToken) =>
        await _context.EnergyPrices
            .OrderByDescending(p => p.Year).ThenByDescending(p => p.Month)
            .ToListAsync(cancellationToken);

    private static decimal GetRateForMonth(IReadOnlyList<Models.EnergyPrice> energyPrices, int year, int month) =>
        energyPrices
            .Where(p => p.Year < year || (p.Year == year && p.Month <= month))
            .Select(p => (decimal?)p.PricePerKwh)
            .FirstOrDefault() ?? DefaultRatePerKwh;

    private static decimal GetMollieSurchargeForMonth(IReadOnlyList<Models.EnergyPrice> energyPrices, int year, int month) =>
        energyPrices
            .Where(p => p.Year < year || (p.Year == year && p.Month <= month))
            .Select(p => (decimal?)p.MollieSurchargeEur)
            .FirstOrDefault() ?? DefaultMollieSurchargeEur;

    private async Task<Models.ChargeCard?> ResolveChargeCardProfileAsync(
        IReadOnlyList<int> myCards,
        CancellationToken cancellationToken)
    {
        var cardProfiles = await _context.ChargeCards
            .Where(c => myCards.Contains(c.Id))
            .OrderBy(c => c.Id)
            .ToListAsync(cancellationToken);
        return cardProfiles.FirstOrDefault(c =>
                   !string.IsNullOrWhiteSpace(c.Street) || !string.IsNullOrWhiteSpace(c.PostalCode))
               ?? cardProfiles.FirstOrDefault();
    }

    private static SessionData MapDbSession(Models.ChargingSession cs, decimal ratePerKwh)
    {
        var kwh = ChargingSessionEnergy.ChargedKwh(cs);
        return new SessionData
        {
            ChargeCardId = cs.ChargeCardId,
            Start = ChargingSessionEnergy.StartUtc(cs.FromTimestamp),
            End = ChargingSessionEnergy.EndUtc(cs.ToTimestamp),
            StationId = cs.StationId,
            StationSerialNumber = cs.SerialNumber,
            Connector = null,
            ChargingStationName = null,
            ChargedKwh = Math.Round(kwh, 2),
            Amount = Math.Round(kwh * ratePerKwh, 2)
        };
    }

    private static SessionData MapSmappeeSession(CombinedInvoiceSourceSession s, decimal ratePerKwh) =>
        new()
        {
            ChargeCardId = s.ChargeCardId,
            Start = ChargingSessionEnergy.StartUtc(s.FromTimestampMs),
            End = ChargingSessionEnergy.EndUtc(s.ToTimestampMs),
            StationId = s.StationId,
            StationSerialNumber = s.SerialNumber,
            Connector = s.Connector,
            ChargingStationName = null,
            ChargedKwh = s.ChargedKwh,
            Amount = Math.Round(s.ChargedKwh * ratePerKwh, 2)
        };
}
