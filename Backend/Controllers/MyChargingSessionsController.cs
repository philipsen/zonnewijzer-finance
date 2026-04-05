using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Backend.Data;
using Backend.Services;

namespace Backend.Controllers
{
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
                if (currentUser?.ChargingUserId == null)
                    return Forbid();

                var availableMonths = await _context.ChargingSessions
                    .Where(s => s.UserId == currentUser.ChargingUserId)
                    .Select(s => new { Year = s.Start.Year, Month = s.Start.Month })
                    .Distinct()
                    .OrderByDescending(x => x.Year)
                    .ThenByDescending(x => x.Month)
                    .ToListAsync();

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
                if (currentUser?.ChargingUserId == null)
                    return Forbid();

                var query = _context.ChargingSessions
                    .Where(s => s.UserId == currentUser.ChargingUserId)
                    .AsQueryable();

                if (year.HasValue && month.HasValue)
                {
                    if (month.Value < 1 || month.Value > 12)
                        return BadRequest("Month must be between 1 and 12");

                    var startDate = new DateTime(year.Value, month.Value, 1);
                    var endDate = startDate.AddMonths(1);
                    query = query.Where(s => s.Start >= startDate && s.Start < endDate);
                }

                var totalSessions = await query.CountAsync();

                var sessions = await query
                    .OrderByDescending(s => s.Start)
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .ToListAsync();

                var sessionData = sessions.Select(s => new
                {
                    id = s.Id,
                    start = s.Start,
                    end = s.End,
                    userId = s.UserId,
                    stationId = s.StationId,
                    chargedKwh = s.ChargedKwh,
                    duration = (int)(s.End - s.Start).TotalMinutes,
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
}
