using System.Text.Json;
using Backend.Data;
using Backend.Models;
using Backend.Services.Smappee;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace IntegrationDaemon.Services;

public interface ISmappeePollingService
{
    Task<SmappeeStatusSnapshot> PollSessionsAsync(CancellationToken cancellationToken = default);
    Task RefreshServiceLocationInfoAsync(CancellationToken cancellationToken = default);
    Task RefreshServiceLocationsListAsync(CancellationToken cancellationToken = default);
    Task RefreshRfidsAsync(CancellationToken cancellationToken = default);
}

public class SmappeePollingService : ISmappeePollingService
{
    private readonly ISmappeeCloudClient _cloudClient;
    private readonly ApplicationDbContext _db;
    private readonly SmappeeOptions _options;
    private readonly SmappeeDaemonOptions _daemonOptions;
    private readonly ISmappeeChargingSyncService _chargingSync;
    private readonly IStatusPublisher _publisher;
    private readonly ILogger<SmappeePollingService> _logger;

    public SmappeePollingService(
        ISmappeeCloudClient cloudClient,
        ApplicationDbContext db,
        IOptions<SmappeeOptions> options,
        IOptions<SmappeeDaemonOptions> daemonOptions,
        ISmappeeChargingSyncService chargingSync,
        IStatusPublisher publisher,
        ILogger<SmappeePollingService> logger)
    {
        _cloudClient = cloudClient;
        _db = db;
        _options = options.Value;
        _daemonOptions = daemonOptions.Value;
        _chargingSync = chargingSync;
        _publisher = publisher;
        _logger = logger;
    }

    public async Task<SmappeeStatusSnapshot> PollSessionsAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        _logger.LogInformation("Polling Smappee sessions at {NowUtc}", now.UtcDateTime);
        if (_options.ServiceLocationId <= 0 || !_options.HasAllCredentials)
        {
            var unconfigured = new SmappeeStatusSnapshot
            {
                ServiceLocationId = _options.ServiceLocationId,
                FetchedAtUtc = now.UtcDateTime,
                IsSuccess = false,
                Error = "Smappee credentials or service location are not configured."
            };
            _db.SmappeeStatusSnapshots.Add(unconfigured);
            await _db.SaveChangesAsync(cancellationToken);
            return unconfigured;
        }

        try
        {
            var rangeEndMs = now.ToUnixTimeMilliseconds();
            var lookbackHours = Math.Clamp(_daemonOptions.LookbackHours, 1, 24 * 14);
            var rangeStartMs = now.AddHours(-lookbackHours).ToUnixTimeMilliseconds();
            var (active, completed, smappeeParkSerials) = await _cloudClient.GetChargingLiveDataAsync(
                _options.ServiceLocationId,
                rangeStartMs,
                rangeEndMs,
                cancellationToken);

            var knownSerials = await _db.ChargingSessions
                .AsNoTracking()
                .Where(s => s.SerialNumber != null && s.SerialNumber != "")
                .Select(s => s.SerialNumber!)
                .Distinct()
                .ToListAsync(cancellationToken);
            var expectedMerged = MergeExpectedAndSmappeeSerials(_options.ExpectedChargerSerials, smappeeParkSerials);
            var live = SmappeeChargingLiveMapper.Build(
                _options.ServiceLocationId,
                active,
                completed,
                rangeStartMs,
                rangeEndMs,
                now,
                now,
                _options.StationDisplayNamesBySerial,
                knownSerials,
                expectedMerged);
            _logger.LogInformation(
                "Smappee sessions summary: ServiceLocationId={ServiceLocationId}, ActiveSessions={ActiveSessions}, CompletedSessions={CompletedSessions}, ChargePoints={ChargePoints}, RangeStartMs={RangeStartMs}, RangeEndMsExclusive={RangeEndMsExclusive}",
                live.ServiceLocationId,
                active.Count,
                completed.Count,
                live.Points.Count,
                rangeStartMs,
                rangeEndMs);
            var payload = JsonSerializer.Serialize(live);
            var snapshot = new SmappeeStatusSnapshot
            {
                ServiceLocationId = live.ServiceLocationId,
                FetchedAtUtc = now.UtcDateTime,
                IsSuccess = true,
                RangeStartMs = live.RangeStartMs,
                RangeEndMsExclusive = live.RangeEndMsExclusive,
                ActiveSessionCount = live.Points.Count(p => p.UiState is "charging" or "preparing" or "suspended"),
                CompletedSessionCount = live.Points.Count(p => p.UiState is "finished_recent" or "available"),
                PayloadJson = payload
            };

            _db.SmappeeStatusSnapshots.Add(snapshot);
            await _db.SaveChangesAsync(cancellationToken);
            await _publisher.PublishSmappeeAsync(payload, cancellationToken);

            // Keep ChargingSessions aligned with Smappee so ended sessions
            // (for example long-running session ids) are closed in local DB.
            var syncLookbackHours = Math.Clamp(_daemonOptions.LookbackHours, 1, 24 * 14);
            var lookbackDays = Math.Clamp((int)Math.Ceiling(syncLookbackHours / 24.0), 1, 14);
            var todayBrussels = DateOnly.FromDateTime(
                TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Europe/Brussels")));
            var from = todayBrussels.AddDays(-lookbackDays);
            var syncResult = await _chargingSync.SyncSmappeeCalendarAsync(
                from,
                todayBrussels,
                "Integration daemon scheduled sync",
                cancellationToken);

            if (!syncResult.Success)
            {
                _logger.LogWarning("Smappee daemon calendar sync failed: {ErrorMessage}", syncResult.ErrorMessage);
            }
            else
            {
                _logger.LogInformation(
                    "Smappee daemon calendar sync OK: sessions +{Added}/~{Updated}, skipped {Skipped}, errors {Errors}",
                    syncResult.SessionsAdded,
                    syncResult.SessionsUpdated,
                    syncResult.SessionsSkipped,
                    syncResult.SessionsErrors);
            }

            _logger.LogInformation(
                "Stored Smappee snapshot. ServiceLocationId={ServiceLocationId}, Active={Active}, Completed={Completed}",
                snapshot.ServiceLocationId,
                snapshot.ActiveSessionCount,
                snapshot.CompletedSessionCount);
            return snapshot;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Smappee daemon poll failed");
            var failed = new SmappeeStatusSnapshot
            {
                ServiceLocationId = _options.ServiceLocationId,
                FetchedAtUtc = now.UtcDateTime,
                IsSuccess = false,
                Error = ex.Message
            };
            _db.SmappeeStatusSnapshots.Add(failed);
            await _db.SaveChangesAsync(cancellationToken);
            return failed;
        }
    }

