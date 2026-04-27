using Microsoft.Extensions.Options;

namespace IntegrationDaemon.Services;

public class SigenergyPollWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptionsMonitor<SigenergyDaemonOptions> _options;
    private readonly ILogger<SigenergyPollWorker> _logger;

    public SigenergyPollWorker(
        IServiceScopeFactory scopeFactory,
        IOptionsMonitor<SigenergyDaemonOptions> options,
        ILogger<SigenergyPollWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Sigenergy poll worker started");
        while (!stoppingToken.IsCancellationRequested)
        {
            var o = _options.CurrentValue;
            if (!o.Enabled)
            {
                _logger.LogDebug("Sigenergy poll worker disabled; sleeping 30s");
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
                continue;
            }

            try
            {
                _logger.LogInformation("Sigenergy poll cycle started");
                using var scope = _scopeFactory.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<ISigenergyPollingService>();
                await service.PollEnergyFlowAsync(stoppingToken);
                _logger.LogInformation("Sigenergy poll cycle completed");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Sigenergy daemon cycle failed");
            }

            await Task.Delay(TimeSpan.FromMinutes(Math.Max(1, o.PollIntervalMinutes)), stoppingToken);
        }
        _logger.LogInformation("Sigenergy poll worker stopped");
    }
}
