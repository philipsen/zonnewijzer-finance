using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Backend;
using Backend.Data;
using Backend.Models;
using Backend.Services.IntegrationDaemon;
using Backend.Services;
using Backend.Services.Smappee;

namespace Backend.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ChargingStationsController : ControllerBase
{
    private static readonly JsonSerializerOptions SmappeeAppliancesJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
    private static readonly Dictionary<string, string> HardcodedStationNameBySerial = new(StringComparer.OrdinalIgnoreCase)
    {
        // Provider live payload serials may diverge from stored device serials.
        // Keep explicit fallback mappings for known chargers so UI state lands on the correct tile.
        ["5821063866"] = "Paal 1",
        ["5821063800"] = "Paal 2",
        ["5821063911"] = "Paal 3",
        ["5821063919"] = "Paal 4",
        ["5821063914"] = "Paal 5"
    };

    private readonly ApplicationDbContext _context;
    private readonly IJwtService _jwtService;
    private readonly IIntegrationDaemonClient _integrationDaemonClient;
    private readonly ILogger<ChargingStationsController> _logger;

    public ChargingStationsController(
        ApplicationDbContext context,
        IJwtService jwtService,
        IIntegrationDaemonClient integrationDaemonClient,
        ILogger<ChargingStationsController> logger)
    {
        _context = context;
        _jwtService = jwtService;
        _integrationDaemonClient = integrationDaemonClient;
        _logger = logger;
    }

