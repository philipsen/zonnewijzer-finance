using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Backend;
using Backend.Data;
using Backend.Services;

namespace Backend.Controllers;

[ApiController]
[Route("api/my-charging-sessions")]
public class MyChargingSessionsController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly IJwtService _jwtService;
    private readonly ILogger<MyChargingSessionsController> _logger;

    public MyChargingSessionsController(ApplicationDbContext context, IJwtService jwtService, ILogger<MyChargingSessionsController> logger)
    {
        _context = context;
        _jwtService = jwtService;
        _logger = logger;
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

            var currentUser = await _context.Users.FindAsync(currentUserId);
            if (currentUser?.ChargeCardId == null)
                return Forbid();

            var timestamps = await _context.ChargingSessions
                .Where(s => s.ChargeCardId == currentUser.ChargeCardId)
                .Select(s => s.FromTimestamp)
                .ToListAsync();

            var availableMonths = timestamps
                .Select(BrusselsTime.BrusselsYearMonthFromUnixMilliseconds)
                .Distinct()
                .OrderByDescending(x => x.Year)
                .ThenByDescending(x => x.Month)
                .ToList();

            var monthsData = availableMonths.Select(m => new
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
            _logger.LogError(ex, "Error retrieving available months for current user");
            return BadRequest($"Failed to retrieve available months: {ex.Message}");
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetMySessions(int page = 1, int pageSize = 50, int? year = null, int? month = null)
    {
        try
        {
            var token = Request.Headers.Authorization.ToString().Replace("Bearer ", "");
            var userIdString = _jwtService.ValidateToken(token);

            if (userIdString == null || !int.TryParse(userIdString, out int currentUserId))
                return Unauthorized();

            var currentUser = await _context.Users.FindAsync(currentUserId);
            if (currentUser?.ChargeCardId == null)
                return Forbid();

            var query = _context.ChargingSessions
                .Where(s => s.ChargeCardId == currentUser.ChargeCardId)
                .AsQueryable();

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

            var chargeCardId = currentUser.ChargeCardId.Value;
            var profile = await _context.ChargeCards.AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == chargeCardId);
            var linkedUser = await _context.Users.AsNoTracking()
                .FirstOrDefaultAsync(u => u.ChargeCardId == chargeCardId);
            var chargeCardDisplayName =
                !string.IsNullOrWhiteSpace(profile?.Comment) ? profile!.Comment!.Trim()
                : (!string.IsNullOrWhiteSpace(linkedUser?.Name) ? linkedUser!.Name : null);

            var sessionData = sessions.Select(s => new
            {
                id = s.Id,
                smappeeSessionId = s.SmappeeSessionId,
                start = ChargingSessionEnergy.StartUtc(s.FromTimestamp),
                end = ChargingSessionEnergy.EndUtc(s.ToTimestamp),
                chargeCardId = s.ChargeCardId,
                chargeCardDisplayName,
                serialNumber = s.SerialNumber,
                rfid = s.Rfid,
                stationId = s.StationId,
                chargedKwh = ChargingSessionEnergy.ChargedKwh(s),
                durationMinutes = ChargingSessionEnergy.EndUtc(s.ToTimestamp) is { } end
                    ? (int?)(int)(end - ChargingSessionEnergy.StartUtc(s.FromTimestamp)).TotalMinutes
                    : null,
                createdAt = s.CreatedAt
            }).ToList();

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
            _logger.LogError(ex, "Error retrieving charging sessions for current user");
            return BadRequest($"Failed to retrieve sessions: {ex.Message}");
        }
    }
}
