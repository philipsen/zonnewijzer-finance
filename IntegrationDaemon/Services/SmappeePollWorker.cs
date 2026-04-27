using Microsoft.Extensions.Options;

namespace IntegrationDaemon.Services;

public class SmappeePollWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptionsMonitor<SmappeeDaemonOptions> _options;
    private readonly ILogger<SmappeePollWorker> _logger;

    public SmappeePollWorker(
        IServiceScopeFactory scopeFactory,
        IOptionsMonitor<SmappeeDaemonOptions> options,
        ILogger<SmappeePollWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Smappee poll worker started");
        while (!stoppingToken.IsCancellationRequested)
        {
            var o = _options.CurrentValue;
            if (!o.Enabled)
            {
                _logger.LogDebug("Smappee poll worker disabled; sleeping 30s");
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
                continue;
            }

            try
            {
                _logger.LogInformation("Smappee poll cycle started");
                using var scope = _scopeFactory.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<ISmappeePollingService>();
                await service.PollSessionsAsync(stoppingToken);
                _logger.LogInformation("Smappee poll cycle completed");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Smappee daemon cycle failed");
            }

            await Task.Delay(TimeSpan.FromMinutes(Math.Max(1, o.PollIntervalMinutes)), stoppingToken);
        }
        _logger.LogInformation("Smappee poll worker stopped");
    }
}
