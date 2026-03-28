using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
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

                var newSessions = new List<ChargingSession>();
                var skippedCount = 0;
                var errorCount = 0;

                using (var reader = new StreamReader(file.OpenReadStream()))
                using (var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture)
                {
                    HeaderValidated = null,
                    MissingFieldFound = null
                }))
                {
                    var records = csv.GetRecords<ChargingSessionCsvRecord>();

                    foreach (var record in records)
                    {
                        try
                        {
                            var session = new ChargingSession
                            {
                                Start = DateTime.Parse(record.Start),
                                End = DateTime.Parse(record.End),
                                UserId = record.UserId,
                                StationId = record.StationId,
                                ChargedKwh = record.ChargedKwh
                            };

                            // Check for duplicates
                            var exists = await _context.ChargingSessions
                                .AnyAsync(s => s.Start == session.Start && 
                                              s.UserId == session.UserId && 
                                              s.StationId == session.StationId);

                            if (!exists)
                            {
                                newSessions.Add(session);
                            }
                            else
                            {
                                skippedCount++;
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Error parsing CSV record: {Record}", record);
                            errorCount++;
                        }
                    }
                }

                if (newSessions.Any())
                {
                    _context.ChargingSessions.AddRange(newSessions);
                    await _context.SaveChangesAsync();
                }

                _logger.LogInformation("CSV upload completed. Added: {Added}, Skipped: {Skipped}, Errors: {Errors}", 
                    newSessions.Count, skippedCount, errorCount);

                return Ok(new
                {
                    message = "Upload completed",
                    added = newSessions.Count,
                    skipped = skippedCount,
                    errors = errorCount,
                    total = newSessions.Count + skippedCount + errorCount
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

                var sessionData = sessions.Select(s => new
                {
                    id = s.Id,
                    start = s.Start,
                    end = s.End,
                    userId = s.UserId,
                    stationId = s.StationId,
                    chargedKwh = s.ChargedKwh,
                    duration = (int)(s.End - s.Start).TotalMinutes,
                    createdAt = s.CreatedAt
                }).ToList();

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
    }

    public class ChargingSessionCsvRecord
    {
        [Name("start")]
        public string Start { get; set; } = string.Empty;
        
        [Name("end")]
        public string End { get; set; } = string.Empty;
        
        [Name("userid")]
        public int UserId { get; set; }
        
        [Name("stationid")]
        public int StationId { get; set; }
        
        [Name("charged_kwh")]
        public decimal ChargedKwh { get; set; }
    }
}