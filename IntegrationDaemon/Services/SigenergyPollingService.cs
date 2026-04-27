using System.Text.Json;
using Backend.Data;
using Backend.Models;
using Backend.Services.Sigenergy;
using Microsoft.Extensions.Options;

namespace IntegrationDaemon.Services;

public interface ISigenergyPollingService
{
    Task<SigenergyStatusSnapshot> PollEnergyFlowAsync(CancellationToken cancellationToken = default);
}

public class SigenergyPollingService : ISigenergyPollingService
{
    private readonly ISigenergyCloudClient _client;
    private readonly SigenergyOptions _options;
    private readonly ApplicationDbContext _db;
    private readonly IStatusPublisher _publisher;
    private readonly ILogger<SigenergyPollingService> _logger;

    public SigenergyPollingService(
        ISigenergyCloudClient client,
        IOptions<SigenergyOptions> options,
        ApplicationDbContext db,
        IStatusPublisher publisher,
        ILogger<SigenergyPollingService> logger)
    {
        _client = client;
        _options = options.Value;
        _db = db;
        _publisher = publisher;
        _logger = logger;
    }

    public async Task<SigenergyStatusSnapshot> PollEnergyFlowAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        _logger.LogInformation("Polling Sigenergy energy flow at {NowUtc}", now);
        if (!_options.IsConfigured)
        {
            var unconfigured = new SigenergyStatusSnapshot
            {
                FetchedAtUtc = now,
                IsSuccess = false,
                Configured = false,
                Error = "Sigenergy is not configured."
            };
            _db.SigenergyStatusSnapshots.Add(unconfigured);
            await _db.SaveChangesAsync(cancellationToken);
            return unconfigured;
        }

        try
        {
            var d = await _client.GetEnergyFlowAsync(cancellationToken);
            double? remaining = null;
            if (_options.UsableBatteryCapacityKwh.HasValue && _options.UsableBatteryCapacityKwh.Value > 0)
                remaining = Math.Round((d.BatterySoc / 100.0) * _options.UsableBatteryCapacityKwh.Value, 2);

            var payload = JsonSerializer.Serialize(new
            {
                Configured = true,
                BatterySocPercent = d.BatterySoc,
                BatteryPowerKw = d.BatteryPower,
                SolarPowerKw = d.PvPower,
                RemainingBatteryKwh = remaining
            });
            var snapshot = new SigenergyStatusSnapshot
            {
                FetchedAtUtc = now,
                IsSuccess = true,
                Configured = true,
                BatterySocPercent = d.BatterySoc,
                BatteryPowerKw = d.BatteryPower,
                SolarPowerKw = d.PvPower,
                RemainingBatteryKwh = remaining,
                PayloadJson = payload
            };
            _db.SigenergyStatusSnapshots.Add(snapshot);
            await _db.SaveChangesAsync(cancellationToken);
            await _publisher.PublishSigenergyAsync(payload, cancellationToken);
            _logger.LogInformation(
                "Stored Sigenergy snapshot. BatterySoc={BatterySoc}, SolarKw={SolarKw}",
                snapshot.BatterySocPercent,
                snapshot.SolarPowerKw);
            return snapshot;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Sigenergy daemon poll failed");
            var failed = new SigenergyStatusSnapshot
            {
                FetchedAtUtc = now,
                IsSuccess = false,
                Configured = true,
                Error = ex.Message
            };
            _db.SigenergyStatusSnapshots.Add(failed);
            await _db.SaveChangesAsync(cancellationToken);
            return failed;
        }
    }
}
