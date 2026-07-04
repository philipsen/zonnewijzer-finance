using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Backend.Services.Smappee;

/// <summary>Periodically runs the same Smappee calendar sync as the admin UI (whitelist + sessions upsert).</summary>
public sealed class SmappeeHourlySyncBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SmappeeHourlySyncBackgroundService> _logger;

    public SmappeeHourlySyncBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<SmappeeHourlySyncBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var scope0 = _scopeFactory.CreateScope();
        var bg0 = scope0.ServiceProvider.GetRequiredService<IOptions<SmappeeBackgroundSyncOptions>>().Value;
        var initialDelay = TimeSpan.FromSeconds(Math.Clamp(bg0.InitialDelaySeconds, 0, 600));
        try
        {
            await Task.Delay(initialDelay, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            var delayUntilNext = TimeSpan.FromHours(1);
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var bg = scope.ServiceProvider.GetRequiredService<IOptions<SmappeeBackgroundSyncOptions>>().Value;
                var smappeeOpts = scope.ServiceProvider.GetRequiredService<IOptions<SmappeeOptions>>().Value;
                delayUntilNext = TimeSpan.FromHours(Math.Clamp(bg.IntervalHours, 1, 168));

                if (!bg.Enabled)
                {
                    _logger.LogDebug("Smappee background sync is disabled (Smappee:BackgroundSync:Enabled).");
                }
                else if (smappeeOpts.ServiceLocationId <= 0)
                {
                    _logger.LogDebug("Smappee background sync skipped: ServiceLocationId is not set.");
                }
                else if (!smappeeOpts.HasAllCredentials)
                {
                    _logger.LogError(
                        "Smappee background sync skipped: credentials incomplete (null/empty). Missing: {Missing}. " +
                        "Set Smappee__ClientId, Smappee__ClientSecret, Smappee__Username, Smappee__Password on the host or in env_file.",
                        string.Join(", ", smappeeOpts.GetMissingCredentialFields()));
                }
                else
                {
                    var lookback = Math.Clamp(bg.LookbackDays, 1, 365);
                    var tz = TimeZoneInfo.FindSystemTimeZoneById("Europe/Brussels");
                    var todayBrussels = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz));
                    var from = todayBrussels.AddDays(-lookback);

                    var sync = scope.ServiceProvider.GetRequiredService<ISmappeeChargingSyncService>();
                    var result = await sync.SyncSmappeeCalendarAsync(
                        from,
                        todayBrussels,
                        "Scheduled Smappee sync",
                        stoppingToken);

                    if (result.Success)
                    {
                        _logger.LogInformation(
                            "Scheduled Smappee sync OK: charge cards +{CardsAdded}/~{CardsUpdated}, sessions +{SessAdded}/~{SessUpdated}, inferred closed {InferredClosed}, skipped {Skipped}, errors {Errors}",
                            result.CardsAdded,
                            result.CardsUpdated,
                            result.SessionsAdded,
                            result.SessionsUpdated,
                            result.SessionsInferredClosed,
                            result.SessionsSkipped,
                            result.SessionsErrors);
                    }
                    else
                    {
                        _logger.LogWarning("Scheduled Smappee sync failed: {Message}", result.ErrorMessage);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Scheduled Smappee sync threw an exception");
            }

            try
            {
                await Task.Delay(delayUntilNext, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }
}