    [Authorize(Roles = "Base,Admin,Owner")]
    [HttpGet("smappee-live")]
    public async Task<IActionResult> GetSmappeeLive(CancellationToken cancellationToken = default)
    {
        try
        {
            var latest = await _context.SmappeeStatusSnapshots
                .AsNoTracking()
                .OrderByDescending(x => x.FetchedAtUtc)
                .FirstOrDefaultAsync(cancellationToken);

            if (latest != null && latest.IsSuccess && !string.IsNullOrWhiteSpace(latest.PayloadJson))
            {
                var dto = JsonSerializer.Deserialize<SmappeeChargingLiveResponse>(latest.PayloadJson);
                if (dto != null)
                {
                    var storedNameBySerial = await _context.SmappeeServiceLocationDevices
                        .AsNoTracking()
                        .Where(d => d.DeviceSerialNumber != null && d.DeviceSerialNumber != "")
                        .Select(d => new { d.DeviceSerialNumber, d.Name })
                        .ToListAsync(cancellationToken);

                    var stationNameBySerial = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var row in storedNameBySerial)
                    {
                        var serial = (row.DeviceSerialNumber ?? "").Trim();
                        if (serial.Length == 0)
                            continue;

                        var normalizedName = SmappeeApplianceDisplayName.NormalizeName(row.Name);
                        if (string.IsNullOrWhiteSpace(normalizedName))
                            continue;

                        // Keep first seen stored display name for a serial.
                        if (!stationNameBySerial.ContainsKey(serial))
                            stationNameBySerial[serial] = normalizedName;
                    }

                    var pointsWithStoredNames = dto.Points
                        .Select(p =>
                        {
                            var serial = (p.SerialNumber ?? "").Trim();
                            stationNameBySerial.TryGetValue(serial, out var storedName);
                            return new ChargingPointLiveDto
                            {
                                SerialNumber = p.SerialNumber ?? "",
                                StationName = string.IsNullOrWhiteSpace(storedName) ? p.StationName : storedName,
                                UiState = p.UiState,
                                SmappeeStatus = p.SmappeeStatus,
                                EnergyKwh = p.EnergyKwh,
                                StartedAtUtc = p.StartedAtUtc,
                                EndedAtUtc = p.EndedAtUtc,
                                RfidMasked = p.RfidMasked
                            };
                        })
                        .ToList();

                    var age = DateTimeOffset.UtcNow - latest.FetchedAtUtc;
                    return Ok(new
                    {
                        dto.FetchedAt,
                        dto.ServiceLocationId,
                        points = pointsWithStoredNames,
                        dto.RangeStartMs,
                        dto.RangeEndMsExclusive,
                        stale = age > TimeSpan.FromMinutes(10)
                    });
                }
            }

            return StatusCode(503, new { error = "No Smappee snapshot available yet. Wait for integration daemon polling." });
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Smappee live snapshot failed");
            return StatusCode(502, new { error = "Could not load live data from the charging system." });
        }
    }

    /// <summary>
    /// Up to five charging-station appliances from stored <c>…/info</c>, ordered by name, with optional <see cref="ChargingStationHomeSquareDto.LiveState"/> from Smappee live (matched by device serial when possible).
    /// </summary>
    [Authorize(Roles = "Base,Admin,Owner")]
    [HttpGet("smappee-charging-station-display-names")]
    public async Task<IActionResult> GetSmappeeChargingStationDisplayNames(CancellationToken cancellationToken = default)
    {
        const string chargingStationType = "charging station";
        var locRows = await _context.SmappeeServiceLocations.AsNoTracking()
            .Where(s => s.AppliancesJson != null && s.AppliancesJson != "")
            .OrderBy(s => s.ServiceLocationId)
            .Select(s => new { s.ServiceLocationId, s.AppliancesJson })
            .ToListAsync(cancellationToken);

        var devicesByLoc = (await _context.SmappeeServiceLocationDevices.AsNoTracking()
                .ToListAsync(cancellationToken))
            .GroupBy(d => d.ServiceLocationId)
            .ToDictionary(
                g => g.Key,
                g => g.OrderBy(d => d.DeviceSerialNumber, StringComparer.OrdinalIgnoreCase).ToList());

        var work = new List<(string Name, string? Serial, int ServiceLocationId)>();
        foreach (var row in locRows)
        {
            List<SmappeeServiceLocationApplianceDto>? appliances;
            try
            {
                appliances = JsonSerializer.Deserialize<List<SmappeeServiceLocationApplianceDto>>(
                    row.AppliancesJson!,
                    SmappeeAppliancesJson);
            }
            catch (JsonException)
            {
                continue;
            }

            if (appliances == null)
                continue;

            if (!devicesByLoc.TryGetValue(row.ServiceLocationId, out var devices))
                devices = new List<SmappeeServiceLocationDevice>();

            var charging = appliances
                .Where(a =>
                    !string.IsNullOrWhiteSpace(a.Type) &&
                    string.Equals(a.Type.Trim(), chargingStationType, StringComparison.OrdinalIgnoreCase))
                .OrderBy(a => a.Id)
                .ToList();

            var usedDeviceIds = new HashSet<int>();
            foreach (var a in charging)
            {
                var name = SmappeeApplianceDisplayName.NormalizeName(a.Name);
                if (name == null)
                    continue;
                var dev = MatchDeviceToAppliance(devices, usedDeviceIds, name);
                var serial = string.IsNullOrWhiteSpace(dev?.DeviceSerialNumber) ? null : dev!.DeviceSerialNumber.Trim();
                work.Add((name, serial, row.ServiceLocationId));
            }
        }

        var ordered = work
            .OrderBy(w => w.Name, StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .ToList();

        Dictionary<string, HomeSquareLiveInfo>? serialToLiveInfo = null;
        Dictionary<string, HomeSquareLiveInfo>? stationNameToLiveInfo = null;
        List<HomeSquareLiveInfo>? unmatchedLiveInfos = null;
        var discoveredSerials = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var discoveredSerialsMatchedByName = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var latest = await _context.SmappeeStatusSnapshots
                .AsNoTracking()
                .Where(x => x.IsSuccess)
                .OrderByDescending(x => x.FetchedAtUtc)
                .FirstOrDefaultAsync(cancellationToken);
            var live = latest == null ? null : JsonSerializer.Deserialize<SmappeeChargingLiveResponse>(latest.PayloadJson);
            if (live != null)
            {
                serialToLiveInfo = new Dictionary<string, HomeSquareLiveInfo>(StringComparer.OrdinalIgnoreCase);
                stationNameToLiveInfo = new Dictionary<string, HomeSquareLiveInfo>(StringComparer.OrdinalIgnoreCase);
                var matchedSerials = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var matchedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                unmatchedLiveInfos = new List<HomeSquareLiveInfo>();
                foreach (var p in live.Points)
                {
                    var mapped = MapChargingPointUiStateToHomeLiveState(p.UiState);
                    var liveInfo = new HomeSquareLiveInfo(mapped, EstimatePowerKwForHomeSquare(p, mapped));
                    var sn = (p.SerialNumber ?? "").Trim();
                    var hasMatchedSerial = false;
                    if (!string.IsNullOrEmpty(sn))
                    {
                        discoveredSerials.Add(sn);
                        hasMatchedSerial = ordered.Any(w =>
                            !string.IsNullOrWhiteSpace(w.Serial) &&
                            string.Equals(w.Serial, sn, StringComparison.OrdinalIgnoreCase));
                        serialToLiveInfo[sn] = PickStrongerHomeLiveInfo(serialToLiveInfo.GetValueOrDefault(sn), liveInfo);
                        if (hasMatchedSerial)
                            matchedSerials.Add(sn);
                    }
                    var configuredName = ResolveHardcodedStationName(sn);
                    var nm = SmappeeApplianceDisplayName.NormalizeName(
                        string.IsNullOrWhiteSpace(p.StationName) ? configuredName : p.StationName);
                    var hasMatchedName = false;
                    if (!string.IsNullOrEmpty(nm))
                    {
                        hasMatchedName = ordered.Any(w =>
                            string.Equals(
                                SmappeeApplianceDisplayName.NormalizeName(w.Name) ?? w.Name,
                                nm,
                                StringComparison.OrdinalIgnoreCase));
                        stationNameToLiveInfo[nm] = PickStrongerHomeLiveInfo(stationNameToLiveInfo.GetValueOrDefault(nm), liveInfo);
                        if (hasMatchedName)
                        {
                            matchedNames.Add(nm);
                            if (!string.IsNullOrEmpty(sn))
                                discoveredSerialsMatchedByName.Add(sn);
                        }
                    }

                    // If this point cannot be matched to one of the shown squares by serial or name,
                    // keep it as fallback so active/idle state is still surfaced instead of all-free.
                    if (!hasMatchedSerial && !hasMatchedName)
                        unmatchedLiveInfos.Add(liveInfo);
                }

                if (unmatchedLiveInfos.Count > 1)
                {
                    unmatchedLiveInfos = unmatchedLiveInfos
                        .OrderByDescending(x => HomeLiveStateRank(x.LiveState))
                        .ThenByDescending(x => x.PowerKw ?? 0m)
                        .ToList();
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }

        if (discoveredSerials.Count == 0)
        {
            var serialsFromSessions = await _context.ChargingSessions
                .AsNoTracking()
                .Where(s => s.SerialNumber != null && s.SerialNumber != "")
                .Select(s => s.SerialNumber!)
                .Distinct()
                .ToListAsync(cancellationToken);
            foreach (var serial in serialsFromSessions)
            {
                var normalized = serial.Trim();
                if (normalized.Length > 0)
                    discoveredSerials.Add(normalized);
            }
        }

        if (discoveredSerials.Count > 0)
        {
            var existingSerials = new HashSet<string>(
                work.Where(w => !string.IsNullOrWhiteSpace(w.Serial)).Select(w => w.Serial!),
                StringComparer.OrdinalIgnoreCase);
            var existingNames = new HashSet<string>(
                work.Select(w => SmappeeApplianceDisplayName.NormalizeName(w.Name) ?? w.Name),
                StringComparer.OrdinalIgnoreCase);
            foreach (var serial in discoveredSerials.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                if (existingSerials.Contains(serial))
                    continue;
                if (discoveredSerialsMatchedByName.Contains(serial))
                    continue;
                var name = ResolveHardcodedStationName(serial);
                if (!string.IsNullOrWhiteSpace(name))
                {
                    var normalizedName = SmappeeApplianceDisplayName.NormalizeName(name) ?? name;
                    if (existingNames.Contains(normalizedName))
                        continue;
                }
                if (string.IsNullOrWhiteSpace(name))
                    name = $"Unknown {serial}";
                work.Add((name, serial, 0));
            }
            ordered = work
                .OrderBy(w => w.Name, StringComparer.OrdinalIgnoreCase)
                .Take(5)
                .ToList();
        }

        var squares = new List<ChargingStationHomeSquareDto>(ordered.Count);
        var unmatchedLiveIndex = 0;
        foreach (var w in ordered)
        {
            HomeSquareLiveInfo? liveInfo = null;
            if (serialToLiveInfo != null)
            {
                if (w.Serial != null && serialToLiveInfo.TryGetValue(w.Serial, out var bySerial))
                    liveInfo = bySerial;
                else if (stationNameToLiveInfo != null)
                {
                    var normalizedName = SmappeeApplianceDisplayName.NormalizeName(w.Name) ?? w.Name;
                    if (stationNameToLiveInfo.TryGetValue(normalizedName, out var byName))
                        liveInfo = byName;
                }
            }

            if (liveInfo == null && unmatchedLiveInfos != null && unmatchedLiveIndex < unmatchedLiveInfos.Count)
            {
                // Place unresolved live points from the end of the ordered tiles first.
                // In current home layout this avoids showing unknown activity on "Paal 1".
                var sourceIndex = unmatchedLiveInfos.Count - 1 - unmatchedLiveIndex;
                liveInfo = unmatchedLiveInfos[sourceIndex];
                unmatchedLiveIndex++;
            }

            // No live / misconfigured: assume available (green). Unmatched with live: same — avoids all-white tiles when SL id or serial pairing is off.
            var liveState = liveInfo?.LiveState ?? ChargingStationHomeSquareDto.LiveStateFree;

            squares.Add(new ChargingStationHomeSquareDto
            {
                Name = w.Name,
                LiveState = liveState,
                InUse = IsHomeSquareInUse(liveState),
                PowerKw = liveInfo?.PowerKw
            });
        }

        var mergedSquares = MergeSquaresByNormalizedNameKeepingStrongest(squares);
        var serialNumbers = discoveredSerials
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return Ok(new { squares = mergedSquares, serialNumbers });
    }

    /// <summary>Green = no session, yellow = idle (preparing/suspended), red = active (charging).</summary>
    private static string MapChargingPointUiStateToHomeLiveState(string? uiState) =>
        uiState switch
        {
            "charging" => ChargingStationHomeSquareDto.LiveStateActive,
            "preparing" or "suspended" => ChargingStationHomeSquareDto.LiveStateIdle,
            "available" or "finished_recent" => ChargingStationHomeSquareDto.LiveStateFree,
            _ => ChargingStationHomeSquareDto.LiveStateFree
        };

    private static HomeSquareLiveInfo PickStrongerHomeLiveInfo(HomeSquareLiveInfo? current, HomeSquareLiveInfo incoming)
    {
        if (current == null)
            return incoming;

        var incomingRank = HomeLiveStateRank(incoming.LiveState);
        var currentRank = HomeLiveStateRank(current.LiveState);
        if (incomingRank > currentRank)
            return incoming;
        if (incomingRank < currentRank)
            return current;

        if (incoming.PowerKw.HasValue && (!current.PowerKw.HasValue || incoming.PowerKw.Value > current.PowerKw.Value))
            return incoming;
        return current;
    }

    private static int HomeLiveStateRank(string? state) => state switch
    {
        ChargingStationHomeSquareDto.LiveStateActive => 3,
        ChargingStationHomeSquareDto.LiveStateIdle => 2,
        ChargingStationHomeSquareDto.LiveStateFree => 1,
        _ => 0
    };

    private static bool IsHomeSquareInUse(string? liveState) =>
        liveState == ChargingStationHomeSquareDto.LiveStateActive || liveState == ChargingStationHomeSquareDto.LiveStateIdle;

    private static decimal? EstimatePowerKwForHomeSquare(ChargingPointLiveDto point, string homeLiveState)
    {
        if (homeLiveState != ChargingStationHomeSquareDto.LiveStateActive)
            return null;
        if (!point.EnergyKwh.HasValue || !point.StartedAtUtc.HasValue)
            return null;

        var endUtc = point.EndedAtUtc ?? DateTime.UtcNow;
        var hours = (decimal)(endUtc - point.StartedAtUtc.Value).TotalHours;
        if (hours <= 0m)
            return null;

        var kw = point.EnergyKwh.Value / hours;
        if (kw <= 0m)
            return null;
        return decimal.Round(kw, 2, MidpointRounding.AwayFromZero);
    }

    private static string? ResolveHardcodedStationName(string? serial)
    {
        var sn = (serial ?? "").Trim();
        if (string.IsNullOrEmpty(sn))
            return null;
        return HardcodedStationNameBySerial.TryGetValue(sn, out var fallback) ? fallback : null;
    }

    private static List<ChargingStationHomeSquareDto> MergeSquaresByNormalizedNameKeepingStrongest(
        IReadOnlyList<ChargingStationHomeSquareDto> squares)
    {
        var merged = new Dictionary<string, ChargingStationHomeSquareDto>(StringComparer.OrdinalIgnoreCase);
        var order = new List<string>();

        foreach (var square in squares)
        {
            var key = SmappeeApplianceDisplayName.NormalizeName(square.Name) ?? square.Name;
            if (!merged.TryGetValue(key, out var current))
            {
                merged[key] = square;
                order.Add(key);
                continue;
            }

            var stronger = PickStrongerHomeLiveInfo(
                new HomeSquareLiveInfo(current.LiveState ?? ChargingStationHomeSquareDto.LiveStateFree, current.PowerKw),
                new HomeSquareLiveInfo(square.LiveState ?? ChargingStationHomeSquareDto.LiveStateFree, square.PowerKw));

            current.LiveState = stronger.LiveState;
            current.InUse = IsHomeSquareInUse(stronger.LiveState);
            current.PowerKw = stronger.PowerKw;
        }

        var result = new List<ChargingStationHomeSquareDto>(order.Count);
        foreach (var key in order)
            result.Add(merged[key]);
        return result;
    }

    private sealed record HomeSquareLiveInfo(string LiveState, decimal? PowerKw);

    private static SmappeeServiceLocationDevice? MatchDeviceToAppliance(
        List<SmappeeServiceLocationDevice> devices,
        ISet<int> usedDeviceIds,
        string normalizedApplianceName)
    {
        foreach (var d in devices)
        {
            if (usedDeviceIds.Contains(d.Id))
                continue;
            var dn = SmappeeApplianceDisplayName.NormalizeName(d.Name);
            if (dn != null &&
                string.Equals(dn, normalizedApplianceName, StringComparison.OrdinalIgnoreCase))
            {
                usedDeviceIds.Add(d.Id);
                return d;
            }
        }

        foreach (var d in devices.OrderBy(x => x.DeviceSerialNumber, StringComparer.OrdinalIgnoreCase))
        {
            if (usedDeviceIds.Contains(d.Id))
                continue;
            usedDeviceIds.Add(d.Id);
            return d;
        }

        return null;
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
