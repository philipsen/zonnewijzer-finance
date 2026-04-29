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
            var previousSnapshot = await _db.SmappeeStatusSnapshots
                .AsNoTracking()
                .Where(s => s.ServiceLocationId == _options.ServiceLocationId && s.IsSuccess)
                .OrderByDescending(s => s.FetchedAtUtc)
                .ThenByDescending(s => s.Id)
                .FirstOrDefaultAsync(cancellationToken);

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

            var previousStates = ExtractPointStates(previousSnapshot?.PayloadJson);
            var currentStates = ExtractPointStates(payload);
            foreach (var (serial, currentState) in currentStates)
            {
                if (!previousStates.TryGetValue(serial, out var previousState))
                    continue;
                if (string.Equals(previousState, currentState, StringComparison.OrdinalIgnoreCase))
                    continue;

                _db.EventLogs.Add(new EventLog
                {
                    Timestamp = DateTime.UtcNow,
                    UserName = "Integration daemon scheduled sync",
                    Action = $"Charging station {serial} state changed: {previousState} -> {currentState}"
                });
            }

            _db.SmappeeStatusSnapshots.Add(snapshot);
            await _db.SaveChangesAsync(cancellationToken);
            await _publisher.PublishSmappeeAsync(payload, cancellationToken);

            // Keep ChargingSessions aligned with Smappee so ended sessions
            // (for example long-running session ids) are closed in local DB.
            var syncLookbackHours = Math.Clamp(_daemonOptions.LookbackHours, 1, 24 * 14);
            var lookbackDays = Math.Clamp((int)Math.Ceiling(syncLookbackHours / 24.0), 1, 14);
            var brusselsTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Brussels");
            var todayBrussels = DateOnly.FromDateTime(
                TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, brusselsTimeZone));
            var from = todayBrussels.AddDays(-lookbackDays);
            var oldestActiveFromDate = FindOldestActiveSessionDate(active, brusselsTimeZone);
            _logger.LogInformation(
                "Smappee daemon calendar sync planning: trigger=scheduled, serviceLocationId={ServiceLocationId}, lookbackHours={LookbackHours}, lookbackDays={LookbackDays}, todayBrussels={TodayBrussels}, defaultFrom={DefaultFrom}, activeSessions={ActiveSessions}, completedSessions={CompletedSessions}",
                _options.ServiceLocationId,
                syncLookbackHours,
                lookbackDays,
                todayBrussels,
                from,
                active.Count,
                completed.Count);
            if (oldestActiveFromDate.HasValue && oldestActiveFromDate.Value < from)
            {
                _logger.LogInformation(
                    "Extending Smappee daemon calendar sync lookback from {DefaultFrom} to {ExtendedFrom} to include active sessions started earlier.",
                    from,
                    oldestActiveFromDate.Value);
                from = oldestActiveFromDate.Value;
            }
            _logger.LogInformation(
                "Smappee daemon calendar sync executing: trigger=scheduled, from={From}, to={To}",
                from,
                todayBrussels);
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
            _logger.LogWarning(
                ex,
                "Smappee daemon poll failed (serviceLocationId={ServiceLocationId}, credentialsComplete={CredentialsComplete}, daemonLookbackHours={LookbackHours})",
                _options.ServiceLocationId,
                _options.HasAllCredentials,
                _daemonOptions.LookbackHours);
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

    private static DateOnly? FindOldestActiveSessionDate(
        IReadOnlyList<SmappeeSessionDto> activeSessions,
        TimeZoneInfo brusselsTimeZone)
    {
        long? oldestFromMs = null;
        foreach (var session in activeSessions)
        {
            var fromMs = SmappeeSessionTimestamps.ToStoredMilliseconds(session.From);
            if (fromMs <= 0)
                continue;

            if (!oldestFromMs.HasValue || fromMs < oldestFromMs.Value)
                oldestFromMs = fromMs;
        }

        if (!oldestFromMs.HasValue)
            return null;

        var fromUtc = DateTimeOffset.FromUnixTimeMilliseconds(oldestFromMs.Value).UtcDateTime;
        var fromBrussels = TimeZoneInfo.ConvertTimeFromUtc(fromUtc, brusselsTimeZone);
        return DateOnly.FromDateTime(fromBrussels);
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

    public async Task RefreshRfidsAsync(CancellationToken cancellationToken = default)
    {
        if (_options.ServiceLocationId <= 0 || !_options.HasAllCredentials)
            return;

        var rfids = await _cloudClient.GetWhitelistedRfidsAsync(_options.ServiceLocationId, cancellationToken);
        _logger.LogInformation("Smappee RFID refresh returned {Count} card(s)", rfids.Count);
    }

    private static Dictionary<string, string> ExtractPointStates(string? payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var dto = JsonSerializer.Deserialize<SmappeeChargingLiveResponse>(payloadJson);
            if (dto?.Points == null || dto.Points.Count == 0)
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            var states = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var point in dto.Points)
            {
                var serial = (point.SerialNumber ?? "").Trim();
                if (string.IsNullOrEmpty(serial))
                    continue;

                var uiState = string.IsNullOrWhiteSpace(point.UiState) ? "unknown" : point.UiState.Trim();
                states[serial] = uiState;
            }

            return states;
        }
        catch
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }
}
