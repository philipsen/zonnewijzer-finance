using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Backend.Data;
using Backend.Models;
using Backend.Services;

namespace Backend.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class InvoicesController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly IJwtService _jwtService;
        private readonly IPdfService _pdfService;
        private readonly ILogger<InvoicesController> _logger;

        public InvoicesController(ApplicationDbContext context, IJwtService jwtService, IPdfService pdfService, ILogger<InvoicesController> logger)
        {
            _context = context;
            _jwtService = jwtService;
            _pdfService = pdfService;
            _logger = logger;
        }

        [HttpGet]
        public async Task<IActionResult> GetInvoicesGrid()
        {
            try
            {
                var token = Request.Headers.Authorization.ToString().Replace("Bearer ", "");
                var userIdString = _jwtService.ValidateToken(token);
                
                if (userIdString == null || !int.TryParse(userIdString, out int currentUserId))
                {
                    _logger.LogWarning("Unauthorized attempt to access invoices");
                    return Unauthorized();
                }

                _logger.LogInformation("User {UserId} requesting invoices grid", currentUserId);

                // Get unique users from charging sessions instead of registered users
                var chargingSessionUsers = await _context.ChargingSessions
                    .Select(cs => cs.UserId)
                    .Distinct()
                    .OrderBy(uid => uid)
                    .ToListAsync();

                // Look up real user names via ChargingUserId links
                var linkedUsers = await _context.Users
                    .Where(u => u.ChargingUserId != null && chargingSessionUsers.Contains(u.ChargingUserId!.Value))
                    .Select(u => new { u.ChargingUserId, u.Name, u.Email })
                    .ToListAsync();

                // Load display names for unlinked charging users
                var profiles = await _context.ChargingUserProfiles
                    .Where(p => chargingSessionUsers.Contains(p.Id))
                    .ToListAsync();

                // Create user objects for grid display
                var users = chargingSessionUsers.Select(uid => {
                    var linked = linkedUsers.FirstOrDefault(u => u.ChargingUserId == uid);
                    var profile = profiles.FirstOrDefault(p => p.Id == uid);
                    return new {
                        Id = uid,
                        Name = linked?.Name ?? profile?.DisplayName ?? $"Charging User {uid}",
                        Email = linked?.Email ?? $"user{uid}@charging.local"
                    };
                }).ToList();

                // Get months that have charging sessions instead of all months
                var rawMonths = await _context.ChargingSessions
                    .Select(cs => new { cs.Start.Year, cs.Start.Month })
                    .Distinct()
                    .OrderBy(d => d.Year)
                    .ThenBy(d => d.Month)
                    .Select(d => new { d.Year, d.Month })
                    .ToListAsync();

                // Pre-load all charging sessions for the relevant users/months to avoid N+1 queries
                var allSessions = await _context.ChargingSessions
                    .Where(cs => chargingSessionUsers.Contains(cs.UserId))
                    .ToListAsync();

                // Pre-load all energy prices
                var energyPrices = await _context.EnergyPrices
                    .OrderByDescending(p => p.Year).ThenByDescending(p => p.Month)
                    .ToListAsync();

                // Helper: find last known price on or before a given year/month
                decimal GetRate(int year, int month) =>
                    energyPrices
                        .Where(p => p.Year < year || (p.Year == year && p.Month <= month))
                        .Select(p => (decimal?)p.PricePerKwh)
                        .FirstOrDefault() ?? 0.22m;

                var months = rawMonths.Select(d => new {
                    Year = d.Year,
                    Month = d.Month,
                    Name = $"{new DateTime(d.Year, d.Month, 1):MMM yyyy}",
                    RatePerKwh = Math.Round(GetRate(d.Year, d.Month), 2)
                }).ToList();

                // Create grid data with charging session info instead of stored invoices
                var gridData = users.Select(user => new
                {
                    User = user,
                    Invoices = months.Select(m => new
                    {
                        Year = m.Year,
                        Month = m.Month,
                        MonthName = m.Name,
                        HasChargingSessions = allSessions
                            .Any(cs => cs.UserId == user.Id &&
                                      cs.Start.Year == m.Year &&
                                      cs.Start.Month == m.Month),
                        TotalKwh = Math.Round(allSessions
                            .Where(cs => cs.UserId == user.Id &&
                                        cs.Start.Year == m.Year &&
                                        cs.Start.Month == m.Month)
                            .Sum(cs => cs.ChargedKwh), 2),
                        RatePerKwh = GetRate(m.Year, m.Month),
                        TotalAmount = Math.Round(allSessions
                            .Where(cs => cs.UserId == user.Id &&
                                        cs.Start.Year == m.Year &&
                                        cs.Start.Month == m.Month)
                            .Sum(cs => cs.ChargedKwh) * GetRate(m.Year, m.Month), 2)
                    }).ToList()
                }).ToList();

                return Ok(new { Users = users, Months = months, GridData = gridData });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving invoices grid");
                return BadRequest($"Failed to retrieve invoices: {ex.Message}");
            }
        }

        [HttpGet("{userId}/{year}/{month}")]
        public async Task<IActionResult> GenerateInvoicePdf(int userId, int year, int month)
        {
            try
            {
                var token = Request.Headers.Authorization.ToString().Replace("Bearer ", "");
                var userIdString = _jwtService.ValidateToken(token);
                
                if (userIdString == null || !int.TryParse(userIdString, out int currentUserId))
                {
                    return Unauthorized();
                }

                _logger.LogInformation("Generating on-the-fly invoice for user {UserId}, {Year}-{Month}", userId, year, month);

                // Get charging sessions for this user and month
                var chargingSessions = await _context.ChargingSessions
                    .Where(cs => cs.UserId == userId && 
                                cs.Start.Year == year && 
                                cs.Start.Month == month)
                    .ToListAsync();

                if (!chargingSessions.Any())
                {
                    return NotFound("No charging sessions found for this user and month");
                }

                // Calculate total kWh and amount
                var totalKwh = chargingSessions.Sum(cs => cs.ChargedKwh);

                // Look up last known energy price on or before this month, fall back to 0.22
                var ratePerKwh = await _context.EnergyPrices
                    .Where(p => p.Year < year || (p.Year == year && p.Month <= month))
                    .OrderByDescending(p => p.Year).ThenByDescending(p => p.Month)
                    .Select(p => (decimal?)p.PricePerKwh)
                    .FirstOrDefaultAsync() ?? 0.22m;

                var totalAmount = totalKwh * ratePerKwh;

                // Resolve real user name if linked
                var linkedUser = await _context.Users
                    .FirstOrDefaultAsync(u => u.ChargingUserId == userId);
                var profile = await _context.ChargingUserProfiles
                    .FirstOrDefaultAsync(p => p.Id == userId);
                var userName = linkedUser?.Name ?? profile?.DisplayName ?? $"Charging User {userId}";

                var invoiceData = new InvoiceData
                {
                    UserId = userId,
                    UserName = userName,
                    Street = profile?.Street,
                    HouseNumber = profile?.HouseNumber,
                    PostalCode = profile?.PostalCode,
                    PlaceName = profile?.PlaceName,
                    Year = year,
                    Month = month,
                    MonthName = new DateTime(year, month, 1).ToString("MMMM yyyy"),
                    InvoiceNumber = $"INV-{year:D4}-{month:D2}-{userId:D4}",
                    TotalKwh = Math.Round(totalKwh, 2),
                    RatePerKwh = ratePerKwh,
                    TotalAmount = Math.Round(totalAmount, 2),
                    Sessions = chargingSessions.Select(cs => new SessionData
                    {
                        Start = cs.Start,
                        End = cs.End,
                        StationId = cs.StationId,
                        ChargedKwh = Math.Round(cs.ChargedKwh, 2),
                        Amount = Math.Round(cs.ChargedKwh * ratePerKwh, 2)
                    }).ToList(),
                    GeneratedAt = DateTime.UtcNow
                };

                // Generate PDF
                var pdfBytes = _pdfService.GenerateInvoicePdf(invoiceData);
                var fileName = $"Invoice_{invoiceData.InvoiceNumber}_{userId}_{year}_{month:D2}.pdf";

                var requester = await _context.Users.FindAsync(currentUserId);
                _context.EventLogs.Add(new EventLog
                {
                    Timestamp = DateTime.UtcNow,
                    UserName = requester?.Name ?? $"User {currentUserId}",
                    Action = $"Invoice download for user {userId} – {invoiceData.MonthName}"
                });
                await _context.SaveChangesAsync();

                return File(pdfBytes, "application/pdf", fileName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating invoice PDF for user {UserId}, {Year}-{Month}", userId, year, month);
                return BadRequest($"Failed to generate invoice: {ex.Message}");
            }
        }
    }
}