using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Backend.Data;
using Backend.Models;
using Backend.Services;

namespace Backend.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ChargingStationsController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly IJwtService _jwtService;
    private readonly ILogger<ChargingStationsController> _logger;

    public ChargingStationsController(
        ApplicationDbContext context,
        IJwtService jwtService,
        ILogger<ChargingStationsController> logger)
    {
        _context = context;
        _jwtService = jwtService;
        _logger = logger;
    }

    /// <summary>
    /// Lists distinct charging points seen in session data (admin only).
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        try
        {
            var token = Request.Headers.Authorization.ToString().Replace("Bearer ", "");
            var userIdString = _jwtService.ValidateToken(token);

            if (userIdString == null || !int.TryParse(userIdString, out int currentUserId))
            {
                _logger.LogWarning("Unauthorized attempt to list charging stations");
                return Unauthorized();
            }

            var requestingUser = await _context.Users.FindAsync(currentUserId);
            if (requestingUser == null || !requestingUser.Role.IsAppAdministrator())
                return Forbid();

            var withSerialAndConnector = await _context.ChargingSessions
                .AsNoTracking()
                .Where(s => s.StationSerialNumber != null && s.Connector != null)
                .GroupBy(s => new { Serial = s.StationSerialNumber!, Conn = s.Connector! })
                .Select(g => new ChargingStationListItemDto
                {
                    StationSerialNumber = g.Key.Serial,
                    Connector = g.Key.Conn,
                    StationId = g.Max(s => s.StationId),
                    ChargingStationName = g.OrderByDescending(s => s.Start).Select(s => s.ChargingStationName).FirstOrDefault(),
                    SessionCount = g.Count(),
                    LastSessionStart = g.Max(s => s.Start),
                    IsLegacy = false
                })
                .ToListAsync();

            var legacyOnly = await _context.ChargingSessions
                .AsNoTracking()
                .Where(s => s.StationSerialNumber == null || s.Connector == null)
                .GroupBy(s => s.StationId)
                .Select(g => new ChargingStationListItemDto
                {
                    StationSerialNumber = null,
                    Connector = null,
                    StationId = g.Key,
                    ChargingStationName = null,
                    SessionCount = g.Count(),
                    LastSessionStart = g.Max(s => s.Start),
                    IsLegacy = true
                })
                .ToListAsync();

            var stations = withSerialAndConnector
                .Concat(legacyOnly)
                .OrderByDescending(s => s.LastSessionStart)
                .ToList();

            _logger.LogInformation("Admin {UserId} listed {Count} charging station aggregates", currentUserId, stations.Count);

            return Ok(new { stations });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing charging stations");
            return BadRequest($"Failed to list charging stations: {ex.Message}");
        }
    }
}
