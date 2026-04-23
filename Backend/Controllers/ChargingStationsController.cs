using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Backend;
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

            var withSerial = (await _context.ChargingSessions
                .AsNoTracking()
                .Where(s => s.SerialNumber != null && s.SerialNumber != "")
                .GroupBy(s => s.SerialNumber!)
                .Select(g => new
                {
                    Serial = g.Key,
                    StationId = g.Max(s => s.StationId),
                    SessionCount = g.Count(),
                    MaxTs = g.Max(s => s.FromTimestamp)
                })
                .ToListAsync())
                .Select(x => new ChargingStationListItemDto
                {
                    StationSerialNumber = x.Serial,
                    Connector = null,
                    StationId = x.StationId,
                    ChargingStationName = null,
                    SessionCount = x.SessionCount,
                    LastSessionStart = ChargingSessionEnergy.StartUtc(x.MaxTs),
                    IsLegacy = false
                })
                .ToList();

            var legacyOnly = (await _context.ChargingSessions
                .AsNoTracking()
                .Where(s => s.SerialNumber == null || s.SerialNumber == "")
                .GroupBy(s => s.StationId)
                .Select(g => new
                {
                    StationId = g.Key,
                    SessionCount = g.Count(),
                    MaxTs = g.Max(s => s.FromTimestamp)
                })
                .ToListAsync())
                .Select(x => new ChargingStationListItemDto
                {
                    StationSerialNumber = null,
                    Connector = null,
                    StationId = x.StationId,
                    ChargingStationName = null,
                    SessionCount = x.SessionCount,
                    LastSessionStart = ChargingSessionEnergy.StartUtc(x.MaxTs),
                    IsLegacy = true
                })
                .ToList();

            var stations = withSerial
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
