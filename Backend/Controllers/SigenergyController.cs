using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Backend.Data;
using System.Text.Json;

namespace Backend.Controllers;

[ApiController]
[Route("api/sigenergy")]
public class SigenergyController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly ILogger<SigenergyController> _logger;

    public SigenergyController(
        ApplicationDbContext db,
        ILogger<SigenergyController> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>Current solar/battery snapshot from Sigen Cloud (fetched on page load; not polled by the server).</summary>
    [HttpGet("live")]
    [Authorize]
    public async Task<IActionResult> GetLive(CancellationToken cancellationToken)
    {
        try
        {
            var latest = await _db.SigenergyStatusSnapshots
                .AsNoTracking()
                .Where(x => x.IsSuccess)
                .OrderByDescending(x => x.FetchedAtUtc)
                .FirstOrDefaultAsync(cancellationToken);
            if (latest != null && !string.IsNullOrWhiteSpace(latest.PayloadJson))
            {
                var dto = JsonSerializer.Deserialize<SigenergyLiveResponse>(latest.PayloadJson);
                if (dto != null)
                    return Ok(dto);
            }
            return Ok(new SigenergyLiveResponse
            {
                Configured = false,
                Error = "No Sigenergy snapshot available yet. Wait for integration daemon polling."
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Sigenergy live snapshot failed");
            return Ok(new SigenergyLiveResponse
            {
                Configured = false,
                Error = "Could not read stored Sigenergy snapshot."
            });
        }
    }
}

public class SigenergyLiveResponse
{
    public bool Configured { get; set; }
    public double? BatterySocPercent { get; set; }
    public double? BatteryPowerKw { get; set; }
    public double? SolarPowerKw { get; set; }
    public double? RemainingBatteryKwh { get; set; }
    public string? Error { get; set; }
}
