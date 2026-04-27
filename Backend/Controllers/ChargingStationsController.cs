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
                    var age = DateTimeOffset.UtcNow - latest.FetchedAtUtc;
                    return Ok(new
                    {
                        dto.FetchedAt,
                        dto.ServiceLocationId,
                        dto.Points,
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

        Dictionary<string, string>? serialToLiveState = null;
        Dictionary<string, string>? stationNameToLiveState = null;
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
                serialToLiveState = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                stationNameToLiveState = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var p in live.Points)
                {
                    var mapped = MapChargingPointUiStateToHomeLiveState(p.UiState);
                    var sn = (p.SerialNumber ?? "").Trim();
                    if (!string.IsNullOrEmpty(sn))
                        serialToLiveState[sn] = mapped;
                    var nm = (p.StationName ?? "").Trim();
                    if (!string.IsNullOrEmpty(nm))
                        stationNameToLiveState[nm] = mapped;
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }

        var squares = new List<ChargingStationHomeSquareDto>(ordered.Count);
        foreach (var w in ordered)
        {
            string? liveState = null;
            if (serialToLiveState != null)
            {
                if (w.Serial != null && serialToLiveState.TryGetValue(w.Serial, out var bySerial))
                    liveState = bySerial;
                else if (stationNameToLiveState != null &&
                         stationNameToLiveState.TryGetValue(w.Name, out var byName))
                    liveState = byName;
            }

            // No live / misconfigured: assume available (green). Unmatched with live: same — avoids all-white tiles when SL id or serial pairing is off.
            liveState ??= ChargingStationHomeSquareDto.LiveStateFree;

            squares.Add(new ChargingStationHomeSquareDto { Name = w.Name, LiveState = liveState });
        }

        return Ok(new { squares });
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

    /// <summary>Stored Smappee service locations (devices + last <c>…/info</c> snapshot). No Smappee call.</summary>
    [HttpGet("smappee-servicelocations")]
    public async Task<IActionResult> GetSmappeeServiceLocations(CancellationToken cancellationToken = default)
    {
        var (auth, _) = await RequireAdminForSmappeeServiceLocationsAsync(cancellationToken);
        if (auth != null)
            return auth;

        var daemonResult = await _integrationDaemonClient.GetSmappeeServiceLocationsAsync(cancellationToken);
        if (!daemonResult.Enabled)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                error = daemonResult.Error ?? "Integration daemon trigger is disabled."
            });
        }
        if (!daemonResult.Success)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new
            {
                error = daemonResult.Error ?? "Could not load service locations from integration daemon."
            });
        }
        return Ok(new
        {
            appName = (string?)null,
            serviceLocations = daemonResult.ServiceLocations,
            fetchedAt = daemonResult.FetchedAtUtc
        });
    }

    /// <summary>Refresh is daemon-owned; backend no longer calls Smappee cloud directly.</summary>
    [HttpPost("smappee-servicelocations/refresh")]
    public async Task<IActionResult> PostSmappeeServiceLocationsRefresh(CancellationToken cancellationToken = default)
    {
        var (auth, _) = await RequireAdminForSmappeeServiceLocationsAsync(cancellationToken);
        if (auth != null)
            return auth;

        var result = await _integrationDaemonClient.TriggerSmappeeServiceLocationsRefreshAsync(cancellationToken);
        if (!result.Enabled)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                error = result.Error ?? "Integration daemon trigger is disabled."
            });
        }

        if (!result.Success)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new
            {
                error = result.Error ?? "Could not refresh service locations from integration daemon."
            });
        }

        var daemonResult = await _integrationDaemonClient.GetSmappeeServiceLocationsAsync(cancellationToken);
        if (!daemonResult.Success)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new
            {
                error = daemonResult.Error ?? "Could not load refreshed service locations from integration daemon."
            });
        }

        return Ok(new
        {
            appName = (string?)null,
            serviceLocations = daemonResult.ServiceLocations,
            fetchedAt = daemonResult.FetchedAtUtc
        });
    }

    private async Task<(IActionResult? Error, int? UserId)> RequireAdminForSmappeeServiceLocationsAsync(
        CancellationToken cancellationToken)
    {
        var token = Request.Headers.Authorization.ToString().Replace("Bearer ", "");
        var userIdString = _jwtService.ValidateToken(token);

        if (userIdString == null || !int.TryParse(userIdString, out int currentUserId))
        {
            _logger.LogWarning("Unauthorized attempt to access Smappee service locations");
            return (Unauthorized(), null);
        }

        var requestingUser = await _context.Users.FindAsync(new object[] { currentUserId }, cancellationToken);
        if (requestingUser == null || !requestingUser.Role.IsAppAdministrator())
            return (Forbid(), null);

        return (null, currentUserId);
    }

    private async Task<(List<SmappeeServiceLocationListItemResponse> Items, DateTimeOffset? FetchedAt)> LoadSmappeeServiceLocationsFromDbAsync(
        CancellationToken cancellationToken)
    {
        var devices = await _context.SmappeeServiceLocationDevices.AsNoTracking()
            .OrderBy(d => d.ServiceLocationId)
            .ThenBy(d => d.DeviceSerialNumber)
            .ToListAsync(cancellationToken);

        if (devices.Count == 0)
            return (new List<SmappeeServiceLocationListItemResponse>(), null);

        var locIds = devices.Select(d => d.ServiceLocationId).Distinct().ToList();
        var locs = await _context.SmappeeServiceLocations.AsNoTracking()
            .Where(s => locIds.Contains(s.ServiceLocationId))
            .ToDictionaryAsync(s => s.ServiceLocationId, cancellationToken);

        var items = new List<SmappeeServiceLocationListItemResponse>(devices.Count);
        foreach (var d in devices)
        {
            var item = new SmappeeServiceLocationListItemResponse
            {
                ServiceLocationId = d.ServiceLocationId,
                Name = d.Name,
                ServiceLocationUuid = d.ServiceLocationUuid,
                DeviceSerialNumber = string.IsNullOrWhiteSpace(d.DeviceSerialNumber) ? null : d.DeviceSerialNumber
            };
            if (locs.TryGetValue(d.ServiceLocationId, out var loc))
                EnrichListItemFromStoredLocation(item, loc);
            items.Add(item);
        }

        var tDev = devices.Max(x => x.UpdatedAtUtc);
        DateTime? tLoc = null;
        if (locs.Count > 0)
            tLoc = locs.Values.Max(l => l.UpdatedAtUtc);
        var latest = tLoc.HasValue && tLoc.Value > tDev ? tLoc.Value : tDev;
        var fetchedAt = new DateTimeOffset(DateTime.SpecifyKind(latest, DateTimeKind.Utc));

        return (items, fetchedAt);
    }

    private void EnrichListItemFromStoredLocation(SmappeeServiceLocationListItemResponse item, SmappeeServiceLocation loc)
    {
        item.Longitude = loc.Longitude;
        item.Latitude = loc.Latitude;
        item.ElectricityCost = loc.ElectricityCost;
        item.ElectricityCurrency = loc.ElectricityCurrency;
        item.Timezone = loc.Timezone;
        if (string.IsNullOrEmpty(loc.AppliancesJson))
            return;
        try
        {
            item.Appliances = JsonSerializer.Deserialize<List<SmappeeServiceLocationApplianceItemResponse>>(
                loc.AppliancesJson,
                SmappeeAppliancesJson);
            if (item.Appliances != null)
            {
                foreach (var a in item.Appliances)
                    a.Name = SmappeeApplianceDisplayName.NormalizeName(a.Name);
            }
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Stored appliances JSON invalid for service location {Id}", item.ServiceLocationId);
        }
    }

}
