using Backend;
using Backend.Data;
using Backend.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Backend.Services.Smappee;

public interface ISmappeeChargingSyncService
{
    /// <summary>
    /// Fetches whitelist + sessions from Smappee for Brussels calendar days <paramref name="from"/>..<paramref name="to"/> (inclusive),
    /// upserts charge cards and sessions, writes an <see cref="EventLog"/> entry.
    /// </summary>
    Task<SmappeeSyncServiceResult> SyncSmappeeCalendarAsync(
        DateOnly from,
        DateOnly to,
        string eventLogUserName,
        CancellationToken cancellationToken);
}

public class SmappeeChargingSyncService : ISmappeeChargingSyncService
{
    private readonly ApplicationDbContext _context;
    private readonly ISmappeeCloudClient _smappee;
    private readonly SmappeeOptions _options;
    private readonly ILogger<SmappeeChargingSyncService> _logger;

    public SmappeeChargingSyncService(
        ApplicationDbContext context,
        ISmappeeCloudClient smappee,
        IOptions<SmappeeOptions> options,
        ILogger<SmappeeChargingSyncService> logger)
    {
        _context = context;
        _smappee = smappee;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<SmappeeSyncServiceResult> SyncSmappeeCalendarAsync(
        DateOnly from,
        DateOnly to,
        string eventLogUserName,
        CancellationToken cancellationToken)
    {
        if (_options.ServiceLocationId <= 0)
            return SmappeeSyncServiceResult.Fail("Smappee ServiceLocationId is not configured.");

        if (to < from)
            return SmappeeSyncServiceResult.Fail("'to' date must be on or after 'from' date.");

        var rangeStartMs = BrusselsTime.StartOfCalendarDayUnixMilliseconds(from);
        var rangeEndMs = BrusselsTime.StartOfCalendarDayUnixMilliseconds(to.AddDays(1));
        var triggerKind = eventLogUserName.Contains("manual", StringComparison.OrdinalIgnoreCase) ? "manual" : "scheduled";
        _logger.LogInformation(
            "Smappee sync trigger context: trigger={Trigger}, actor={Actor}, serviceLocationId={ServiceLocationId}, from={From}, to={To}, cancellationRequested={CancellationRequested}",
            triggerKind,
            eventLogUserName,
            _options.ServiceLocationId,
            from,
            to,
            cancellationToken.IsCancellationRequested);

        _logger.LogInformation(
            "Smappee sync calendar From={From} To={To} (request); API range query startMs={StartMs} ({StartDesc}) endMs={EndMs} ({EndDesc}) (end is exclusive: start of Brussels day after To)",
            from,
            to,
            rangeStartMs,
            BrusselsTime.DescribeUnixMilliseconds(rangeStartMs),
            rangeEndMs,
            BrusselsTime.DescribeUnixMilliseconds(rangeEndMs));

        var rfids = await _smappee.GetWhitelistedRfidsAsync(_options.ServiceLocationId, cancellationToken);
        var (cardsAdded, cardsUpdated) = await UpsertChargeCardsFromWhitelistAsync(rfids, cancellationToken);

        var sessionsDto = await _smappee.GetChargingSessionsAsync(
            _options.ServiceLocationId,
            rangeStartMs,
            rangeEndMs,
            active: null,
            cancellationToken);

        _logger.LogInformation("Smappee sync: received {Count} session DTOs in range", sessionsDto.Count);
        if (sessionsDto.Count > 0)
        {
            var s0 = sessionsDto[0];
            var s0FromMs = SmappeeSessionTimestamps.ToStoredMilliseconds(s0.From);
            var s0ToMs = SmappeeSessionTimestamps.ToStoredMilliseconds(s0.To);
            _logger.LogInformation(
                "Smappee sync: first session id={Id} FromRaw={FromRaw} {FromStored} ToRaw={ToRaw} {ToStored} Duration={Duration}s Status={Status} " +
                "FromLooksLikeSecondsNotMs={FromSecHint} ToLooksLikeSecondsNotMs={ToSecHint}",
                s0.Id,
                s0.From,
                BrusselsTime.DescribeUnixMilliseconds(s0FromMs),
                s0.To,
                s0ToMs.HasValue ? BrusselsTime.DescribeUnixMilliseconds(s0ToMs.Value) : "null (ongoing)",
                s0.Duration,
                s0.Status,
                BrusselsTime.LooksLikeUnixSecondsInsteadOfMilliseconds(s0.From),
                s0.To.HasValue && BrusselsTime.LooksLikeUnixSecondsInsteadOfMilliseconds(s0.To.Value));
        }

        var (sessAdded, sessUpdated, sessSkipped, sessErrors) =
            await UpsertSessionsFromSmappeeAsync(sessionsDto, cancellationToken);

        _context.EventLogs.Add(new EventLog
        {
            Timestamp = DateTime.UtcNow,
            UserName = eventLogUserName,
            Action =
                $"Smappee sync: charge cards +{cardsAdded}/~{cardsUpdated}, sessions +{sessAdded}/~{sessUpdated}, skipped {sessSkipped}, errors {sessErrors}"
        });
        await _context.SaveChangesAsync(cancellationToken);

        return SmappeeSyncServiceResult.Ok(cardsAdded, cardsUpdated, sessAdded, sessUpdated, sessSkipped, sessErrors);
    }

    private async Task<(int Added, int Updated)> UpsertChargeCardsFromWhitelistAsync(
        IReadOnlyList<SmappeeRfidDto> rfids,
        CancellationToken cancellationToken)
    {
        var added = 0;
        var updated = 0;
        foreach (var r in rfids)
        {
            var norm = ChargeCardImportIds.NormalizeTokenKey(r.Value);
            if (string.IsNullOrEmpty(norm))
                continue;

            var byWhitelist = await _context.ChargeCards
                .FirstOrDefaultAsync(c => c.SmappeeWhitelistId == r.Id, cancellationToken);
            var byValue = await _context.ChargeCards
                .FirstOrDefaultAsync(c => c.Value == norm, cancellationToken);

            ChargeCard row;
            if (byWhitelist != null)
                row = byWhitelist;
            else if (byValue != null)
                row = byValue;
            else
            {
                row = new ChargeCard
                {
                    SmappeeWhitelistId = r.Id,
                    Value = norm,
                    Comment = string.IsNullOrWhiteSpace(r.Comment) ? null : r.Comment.Trim(),
                    ExpirationTimestamp = r.ExpirationTimestamp
                };
                _context.ChargeCards.Add(row);
                added++;
                continue;
            }

            var changed = false;
            if (row.SmappeeWhitelistId != r.Id)
            {
                row.SmappeeWhitelistId = r.Id;
                changed = true;
            }

            if (row.Value != norm)
            {
                row.Value = norm;
                changed = true;
            }

            if (!string.IsNullOrWhiteSpace(r.Comment) && row.Comment != r.Comment.Trim())
            {
                row.Comment = r.Comment.Trim();
                changed = true;
            }

            if (row.ExpirationTimestamp != r.ExpirationTimestamp)
            {
                row.ExpirationTimestamp = r.ExpirationTimestamp;
                changed = true;
            }

            if (changed)
                updated++;
        }

        await _context.SaveChangesAsync(cancellationToken);
        return (added, updated);
    }

    private async Task<(int Added, int Updated, int Skipped, int Errors)> UpsertSessionsFromSmappeeAsync(
        IReadOnlyList<SmappeeSessionDto> list,
        CancellationToken cancellationToken)
    {
        var defaultConn = _options.DefaultConnector ?? "1";
        var added = 0;
        var updated = 0;
        var skipped = 0;
        var errors = 0;

        foreach (var dto in list)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(dto.Rfid))
                {
                    var fromMs = SmappeeSessionTimestamps.ToStoredMilliseconds(dto.From);
                    var toMs = SmappeeSessionTimestamps.ToStoredMilliseconds(dto.To);
                    _logger.LogWarning(
                        "Smappee session id={SessionId}: error (missing RFID); FromRaw={FromRaw} {FromDesc} ToRaw={ToRaw} {ToDesc} " +
                        "FromLooksLikeSecondsNotMs={FromSecHint} Serial={Serial} Status={Status}",
                        dto.Id,
                        dto.From,
                        BrusselsTime.DescribeUnixMilliseconds(fromMs),
                        dto.To,
                        toMs.HasValue ? BrusselsTime.DescribeUnixMilliseconds(toMs.Value) : "null (ongoing)",
                        BrusselsTime.LooksLikeUnixSecondsInsteadOfMilliseconds(dto.From),
                        dto.SerialNumber,
                        dto.Status);
                    errors++;
                    continue;
                }

                var norm = ChargeCardImportIds.NormalizeTokenKey(dto.Rfid);
                var card = await _context.ChargeCards.FirstOrDefaultAsync(c => c.Value == norm, cancellationToken);
                if (card == null)
                {
                    card = new ChargeCard
                    {
                        Value = norm,
                        Comment = null,
                        SmappeeWhitelistId = null,
                        ExpirationTimestamp = null
                    };
                    _context.ChargeCards.Add(card);
                    await _context.SaveChangesAsync(cancellationToken);
                }

                var serial = dto.SerialNumberNormalized();
                if (string.IsNullOrEmpty(serial))
                {
                    var fromMs = SmappeeSessionTimestamps.ToStoredMilliseconds(dto.From);
                    var toMs = SmappeeSessionTimestamps.ToStoredMilliseconds(dto.To);
                    _logger.LogWarning(
                        "Smappee session id={SessionId}: error (missing serial); FromRaw={FromRaw} {FromDesc} ToRaw={ToRaw} {ToDesc} " +
                        "Rfid={Rfid} RawSerial={RawSerial} FromLooksLikeSecondsNotMs={FromSecHint}",
                        dto.Id,
                        dto.From,
                        BrusselsTime.DescribeUnixMilliseconds(fromMs),
                        dto.To,
                        toMs.HasValue ? BrusselsTime.DescribeUnixMilliseconds(toMs.Value) : "null (ongoing)",
                        dto.Rfid,
                        dto.SerialNumber,
                        BrusselsTime.LooksLikeUnixSecondsInsteadOfMilliseconds(dto.From));
                    errors++;
                    continue;
                }

                var connectorForPoint = dto.Connector is >= 0
                    ? dto.Connector.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    : defaultConn;
                var stationId = ChargingPointIds.ChargingPointIdFromSerialAndConnector(serial, connectorForPoint);
                var startRead = dto.StartReading;
                var stopRead = dto.StopReading;
                var hasEnergy = dto.Energy is > 0m;
                if (!startRead.HasValue && !hasEnergy)
                {
                    var fromMs = SmappeeSessionTimestamps.ToStoredMilliseconds(dto.From);
                    var toMs = SmappeeSessionTimestamps.ToStoredMilliseconds(dto.To);
                    _logger.LogWarning(
                        "Smappee session id={SessionId}: error (missing start reading); FromRaw={FromRaw} {FromDesc} ToRaw={ToRaw} {ToDesc} " +
                        "StopReading={StopReading} Rfid={Rfid} Serial={Serial}",
                        dto.Id,
                        dto.From,
                        BrusselsTime.DescribeUnixMilliseconds(fromMs),
                        dto.To,
                        toMs.HasValue ? BrusselsTime.DescribeUnixMilliseconds(toMs.Value) : "null (ongoing)",
                        stopRead,
                        dto.Rfid,
                        serial);
                    errors++;
                    continue;
                }

                if (dto.To.HasValue && !stopRead.HasValue && !hasEnergy)
                {
                    var fromMs = SmappeeSessionTimestamps.ToStoredMilliseconds(dto.From);
                    var toMs = SmappeeSessionTimestamps.ToStoredMilliseconds(dto.To);
                    _logger.LogWarning(
                        "Smappee session id={SessionId}: error (ended session missing stop reading); FromRaw={FromRaw} {FromDesc} ToRaw={ToRaw} {ToDesc} " +
                        "StartReading={StartReading} Rfid={Rfid} Serial={Serial}",
                        dto.Id,
                        dto.From,
                        BrusselsTime.DescribeUnixMilliseconds(fromMs),
                        dto.To,
                        toMs.HasValue ? BrusselsTime.DescribeUnixMilliseconds(toMs.Value) : "null",
                        startRead,
                        dto.Rfid,
                        serial);
                    errors++;
                    continue;
                }

                var existing = await _context.ChargingSessions
                    .FirstOrDefaultAsync(s => s.SmappeeSessionId == dto.Id, cancellationToken);

                if (existing != null)
                {
                    var wasOpen = !existing.ToTimestamp.HasValue;
                    var closingNow = wasOpen && dto.To.HasValue;
                    MapDtoToSession(existing, dto, card.Id, stationId, serial, defaultConn);
                    updated++;

                    if (closingNow)
                    {
                        _logger.LogInformation(
                            "Smappee session closed: sessionId={SessionId}, dbRowId={DbRowId}, from={FromTimestamp}, to={ToTimestamp}, stationId={StationId}, chargeCardId={ChargeCardId}",
                            existing.SmappeeSessionId,
                            existing.Id,
                            existing.FromTimestamp,
                            existing.ToTimestamp,
                            existing.StationId,
                            existing.ChargeCardId);
                    }
                }
                else
                {
                    var session = new ChargingSession();
                    MapDtoToSession(session, dto, card.Id, stationId, serial, defaultConn);
                    session.SmappeeSessionId = dto.Id;
                    _context.ChargingSessions.Add(session);
                    added++;

                    _logger.LogInformation(
                        "Smappee session created: sessionId={SessionId}, from={FromTimestamp}, to={ToTimestamp}, stationId={StationId}, chargeCardId={ChargeCardId}",
                        session.SmappeeSessionId,
                        session.FromTimestamp,
                        session.ToTimestamp,
                        session.StationId,
                        session.ChargeCardId);
                }
            }
            catch (Exception ex)
            {
                var fromMs = SmappeeSessionTimestamps.ToStoredMilliseconds(dto.From);
                var toMs = SmappeeSessionTimestamps.ToStoredMilliseconds(dto.To);
                _logger.LogWarning(
                    ex,
                    "Smappee session id={SessionId}: exception while mapping; FromRaw={FromRaw} {FromDesc} ToRaw={ToRaw} {ToDesc} " +
                    "Rfid={Rfid} Serial={Serial} FromLooksLikeSecondsNotMs={FromSecHint}",
                    dto.Id,
                    dto.From,
                    BrusselsTime.DescribeUnixMilliseconds(fromMs),
                    dto.To,
                    toMs.HasValue ? BrusselsTime.DescribeUnixMilliseconds(toMs.Value) : "null (ongoing)",
                    dto.Rfid,
                    dto.SerialNumber,
                    BrusselsTime.LooksLikeUnixSecondsInsteadOfMilliseconds(dto.From));
                errors++;
            }
        }

        await _context.SaveChangesAsync(cancellationToken);
        return (added, updated, skipped, errors);
    }

    private static void MapDtoToSession(
        ChargingSession session,
        SmappeeSessionDto dto,
        int chargeCardId,
        int stationId,
        string serial,
        string _)
    {
        session.SmappeeSessionId = dto.Id;
        session.SerialNumber = serial;
        session.AuthenticationType = dto.AuthenticationType;
        session.Rfid = ChargeCardImportIds.NormalizeTokenKey(dto.Rfid ?? "");
        session.CsmsTransactionId = string.IsNullOrWhiteSpace(dto.CsmsTransactionId) ? null : dto.CsmsTransactionId;
        session.FromTimestamp = SmappeeSessionTimestamps.ToStoredMilliseconds(dto.From);
        session.ToTimestamp = SmappeeSessionTimestamps.ToStoredMilliseconds(dto.To);
        session.Status = dto.Status;
        session.Range = dto.Range;
        session.Duration = dto.Duration;
        session.Priority = dto.Priority;
        ChargingSessionEnergy.SetReadingsFromSmappeeDto(session, dto.StartReading, dto.StopReading, dto.Energy);
        session.ChargeCardId = chargeCardId;
        session.StationId = stationId;
    }
}
