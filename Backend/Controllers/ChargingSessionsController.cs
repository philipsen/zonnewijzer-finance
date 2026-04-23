using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Backend;
using Backend.Data;
using Backend.Models;
using Backend.Services;
using Backend.Services.Smappee;

namespace Backend.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ChargingSessionsController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly IJwtService _jwtService;
    private readonly ILogger<ChargingSessionsController> _logger;

    public ChargingSessionsController(
        ApplicationDbContext context,
        IJwtService jwtService,
        ILogger<ChargingSessionsController> logger)
    {
        _context = context;
        _jwtService = jwtService;
        _logger = logger;
    }

    [Authorize(Roles = "Admin,Owner")]
    [HttpPost]
    public async Task<IActionResult> AddSession([FromBody] AddSessionRequest request)
    {
        try
        {
            var endMs = request.End.HasValue
                ? new DateTimeOffset(request.End.Value.ToUniversalTime()).ToUnixTimeMilliseconds()
                : (long?)null;
            var startMs = new DateTimeOffset(request.Start.ToUniversalTime()).ToUnixTimeMilliseconds();
            if (endMs.HasValue && endMs.Value < startMs)
                return BadRequest("End time cannot be before start time");

            if (request.ChargedKwh < 0)
                return BadRequest("ChargedKwh must be non-negative");

            if (!TryResolveStationForWrite(request, out var stationId, out var stationSerial, out _, out var stationError))
                return BadRequest(stationError);

            var existing = await _context.ChargingSessions.FirstOrDefaultAsync(s =>
                s.SmappeeSessionId == null
                && s.FromTimestamp == startMs
                && s.ChargeCardId == request.ChargeCardId
                && s.StationId == stationId);

            ChargingSession session;
            if (existing != null)
            {
                session = existing;
                session.ToTimestamp = endMs;
                session.SerialNumber = stationSerial;
                session.ChargeCardId = request.ChargeCardId;
                var cardExisting = await _context.ChargeCards.AsNoTracking().FirstOrDefaultAsync(c => c.Id == request.ChargeCardId);
                session.Rfid = cardExisting?.Value;
                session.StartReading = 0;
                session.StopReading = request.ChargedKwh;
                _context.EventLogs.Add(new EventLog
                {
                    Timestamp = DateTime.UtcNow,
                    UserName = User.Identity?.Name ?? "Unknown",
                    Action = $"Updated charging session (same start/charge card/station): id {session.Id}, {request.ChargedKwh} kWh"
                });
            }
            else
            {
                var card = await _context.ChargeCards.AsNoTracking().FirstOrDefaultAsync(c => c.Id == request.ChargeCardId);
                session = new ChargingSession
                {
                    FromTimestamp = startMs,
                    ToTimestamp = endMs,
                    ChargeCardId = request.ChargeCardId,
                    StationId = stationId,
                    SerialNumber = stationSerial,
                    Rfid = card?.Value,
                    AuthenticationType = "MANUAL",
                    StartReading = 0,
                    StopReading = request.ChargedKwh,
                    Status = "STOPPED"
                };
                _context.ChargingSessions.Add(session);
                _context.EventLogs.Add(new EventLog
                {
                    Timestamp = DateTime.UtcNow,
                    UserName = User.Identity?.Name ?? "Unknown",
                    Action = $"Manually added charging session: Charge card {session.ChargeCardId}, Station {session.StationId}, {request.ChargedKwh} kWh"
                });
            }

            await _context.SaveChangesAsync();
            await HydrateSessionRfidAsync(session);
            return Ok(ToSessionResponse(session));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding charging session manually");
            return BadRequest($"Failed to add session: {ex.Message}");
        }
    }

    [Authorize(Roles = "Admin,Owner")]
    [HttpPost("sync-smappee")]
    public async Task<IActionResult> SyncSmappee(
        [FromBody] SyncSmappeeRequest body,
        [FromServices] ISmappeeChargingSyncService smappeeSync,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await smappeeSync.SyncSmappeeCalendarAsync(
                body.From,
                body.To,
                User.Identity?.Name ?? "Unknown",
                cancellationToken);

            if (!result.Success)
                return BadRequest(result.ErrorMessage);

            return Ok(new
            {
                message = "Smappee sync completed",
                chargeCards = new { added = result.CardsAdded, updated = result.CardsUpdated },
                sessions = new
                {
                    added = result.SessionsAdded,
                    updated = result.SessionsUpdated,
                    skipped = result.SessionsSkipped,
                    errors = result.SessionsErrors,
                    total = result.SessionsAdded + result.SessionsUpdated + result.SessionsSkipped + result.SessionsErrors
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Smappee sync failed");
            return BadRequest($"Smappee sync failed: {ex.Message}");
        }
    }

    [HttpGet("available-months")]
    public async Task<IActionResult> GetAvailableMonths()
    {
        try
        {
            var token = Request.Headers.Authorization.ToString().Replace("Bearer ", "");
            var userIdString = _jwtService.ValidateToken(token);

            if (userIdString == null || !int.TryParse(userIdString, out int currentUserId))
                return Unauthorized();

            var timestamps = await _context.ChargingSessions
                .Select(s => s.FromTimestamp)
                .ToListAsync();

            var months = timestamps
                .Select(BrusselsTime.BrusselsYearMonthFromUnixMilliseconds)
                .Distinct()
                .OrderByDescending(x => x.Year)
                .ThenByDescending(x => x.Month)
                .ToList();

            var monthsData = months.Select(m => new
            {
                value = $"{m.Year}-{m.Month:D2}",
                label = new DateTime(m.Year, m.Month, 1).ToString("MMMM yyyy"),
                year = m.Year,
                month = m.Month
            }).ToList();

            return Ok(monthsData);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving available months");
            return BadRequest($"Failed to retrieve available months: {ex.Message}");
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetAllSessions(int page = 1, int pageSize = 50, int? year = null, int? month = null)
    {
        try
        {
            var token = Request.Headers.Authorization.ToString().Replace("Bearer ", "");
            var userIdString = _jwtService.ValidateToken(token);

            if (userIdString == null || !int.TryParse(userIdString, out int currentUserId))
                return Unauthorized();

            var query = _context.ChargingSessions.AsQueryable();

            if (year.HasValue && month.HasValue)
            {
                if (month.Value < 1 || month.Value > 12)
                    return BadRequest("Month must be between 1 and 12");

                var (startMs, endMs) = BrusselsTime.MonthRangeUnixMilliseconds(year.Value, month.Value);
                query = query.Where(s => s.FromTimestamp >= startMs && s.FromTimestamp < endMs);
            }

            var totalSessions = await query.CountAsync();

            var sessions = await query
                .OrderByDescending(s => s.FromTimestamp)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            var cardIds = sessions.Select(s => s.ChargeCardId).Distinct().ToList();
            var cards = await _context.ChargeCards
                .AsNoTracking()
                .Where(c => cardIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, c => c.Comment);
            var linkedByCardId = await _context.Users
                .AsNoTracking()
                .Where(u => u.ChargeCardId != null && cardIds.Contains(u.ChargeCardId.Value))
                .ToDictionaryAsync(u => u.ChargeCardId!.Value, u => u.Name);

            string? ChargeCardLabel(int cid)
            {
                if (cards.TryGetValue(cid, out var comment) && !string.IsNullOrWhiteSpace(comment))
                    return comment.Trim();
                if (linkedByCardId.TryGetValue(cid, out var appName) && !string.IsNullOrWhiteSpace(appName))
                    return appName;
                return null;
            }

            var sessionData = sessions.Select(s => ToSessionResponse(s, ChargeCardLabel(s.ChargeCardId))).ToList();

            return Ok(new
            {
                sessions = sessionData,
                pagination = new
                {
                    page,
                    pageSize,
                    totalSessions,
                    totalPages = (int)Math.Ceiling((double)totalSessions / pageSize)
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving charging sessions");
            return BadRequest($"Failed to retrieve sessions: {ex.Message}");
        }
    }

    [Authorize(Roles = "Admin,Owner")]
    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateSession(int id, [FromBody] UpdateSessionRequest request)
    {
        try
        {
            var endMs = request.End.HasValue
                ? new DateTimeOffset(request.End.Value.ToUniversalTime()).ToUnixTimeMilliseconds()
                : (long?)null;
            var startMs = new DateTimeOffset(request.Start.ToUniversalTime()).ToUnixTimeMilliseconds();
            if (endMs.HasValue && endMs.Value < startMs)
                return BadRequest("End time cannot be before start time");

            if (request.ChargedKwh < 0)
                return BadRequest("ChargedKwh must be non-negative");

            var session = await _context.ChargingSessions.FindAsync(id);
            if (session == null)
                return NotFound($"Session {id} not found");

            if (!TryResolveStationForWrite(request, out var stationId, out var stationSerial, out _, out var stationError))
                return BadRequest(stationError);

            session.FromTimestamp = startMs;
            session.ToTimestamp = endMs;
            session.ChargeCardId = request.ChargeCardId;
            session.StationId = stationId;
            session.SerialNumber = stationSerial;
            session.StartReading = 0;
            session.StopReading = request.ChargedKwh;

            _context.EventLogs.Add(new EventLog
            {
                Timestamp = DateTime.UtcNow,
                UserName = User.Identity?.Name ?? "Unknown",
                Action = $"Updated charging session {id}: Charge card {session.ChargeCardId}, Station {session.StationId}, {request.ChargedKwh} kWh"
            });

            await _context.SaveChangesAsync();
            await HydrateSessionRfidAsync(session);
            return Ok(ToSessionResponse(session));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating charging session {SessionId}", id);
            return BadRequest($"Failed to update session: {ex.Message}");
        }
    }

    [Authorize(Roles = "Admin,Owner")]
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteSession(int id)
    {
        try
        {
            var session = await _context.ChargingSessions.FindAsync(id);
            if (session == null)
                return NotFound($"Session {id} not found");

            _context.ChargingSessions.Remove(session);

            _context.EventLogs.Add(new EventLog
            {
                Timestamp = DateTime.UtcNow,
                UserName = User.Identity?.Name ?? "Unknown",
                Action = $"Deleted charging session {id}: Charge card {session.ChargeCardId}, Station {session.StationId}"
            });

            await _context.SaveChangesAsync();

            return Ok(new { message = $"Session {id} deleted" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting charging session {SessionId}", id);
            return BadRequest($"Failed to delete session: {ex.Message}");
        }
    }

    private async Task HydrateSessionRfidAsync(ChargingSession session)
    {
        if (!string.IsNullOrEmpty(session.Rfid))
            return;
        var card = await _context.ChargeCards.AsNoTracking().FirstOrDefaultAsync(c => c.Id == session.ChargeCardId);
        if (card != null)
        {
            session.Rfid = card.Value;
            await _context.SaveChangesAsync();
        }
    }

    private static object ToSessionResponse(ChargingSession s, string? chargeCardDisplayName = null)
    {
        var start = ChargingSessionEnergy.StartUtc(s.FromTimestamp);
        var end = ChargingSessionEnergy.EndUtc(s.ToTimestamp);
        var kwh = ChargingSessionEnergy.ChargedKwh(s);
        return new
        {
            id = s.Id,
            smappeeSessionId = s.SmappeeSessionId,
            start,
            end,
            chargeCardId = s.ChargeCardId,
            chargeCardDisplayName,
            serialNumber = s.SerialNumber,
            authenticationType = s.AuthenticationType,
            rfid = s.Rfid,
            csmsTransactionId = s.CsmsTransactionId,
            fromTimestamp = s.FromTimestamp,
            toTimestamp = s.ToTimestamp,
            status = s.Status,
            range = s.Range,
            duration = s.Duration,
            priority = s.Priority,
            startReading = s.StartReading,
            stopReading = s.StopReading,
            stationId = s.StationId,
            chargedKwh = kwh,
            durationMinutes = end.HasValue ? (int?)(int)(end.Value - start).TotalMinutes : null,
            createdAt = s.CreatedAt
        };
    }

    private static bool TryResolveStationForWrite(
        AddSessionRequest request,
        out int resolvedStationId,
        out string? stationSerial,
        out string? connector,
        out string? error)
        => TryResolveStationForWrite(
            request.StationSerialNumber,
            request.Connector,
            request.StationId,
            out resolvedStationId,
            out stationSerial,
            out connector,
            out error);

    private static bool TryResolveStationForWrite(
        UpdateSessionRequest request,
        out int resolvedStationId,
        out string? stationSerial,
        out string? connector,
        out string? error)
        => TryResolveStationForWrite(
            request.StationSerialNumber,
            request.Connector,
            request.StationId,
            out resolvedStationId,
            out stationSerial,
            out connector,
            out error);

    private static bool TryResolveStationForWrite(
        string? stationSerialNumber,
        string? connectorRaw,
        int legacyStationId,
        out int resolvedStationId,
        out string? stationSerial,
        out string? connector,
        out string? error)
    {
        error = null;
        stationSerial = null;
        connector = null;

        var hasSerial = !string.IsNullOrWhiteSpace(stationSerialNumber);
        var hasConnector = !string.IsNullOrWhiteSpace(connectorRaw);
        if (hasSerial && hasConnector)
        {
            stationSerial = stationSerialNumber!.Trim().Trim('"');
            connector = connectorRaw!.Trim().Trim('"');
            resolvedStationId = ChargingPointIds.ChargingPointIdFromSerialAndConnector(stationSerial, connector);
            return true;
        }

        if (legacyStationId >= 1)
        {
            resolvedStationId = legacyStationId;
            stationSerial = hasSerial ? stationSerialNumber!.Trim().Trim('"') : null;
            connector = hasConnector ? connectorRaw!.Trim().Trim('"') : null;
            return true;
        }

        error = "Provide station serial number and connector together, or a positive station id (legacy).";
        resolvedStationId = default;
        return false;
    }
}

public class SyncSmappeeRequest
{
    public DateOnly From { get; set; }
    public DateOnly To { get; set; }
}

public class AddSessionRequest
{
    public DateTime Start { get; set; }
    public DateTime? End { get; set; }
    public int ChargeCardId { get; set; }
    public int StationId { get; set; }
    public decimal ChargedKwh { get; set; }
    public string? StationSerialNumber { get; set; }
    public string? Connector { get; set; }
}

public class UpdateSessionRequest
{
    public DateTime Start { get; set; }
    public DateTime? End { get; set; }
    public int ChargeCardId { get; set; }
    public int StationId { get; set; }
    public decimal ChargedKwh { get; set; }
    public string? StationSerialNumber { get; set; }
    public string? Connector { get; set; }
}
