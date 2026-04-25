using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Backend.Services.Sigenergy;

namespace Backend.Controllers;

[ApiController]
[Route("api/sigenergy")]
public class SigenergyController : ControllerBase
{
    private readonly ISigenergyCloudClient _client;
    private readonly SigenergyOptions _options;
    private readonly ILogger<SigenergyController> _logger;

    public SigenergyController(
        ISigenergyCloudClient client,
        IOptions<SigenergyOptions> options,
        ILogger<SigenergyController> logger)
    {
        _client = client;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>Current solar/battery snapshot from Sigen Cloud (fetched on page load; not polled by the server).</summary>
    [HttpGet("live")]
    [Authorize]
    public async Task<IActionResult> GetLive(CancellationToken cancellationToken)
    {
        if (!_options.IsConfigured)
        {
            return Ok(new SigenergyLiveResponse { Configured = false });
        }

        try
        {
            var d = await _client.GetEnergyFlowAsync(cancellationToken);
            double? remainingBatteryKwh = null;
            if (_options.UsableBatteryCapacityKwh.HasValue && _options.UsableBatteryCapacityKwh.Value > 0)
            {
                remainingBatteryKwh = Math.Round((d.BatterySoc / 100.0) * _options.UsableBatteryCapacityKwh.Value, 2);
            }

            return Ok(new SigenergyLiveResponse
            {
                Configured = true,
                BatterySocPercent = d.BatterySoc,
                BatteryPowerKw = d.BatteryPower,
                SolarPowerKw = d.PvPower,
                RemainingBatteryKwh = remainingBatteryKwh
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Sigenergy live snapshot failed");
            return Ok(new SigenergyLiveResponse
            {
                Configured = true,
                Error = "Could not read Sigenergy (Sigen Cloud). Check server logs and Sigenergy settings."
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
