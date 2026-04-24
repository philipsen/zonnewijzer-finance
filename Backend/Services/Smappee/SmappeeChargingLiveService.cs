using Backend.Data;
using Backend.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Backend.Services.Smappee;

public class SmappeeChargingLiveService : ISmappeeChargingLiveService
{
    private readonly ISmappeeCloudClient _smappee;
    private readonly ApplicationDbContext _db;
    private readonly SmappeeOptions _options;
    private readonly ILogger<SmappeeChargingLiveService> _logger;

    public SmappeeChargingLiveService(
        ISmappeeCloudClient smappee,
        ApplicationDbContext db,
        IOptions<SmappeeOptions> options,
        ILogger<SmappeeChargingLiveService> logger)
    {
        _smappee = smappee;
        _db = db;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<SmappeeChargingLiveResponse> GetLiveAsync(CancellationToken cancellationToken = default)
    {
        if (_options.ServiceLocationId <= 0)
            throw new InvalidOperationException("Smappee ServiceLocationId is not configured.");

        if (!_options.HasAllCredentials)
            throw new InvalidOperationException("Smappee API credentials are not configured.");

        var now = DateTimeOffset.UtcNow;
        var rangeStartMs = now.AddDays(-SmappeeChargingLiveMapper.LookbackDays).ToUnixTimeMilliseconds();
        var rangeEndMs = now.ToUnixTimeMilliseconds();

        _logger.LogDebug(
            "Smappee live: serviceLocationId={Id} range={Start},{End} (excl end)",
            _options.ServiceLocationId,
            rangeStartMs,
            rangeEndMs);

        var (active, completed, smappeeParkSerials) = await _smappee.GetChargingLiveDataAsync(
            _options.ServiceLocationId,
            rangeStartMs,
            rangeEndMs,
            cancellationToken);

        if (smappeeParkSerials.Count > 0)
        {
            _logger.LogDebug(
                "Smappee live: {Count} charger serial(s) from servicelocation list for park {Id}",
                smappeeParkSerials.Count,
                _options.ServiceLocationId);
        }

        var knownSerials = await _db.ChargingSessions
            .AsNoTracking()
            .Where(s => s.SerialNumber != null && s.SerialNumber != "")
            .Select(s => s.SerialNumber!)
            .Distinct()
            .ToListAsync(cancellationToken);

        var expectedMerged = MergeExpectedAndSmappeeSerials(_options.ExpectedChargerSerials, smappeeParkSerials);

        var fetched = DateTimeOffset.UtcNow;
        return SmappeeChargingLiveMapper.Build(
            _options.ServiceLocationId,
            active,
            completed,
            rangeStartMs,
            rangeEndMs,
            fetched,
            now,
            _options.StationDisplayNamesBySerial,
            knownSerials,
            expectedMerged);
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
}