    private static List<string>? MergeExpectedAndSmappeeSerials(
        IReadOnlyList<string>? configuredExpected,
        IReadOnlyList<string> fromSmappee)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var list = new List<string>();
        void add(string? raw)
        {
            var s = (raw ?? "").Trim();
            if (string.IsNullOrEmpty(s) || !set.Add(s))
                return;
            list.Add(s);
        }

        if (configuredExpected != null)
        {
            foreach (var s in configuredExpected)
                add(s);
        }

        foreach (var s in fromSmappee)
            add(s);

        return list.Count == 0 ? null : list;
    }

    public async Task RefreshServiceLocationInfoAsync(CancellationToken cancellationToken = default)
    {
        if (_options.ServiceLocationId <= 0 || !_options.HasAllCredentials)
            return;

        var token = await _cloudClient.GetAccessTokenAsync(cancellationToken);
        var info = await _cloudClient.GetServiceLocationInfoAsync(token, _options.ServiceLocationId, cancellationToken);
        var now = DateTime.UtcNow;
        var loc = await _db.SmappeeServiceLocations.FindAsync(new object[] { _options.ServiceLocationId }, cancellationToken)
            ?? new SmappeeServiceLocation { ServiceLocationId = _options.ServiceLocationId };

        loc.Longitude = info.Lon;
        loc.Latitude = info.Lat;
        loc.ElectricityCost = (decimal)info.ElectricityCost;
        loc.ElectricityCurrency = string.IsNullOrWhiteSpace(info.ElectricityCurrency) ? null : info.ElectricityCurrency.Trim();
        loc.Timezone = string.IsNullOrWhiteSpace(info.Timezone) ? null : info.Timezone.Trim();
        loc.AppliancesJson = info.Appliances is { Count: > 0 } ? JsonSerializer.Serialize(info.Appliances) : null;
        loc.InfoFetchedAtUtc = now;
        loc.UpdatedAtUtc = now;
        _db.Update(loc);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task RefreshServiceLocationsListAsync(CancellationToken cancellationToken = default)
    {
        if (!_options.HasAllCredentials)
            return;

        var (_, rows) = await _cloudClient.GetServiceLocationsListAsync(cancellationToken);
        var now = DateTime.UtcNow;
        _db.SmappeeServiceLocationDevices.RemoveRange(_db.SmappeeServiceLocationDevices);
        foreach (var row in rows)
        {
            _db.SmappeeServiceLocationDevices.Add(new SmappeeServiceLocationDevice
            {
                ServiceLocationId = row.ServiceLocationId,
                DeviceSerialNumber = (row.DeviceSerialNumber ?? string.Empty).Trim(),
                Name = string.IsNullOrWhiteSpace(row.Name) ? null : row.Name.Trim(),
                ServiceLocationUuid = string.IsNullOrWhiteSpace(row.ServiceLocationUuid) ? null : row.ServiceLocationUuid.Trim(),
                UpdatedAtUtc = now
            });
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task RefreshRfidsAsync(CancellationToken cancellationToken = default)
    {
        if (_options.ServiceLocationId <= 0 || !_options.HasAllCredentials)
            return;

        var rfids = await _cloudClient.GetWhitelistedRfidsAsync(_options.ServiceLocationId, cancellationToken);
        _logger.LogInformation("Smappee RFID refresh returned {Count} card(s)", rfids.Count);
    }
}
