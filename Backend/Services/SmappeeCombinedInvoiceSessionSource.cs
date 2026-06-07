using Backend.Data;
using Backend.Models;
using Backend.Services.Smappee;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Backend.Services;

public sealed class SmappeeCombinedInvoiceSessionSource : ISmappeeCombinedInvoiceSessionSource
{
    private readonly ISmappeeCloudClient _smappee;
    private readonly ApplicationDbContext _context;
    private readonly SmappeeOptions _options;
    private readonly ILogger<SmappeeCombinedInvoiceSessionSource> _logger;

    private CombinedInvoiceSmappeeFetchResult? _cache;
    private (int StartYear, int StartMonth, int EndYear, int EndMonth)? _cacheRange;

    public SmappeeCombinedInvoiceSessionSource(
        ISmappeeCloudClient smappee,
        ApplicationDbContext context,
        IOptions<SmappeeOptions> options,
        ILogger<SmappeeCombinedInvoiceSessionSource> logger)
    {
        _smappee = smappee;
        _context = context;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<CombinedInvoiceSmappeeFetchResult> FetchSessionsForRangeAsync(
        int startYear,
        int startMonth,
        int endYear,
        int endMonth,
        CancellationToken cancellationToken = default)
    {
        var rangeKey = (startYear, startMonth, endYear, endMonth);
        if (_cache != null && _cacheRange == rangeKey)
            return _cache;

        if (_options.ServiceLocationId <= 0)
            throw new InvalidOperationException("Smappee ServiceLocationId is not configured.");

        if (!_options.HasAllCredentials)
            throw new InvalidOperationException("Smappee API credentials are not configured.");

        var rangeStartMs = BrusselsTime.StartOfCalendarDayUnixMilliseconds(new DateOnly(startYear, startMonth, 1));
        var rangeEndMs = BrusselsTime.StartOfCalendarDayUnixMilliseconds(
            new DateOnly(endYear, endMonth, 1).AddMonths(1));

        _logger.LogInformation(
            "Fetching combined invoice sessions from Smappee for {StartYear}-{StartMonth} to {EndYear}-{EndMonth} (ms {StartMs}..{EndMs})",
            startYear, startMonth, endYear, endMonth, rangeStartMs, rangeEndMs);

        var (_, completed) = await _smappee.GetChargingSessionsActiveAndCompletedAsync(
            _options.ServiceLocationId,
            rangeStartMs,
            rangeEndMs,
            cancellationToken);

        // Only completed sessions; active/ongoing and sessions ending on/after range end belong on a later invoice.
        var smappeeSessions = completed
            .GroupBy(s => s.Id)
            .Select(g => g.First())
            .ToList();

        var cards = await _context.ChargeCards.AsNoTracking().ToListAsync(cancellationToken);
        var cardByToken = cards
            .GroupBy(c => ChargeCardImportIds.NormalizeTokenKey(c.Value), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var userLinks = await _context.UserChargeCards.AsNoTracking().ToListAsync(cancellationToken);
        var userIdByCardId = userLinks
            .GroupBy(x => x.ChargeCardId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.UserId).Distinct().ToList());

        var defaultConn = _options.DefaultConnector ?? "1";
        var mapped = new List<CombinedInvoiceSourceSession>();
        var skippedUnmapped = 0;
        var skippedInvalid = 0;
        var skippedEndedOnOrAfterRangeEnd = 0;

        foreach (var dto in smappeeSessions)
        {
            var fromMs = SmappeeSessionTimestamps.ToStoredMilliseconds(dto.From);
            var toMs = SmappeeSessionTimestamps.ToStoredMilliseconds(dto.To);

            if (!CombinedInvoiceSessionRules.StartedInRange(fromMs, rangeStartMs, rangeEndMs))
                continue;

            if (!CombinedInvoiceSessionRules.EndedBeforeRangeEnd(toMs, rangeEndMs))
            {
                skippedEndedOnOrAfterRangeEnd++;
                continue;
            }

            if (string.IsNullOrWhiteSpace(dto.Rfid))
            {
                skippedInvalid++;
                continue;
            }

            var serial = dto.SerialNumberNormalized();
            if (string.IsNullOrEmpty(serial))
            {
                skippedInvalid++;
                continue;
            }

            var temp = new ChargingSession();
            ChargingSessionEnergy.SetReadingsFromSmappeeDto(temp, dto.StartReading, dto.StopReading, dto.Energy);
            var kwh = ChargingSessionEnergy.ChargedKwh(temp);
            if (kwh <= 0m)
            {
                skippedInvalid++;
                continue;
            }

            var token = ChargeCardImportIds.NormalizeTokenKey(dto.Rfid);
            if (!cardByToken.TryGetValue(token, out var card))
            {
                skippedUnmapped++;
                continue;
            }

            if (!userIdByCardId.TryGetValue(card.Id, out var appUserIds) || appUserIds.Count == 0)
            {
                skippedUnmapped++;
                continue;
            }

            var connectorForPoint = dto.Connector is >= 0
                ? dto.Connector.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)
                : defaultConn;
            var stationId = ChargingPointIds.ChargingPointIdFromSerialAndConnector(serial, connectorForPoint);

            mapped.Add(new CombinedInvoiceSourceSession
            {
                AppUserId = appUserIds[0],
                ChargeCardId = card.Id,
                FromTimestampMs = fromMs,
                ToTimestampMs = toMs,
                StationId = stationId,
                SerialNumber = serial,
                Connector = connectorForPoint,
                ChargedKwh = Math.Round(kwh, 2)
            });
        }

        if (skippedEndedOnOrAfterRangeEnd > 0)
        {
            _logger.LogInformation(
                "Combined invoice: excluded {Count} session(s) that ended on or after range end {EndMs}",
                skippedEndedOnOrAfterRangeEnd,
                rangeEndMs);
        }

        var byUser = mapped
            .GroupBy(s => s.AppUserId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<CombinedInvoiceSourceSession>)g.OrderBy(s => s.FromTimestampMs).ToList());

        var result = new CombinedInvoiceSmappeeFetchResult
        {
            Sessions = mapped.OrderBy(s => s.FromTimestampMs).ToList(),
            ByAppUserId = byUser,
            SkippedUnmapped = skippedUnmapped,
            SkippedInvalid = skippedInvalid
        };

        _cache = result;
        _cacheRange = rangeKey;
        return result;
    }
}
