using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Backend;
using Backend.Data;
using Backend.Models;
using Backend.Services;
using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;
using CsvHelper.Configuration.Attributes;

namespace Backend.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ChargingSessionsController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly IJwtService _jwtService;
        private readonly ILogger<ChargingSessionsController> _logger;

        public ChargingSessionsController(ApplicationDbContext context, IJwtService jwtService, ILogger<ChargingSessionsController> logger)
        {
            _context = context;
            _jwtService = jwtService;
            _logger = logger;
        }

        [Authorize(Roles = "Admin")]
        [HttpPost]
        public async Task<IActionResult> AddSession([FromBody] AddSessionRequest request)
        {
            try
            {
                if (request.End.HasValue && request.End.Value < request.Start)
                    return BadRequest("End time cannot be before start time");

                if (request.ChargedKwh < 0)
                    return BadRequest("ChargedKwh must be non-negative");

                if (!TryResolveStationForWrite(request, out var stationId, out var stationSerial, out var connector, out var stationName, out var stationError))
                    return BadRequest(stationError);

                var existing = await _context.ChargingSessions.FirstOrDefaultAsync(s =>
                    s.Start == request.Start && s.UserId == request.UserId && s.StationId == stationId);

                ChargingSession session;
                var replacedExisting = false;
                if (existing != null)
                {
                    session = existing;
                    replacedExisting = true;
                    session.End = request.End;
                    session.StationSerialNumber = stationSerial;
                    session.Connector = connector;
                    session.ChargingStationName = stationName;
                    session.ChargedKwh = request.ChargedKwh;
                    _context.EventLogs.Add(new EventLog
                    {
                        Timestamp = DateTime.UtcNow,
                        UserName = User.Identity?.Name ?? "Unknown",
                        Action = $"Updated charging session (same start/user/station): id {session.Id}, {session.ChargedKwh} kWh"
                    });
                }
                else
                {
                    session = new ChargingSession
                    {
                        Start = request.Start,
                        End = request.End,
                        UserId = request.UserId,
                        StationId = stationId,
                        StationSerialNumber = stationSerial,
                        Connector = connector,
                        ChargingStationName = stationName,
                        ChargedKwh = request.ChargedKwh
                    };
                    _context.ChargingSessions.Add(session);
                    _context.EventLogs.Add(new EventLog
                    {
                        Timestamp = DateTime.UtcNow,
                        UserName = User.Identity?.Name ?? "Unknown",
                        Action = $"Manually added charging session: User {session.UserId}, Station {session.StationId}, {session.ChargedKwh} kWh"
                    });
                }

                await _context.SaveChangesAsync();

                if (replacedExisting)
                    _logger.LogInformation("Admin replaced charging session {SessionId} (duplicate start/user/station)", session.Id);
                else
                    _logger.LogInformation("Admin manually added charging session {SessionId}", session.Id);

                return Ok(ToSessionResponse(session));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error adding charging session manually");
                return BadRequest($"Failed to add session: {ex.Message}");
            }
        }

        [Authorize(Roles = "Admin")]
        [HttpPost("upload")]
        public async Task<IActionResult> UploadCsv(IFormFile file)
        {
            try
            {
                var token = Request.Headers.Authorization.ToString().Replace("Bearer ", "");
                var userIdString = _jwtService.ValidateToken(token);
                
                if (userIdString == null || !int.TryParse(userIdString, out int currentUserId))
                {
                    _logger.LogWarning("Unauthorized attempt to upload charging sessions");
                    return Unauthorized();
                }

                if (file == null || file.Length == 0)
                {
                    return BadRequest("No file uploaded");
                }

                if (!file.FileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
                {
                    return BadRequest("File must be a CSV");
                }

                _logger.LogInformation("User {UserId} uploading CSV file: {FileName}", currentUserId, file.FileName);

                await using var uploadStream = file.OpenReadStream();
                using var buffer = new MemoryStream();
                await uploadStream.CopyToAsync(buffer);
                var csvBytes = buffer.ToArray();
                var headerLine = ReadFirstNonEmptyLine(csvBytes);
                if (!headerLine.Contains("Station serial number", StringComparison.OrdinalIgnoreCase))
                {
                    return BadRequest(
                        "CSV must be a charging station export. The first row must include the column \"Station serial number\" " +
                        "(see Backend/Assets/sample-charging-export.csv).");
                }

                using var readStream = new MemoryStream(csvBytes);
                using var reader = new StreamReader(readStream, System.Text.Encoding.UTF8, detectEncodingFromByteOrderMarks: true);

                var parsed = await ParseExportFormatCsvAsync(reader);
                var newSessions = parsed.Sessions;
                var skippedCount = parsed.Skipped;
                var errorCount = parsed.Errors;

                var addedCount = 0;
                var updatedCount = 0;
                if (newSessions.Any())
                {
                    (addedCount, updatedCount) = await UpsertChargingSessionsFromImportAsync(newSessions);
                    await UpsertChargingProfilesFromImportAsync(parsed.ChargingUserMeta);
                }

                _logger.LogInformation(
                    "CSV upload completed. Added: {Added}, Updated: {Updated}, Skipped: {Skipped}, Errors: {Errors}",
                    addedCount, updatedCount, skippedCount, errorCount);

                _context.EventLogs.Add(new EventLog
                {
                    Timestamp = DateTime.UtcNow,
                    UserName = User.Identity?.Name ?? "Unknown",
                    Action = $"Upload '{file.FileName}': {addedCount} added, {updatedCount} updated (same start/user/station as existing), {skippedCount} duplicate line(s) in file skipped"
                });
                await _context.SaveChangesAsync();

                return Ok(new
                {
                    message = "Upload completed",
                    added = addedCount,
                    updated = updatedCount,
                    skipped = skippedCount,
                    errors = errorCount,
                    total = addedCount + updatedCount + skippedCount + errorCount
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error uploading charging sessions CSV");
                return BadRequest($"Upload failed: {ex.Message}");
            }
        }

        [HttpGet("available-months")]
        public async Task<IActionResult> GetAvailableMonths()
        {
            try
            {
                var token = Request.Headers.Authorization.ToString().Replace("Bearer ", "");
                var userIdString = _jwtService.ValidateToken(token);
                
                if (userIdString == null || !int.TryParse(userIdString, out int currentUserId))
                {
                    _logger.LogWarning("Unauthorized attempt to access available months");
                    return Unauthorized();
                }

                // Get distinct year-month combinations from sessions
                var availableMonths = await _context.ChargingSessions
                    .Select(s => new { 
                        Year = s.Start.Year, 
                        Month = s.Start.Month 
                    })
                    .Distinct()
                    .OrderByDescending(x => x.Year)
                    .ThenByDescending(x => x.Month)
                    .ToListAsync();

                var monthsData = availableMonths.Select(m => new
                {
                    value = $"{m.Year}-{m.Month:D2}",
                    label = new DateTime(m.Year, m.Month, 1).ToString("MMMM yyyy"),
                    year = m.Year,
                    month = m.Month
                }).ToList();

                _logger.LogInformation("Retrieved {Count} available months for sessions", monthsData.Count);

                return Ok(monthsData);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving available months");
                return BadRequest($"Failed to retrieve available months: {ex.Message}");
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetAllSessions(int page = 1, int pageSize = 50, int? year = null, int? month = null)
        {
            try
            {
                var token = Request.Headers.Authorization.ToString().Replace("Bearer ", "");
                var userIdString = _jwtService.ValidateToken(token);
                
                if (userIdString == null || !int.TryParse(userIdString, out int currentUserId))
                {
                    _logger.LogWarning("Unauthorized attempt to access charging sessions");
                    return Unauthorized();
                }

                // Start with base query
                var query = _context.ChargingSessions.AsQueryable();

                // Apply month/year filter if provided
                if (year.HasValue && month.HasValue)
                {
                    // Validate month range
                    if (month.Value < 1 || month.Value > 12)
                    {
                        _logger.LogWarning("Invalid month parameter: {Month}", month.Value);
                        return BadRequest("Month must be between 1 and 12");
                    }

                    var startDate = new DateTime(year.Value, month.Value, 1);
                    var endDate = startDate.AddMonths(1);
                    
                    query = query.Where(s => s.Start >= startDate && s.Start < endDate);
                    
                    _logger.LogInformation("Filtering sessions for {Year}-{Month:D2}. Date range: {StartDate} to {EndDate}", 
                        year.Value, month.Value, startDate, endDate);
                }

                var totalSessions = await query.CountAsync();
                
                _logger.LogInformation("Found {TotalSessions} sessions matching filter criteria", totalSessions);
                
                var sessions = await query
                    .OrderByDescending(s => s.Start)
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .ToListAsync();

                var chargingUserIds = sessions.Select(s => s.UserId).Distinct().ToList();
                var profiles = await _context.ChargingUserProfiles
                    .AsNoTracking()
                    .Where(p => chargingUserIds.Contains(p.Id))
                    .ToDictionaryAsync(p => p.Id, p => p.DisplayName);
                var linkedByChargingId = await _context.Users
                    .AsNoTracking()
                    .Where(u => u.ChargingUserId != null && chargingUserIds.Contains(u.ChargingUserId.Value))
                    .ToDictionaryAsync(u => u.ChargingUserId!.Value, u => u.Name);

                string? ChargingUserLabel(int uid)
                {
                    if (profiles.TryGetValue(uid, out var dn) && !string.IsNullOrWhiteSpace(dn))
                        return dn.Trim();
                    if (linkedByChargingId.TryGetValue(uid, out var appName) && !string.IsNullOrWhiteSpace(appName))
                        return appName;
                    return null;
                }

                var sessionData = sessions.Select(s => ToSessionResponse(s, ChargingUserLabel(s.UserId))).ToList();

                _logger.LogInformation("Retrieved {Count} charging sessions for user {UserId} (page {Page})", 
                    sessionData.Count, currentUserId, page);

                return Ok(new
                {
                    sessions = sessionData,
                    pagination = new
                    {
                        page,
                        pageSize,
                        totalSessions,
                        totalPages = (int)Math.Ceiling((double)totalSessions / pageSize)
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving charging sessions");
                return BadRequest($"Failed to retrieve sessions: {ex.Message}");
            }
        }

        [Authorize(Roles = "Admin")]
        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateSession(int id, [FromBody] UpdateSessionRequest request)
        {
            try
            {
                if (request.End.HasValue && request.End.Value < request.Start)
                    return BadRequest("End time cannot be before start time");

                if (request.ChargedKwh < 0)
                    return BadRequest("ChargedKwh must be non-negative");

                var session = await _context.ChargingSessions.FindAsync(id);
                if (session == null)
                    return NotFound($"Session {id} not found");

                if (!TryResolveStationForWrite(request, out var stationId, out var stationSerial, out var connector, out var stationName, out var stationError))
                    return BadRequest(stationError);

                session.Start = request.Start;
                session.End = request.End;
                session.UserId = request.UserId;
                session.StationId = stationId;
                session.StationSerialNumber = stationSerial;
                session.Connector = connector;
                session.ChargingStationName = stationName;
                session.ChargedKwh = request.ChargedKwh;

                _context.EventLogs.Add(new EventLog
                {
                    Timestamp = DateTime.UtcNow,
                    UserName = User.Identity?.Name ?? "Unknown",
                    Action = $"Updated charging session {id}: User {session.UserId}, Station {session.StationId}, {session.ChargedKwh} kWh"
                });

                await _context.SaveChangesAsync();

                _logger.LogInformation("Admin updated charging session {SessionId}", id);

                return Ok(ToSessionResponse(session));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating charging session {SessionId}", id);
                return BadRequest($"Failed to update session: {ex.Message}");
            }
        }

        [Authorize(Roles = "Admin")]
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteSession(int id)
        {
            try
            {
                var session = await _context.ChargingSessions.FindAsync(id);
                if (session == null)
                    return NotFound($"Session {id} not found");

                _context.ChargingSessions.Remove(session);

                _context.EventLogs.Add(new EventLog
                {
                    Timestamp = DateTime.UtcNow,
                    UserName = User.Identity?.Name ?? "Unknown",
                    Action = $"Deleted charging session {id}: User {session.UserId}, Station {session.StationId}, {session.ChargedKwh} kWh"
                });

                await _context.SaveChangesAsync();

                _logger.LogInformation("Admin deleted charging session {SessionId}", id);

                return Ok(new { message = $"Session {id} deleted" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting charging session {SessionId}", id);
                return BadRequest($"Failed to delete session: {ex.Message}");
            }
        }

        private sealed record ChargingUserImportMeta(string RfidTokenKey, string? DisplayNameFromBrackets);

        private sealed record ExportParseResult(
            List<ChargingSession> Sessions,
            int Skipped,
            int Errors,
            Dictionary<int, ChargingUserImportMeta> ChargingUserMeta);

        private async Task UpsertChargingProfilesFromImportAsync(Dictionary<int, ChargingUserImportMeta> metaByUserId)
        {
            foreach (var (userId, meta) in metaByUserId)
            {
                var profile = await _context.ChargingUserProfiles.FindAsync(userId);
                if (profile == null)
                {
                    _context.ChargingUserProfiles.Add(new ChargingUserProfile
                    {
                        Id = userId,
                        RfidTokenKey = meta.RfidTokenKey,
                        DisplayName = string.IsNullOrWhiteSpace(meta.DisplayNameFromBrackets)
                            ? null
                            : meta.DisplayNameFromBrackets.Trim()
                    });
                    continue;
                }

                if (string.IsNullOrWhiteSpace(profile.RfidTokenKey) && !string.IsNullOrWhiteSpace(meta.RfidTokenKey))
                    profile.RfidTokenKey = meta.RfidTokenKey.Trim();
                if (string.IsNullOrWhiteSpace(profile.DisplayName) && !string.IsNullOrWhiteSpace(meta.DisplayNameFromBrackets))
                    profile.DisplayName = meta.DisplayNameFromBrackets.Trim();
            }

            await _context.SaveChangesAsync();
        }

        /// <summary>
        /// Inserts new sessions or updates existing rows that share the unique key (Start, UserId, StationId).
        /// </summary>
        private async Task<(int Added, int Updated)> UpsertChargingSessionsFromImportAsync(List<ChargingSession> sessions)
        {
            if (sessions.Count == 0)
                return (0, 0);

            var keysSet = sessions.Select(s => (s.Start, s.UserId, s.StationId)).ToHashSet();
            var stationIds = sessions.Select(s => s.StationId).Distinct().ToList();
            var userIds = sessions.Select(s => s.UserId).Distinct().ToList();
            var minStart = sessions.Min(s => s.Start);
            var maxStart = sessions.Max(s => s.Start);

            var candidates = await _context.ChargingSessions
                .Where(s => stationIds.Contains(s.StationId) && userIds.Contains(s.UserId) &&
                            s.Start >= minStart && s.Start <= maxStart)
                .ToListAsync();

            var existingByKey = candidates
                .Where(s => keysSet.Contains((s.Start, s.UserId, s.StationId)))
                .GroupBy(s => (s.Start, s.UserId, s.StationId))
                .ToDictionary(g => g.Key, g => g.First());

            var added = 0;
            var updated = 0;
            foreach (var session in sessions)
            {
                var key = (session.Start, session.UserId, session.StationId);
                if (existingByKey.TryGetValue(key, out var row))
                {
                    row.End = session.End;
                    row.ChargedKwh = session.ChargedKwh;
                    row.StationSerialNumber = session.StationSerialNumber;
                    row.Connector = session.Connector;
                    row.ChargingStationName = session.ChargingStationName;
                    updated++;
                }
                else
                {
                    _context.ChargingSessions.Add(session);
                    existingByKey[key] = session;
                    added++;
                }
            }

            await _context.SaveChangesAsync();
            return (added, updated);
        }

        private async Task<ExportParseResult> ParseExportFormatCsvAsync(StreamReader textReader)
        {
            var newSessions = new List<ChargingSession>();
            var skipped = 0;
            var errors = 0;
            var seenInFile = new HashSet<(DateTime Start, int UserId, int StationId)>();
            var chargingUserMeta = new Dictionary<int, ChargingUserImportMeta>();

            var inv = CultureInfo.InvariantCulture;
            var cfg = new CsvConfiguration(inv)
            {
                HeaderValidated = null,
                MissingFieldFound = null,
                PrepareHeaderForMatch = args => args.Header.Trim(),
            };
            using var csv = new CsvReader(textReader, cfg);

            await csv.ReadAsync();
            csv.ReadHeader();

            while (await csv.ReadAsync())
            {
                try
                {
                    var record = csv.GetRecord<ChargingSessionExportCsvRecord>();
                    if (record == null || string.IsNullOrWhiteSpace(record.Token))
                    {
                        errors++;
                        continue;
                    }

                    if (!TryParseKwh(record.Kwh, out var kwh) || kwh < 0)
                    {
                        errors++;
                        continue;
                    }

                    if (!DateTime.TryParse(record.From, inv, DateTimeStyles.AllowWhiteSpaces, out var start))
                    {
                        errors++;
                        continue;
                    }

                    DateTime? end = null;
                    var toRaw = (record.To ?? string.Empty).Trim();
                    if (toRaw.Length > 0)
                    {
                        if (!DateTime.TryParse(toRaw, inv, DateTimeStyles.AllowWhiteSpaces, out var endParsed))
                        {
                            errors++;
                            continue;
                        }

                        end = endParsed;
                    }

                    if (end.HasValue && end.Value < start)
                    {
                        errors++;
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(record.StationSerialNumber))
                    {
                        errors++;
                        continue;
                    }

                    var tokenKey = ChargingImportIds.NormalizeTokenKey(record.Token);
                    if (string.IsNullOrEmpty(tokenKey))
                    {
                        errors++;
                        continue;
                    }

                    var userId = ChargingImportIds.ChargingUserIdFromTokenKey(tokenKey);
                    var bracketName = ChargingImportIds.ParseDisplayNameInParentheses(record.Token);
                    chargingUserMeta[userId] = new ChargingUserImportMeta(tokenKey, bracketName);

                    var serialNorm = record.StationSerialNumber.Trim().Trim('"');
                    var connectorNorm = (record.Connector ?? string.Empty).Trim().Trim('"');
                    var stationId = ChargingPointIds.ChargingPointIdFromSerialAndConnector(serialNorm, connectorNorm);
                    var stationDisplayName = string.IsNullOrWhiteSpace(record.ChargingStationName)
                        ? null
                        : record.ChargingStationName.Trim();

                    var session = new ChargingSession
                    {
                        Start = start,
                        End = end,
                        UserId = userId,
                        StationId = stationId,
                        StationSerialNumber = serialNorm,
                        Connector = string.IsNullOrEmpty(connectorNorm) ? null : connectorNorm,
                        ChargingStationName = stationDisplayName,
                        ChargedKwh = kwh
                    };

                    var dedupeKey = (session.Start, session.UserId, session.StationId);
                    if (!seenInFile.Add(dedupeKey))
                    {
                        skipped++;
                        continue;
                    }

                    newSessions.Add(session);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error parsing export-format CSV row");
                    errors++;
                }
            }

            return new ExportParseResult(newSessions, skipped, errors, chargingUserMeta);
        }

        private static string ReadFirstNonEmptyLine(byte[] csvBytes)
        {
            var text = System.Text.Encoding.UTF8.GetString(csvBytes);
            foreach (var segment in text.Split(["\r\n", "\n", "\r"], StringSplitOptions.None))
            {
                var t = segment.Trim();
                if (t.Length > 0)
                    return t;
            }
            return string.Empty;
        }

        private static object ToSessionResponse(ChargingSession s, string? chargingUserDisplayName = null) => new
        {
            id = s.Id,
            start = s.Start,
            end = s.End,
            userId = s.UserId,
            chargingUserDisplayName,
            stationId = s.StationId,
            stationSerialNumber = s.StationSerialNumber,
            connector = s.Connector,
            chargingStationName = s.ChargingStationName,
            chargedKwh = s.ChargedKwh,
            duration = s.End.HasValue ? (int?)(int)(s.End.Value - s.Start).TotalMinutes : null,
            createdAt = s.CreatedAt
        };

        private static bool TryResolveStationForWrite(
            AddSessionRequest request,
            out int resolvedStationId,
            out string? stationSerial,
            out string? connector,
            out string? chargingStationName,
            out string? error)
            => TryResolveStationForWrite(
                request.StationSerialNumber,
                request.Connector,
                request.ChargingStationName,
                request.StationId,
                out resolvedStationId,
                out stationSerial,
                out connector,
                out chargingStationName,
                out error);

        private static bool TryResolveStationForWrite(
            UpdateSessionRequest request,
            out int resolvedStationId,
            out string? stationSerial,
            out string? connector,
            out string? chargingStationName,
            out string? error)
            => TryResolveStationForWrite(
                request.StationSerialNumber,
                request.Connector,
                request.ChargingStationName,
                request.StationId,
                out resolvedStationId,
                out stationSerial,
                out connector,
                out chargingStationName,
                out error);

        private static bool TryResolveStationForWrite(
            string? stationSerialNumber,
            string? connectorRaw,
            string? chargingStationNameRaw,
            int legacyStationId,
            out int resolvedStationId,
            out string? stationSerial,
            out string? connector,
            out string? chargingStationName,
            out string? error)
        {
            error = null;
            stationSerial = null;
            connector = null;
            chargingStationName = string.IsNullOrWhiteSpace(chargingStationNameRaw)
                ? null
                : chargingStationNameRaw.Trim();

            var hasSerial = !string.IsNullOrWhiteSpace(stationSerialNumber);
            var hasConnector = !string.IsNullOrWhiteSpace(connectorRaw);
            if (hasSerial && hasConnector)
            {
                stationSerial = stationSerialNumber!.Trim().Trim('"');
                connector = connectorRaw!.Trim().Trim('"');
                resolvedStationId = ChargingPointIds.ChargingPointIdFromSerialAndConnector(stationSerial, connector);
                return true;
            }

            if (legacyStationId >= 1)
            {
                resolvedStationId = legacyStationId;
                stationSerial = hasSerial ? stationSerialNumber!.Trim().Trim('"') : null;
                connector = hasConnector ? connectorRaw!.Trim().Trim('"') : null;
                return true;
            }

            error = "Provide station serial number and connector together, or a positive station id (legacy).";
            resolvedStationId = default;
            return false;
        }

        private static bool TryParseKwh(string raw, out decimal kwh)
        {
            kwh = 0;
            var s = raw.Trim().Trim('"');
            if (decimal.TryParse(s, NumberStyles.Number, CultureInfo.GetCultureInfo("nl-NL"), out kwh))
                return true;
            return decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out kwh);
        }
    }

    public class AddSessionRequest
    {
        public DateTime Start { get; set; }
        /// <summary>Omit or null while the session is still ongoing.</summary>
        public DateTime? End { get; set; }
        public int UserId { get; set; }
        /// <summary>Legacy internal id; ignored when <see cref="StationSerialNumber"/> and <see cref="Connector"/> are both set.</summary>
        public int StationId { get; set; }
        public decimal ChargedKwh { get; set; }
        public string? StationSerialNumber { get; set; }
        public string? Connector { get; set; }
        public string? ChargingStationName { get; set; }
    }

    public class UpdateSessionRequest
    {
        public DateTime Start { get; set; }
        /// <summary>Null clears the end time (session ongoing).</summary>
        public DateTime? End { get; set; }
        public int UserId { get; set; }
        public int StationId { get; set; }
        public decimal ChargedKwh { get; set; }
        public string? StationSerialNumber { get; set; }
        public string? Connector { get; set; }
        public string? ChargingStationName { get; set; }
    }

    /// <summary>
    /// Zonne / export CSV (e.g. station portal): headers with spaces and Dutch-style kWh decimals.
    /// </summary>
    public class ChargingSessionExportCsvRecord
    {
        [Name("Station serial number")]
        public string StationSerialNumber { get; set; } = string.Empty;

        [Name("Charging station name")]
        public string ChargingStationName { get; set; } = string.Empty;

        [Name("From")]
        public string From { get; set; } = string.Empty;

        [Name("To")]
        public string To { get; set; } = string.Empty;

        [Name("Duration [h:mm]")]
        public string Duration { get; set; } = string.Empty;

        [Name("kWh")]
        public string Kwh { get; set; } = string.Empty;

        [Name("Connector")]
        public string Connector { get; set; } = string.Empty;

        [Name("Token")]
        public string Token { get; set; } = string.Empty;
    }
}