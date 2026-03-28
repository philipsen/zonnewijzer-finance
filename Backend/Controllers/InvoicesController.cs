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

                // Create user objects for grid display
                var users = chargingSessionUsers.Select(uid => new { 
                    Id = uid, 
                    Name = $"Charging User {uid}", 
                    Email = $"user{uid}@charging.local" 
                }).ToList();

                // Get months that have charging sessions instead of all months
                var months = await _context.ChargingSessions
                    .Select(cs => new { cs.Start.Year, cs.Start.Month })
                    .Distinct()
                    .OrderBy(d => d.Year)
                    .ThenBy(d => d.Month)
                    .Select(d => new { 
                        Year = d.Year, 
                        Month = d.Month, 
                        Name = $"{new DateTime(d.Year, d.Month, 1):MMM yyyy}" 
                    })
                    .ToListAsync();

                // Create grid data with charging session info instead of stored invoices
                var gridData = users.Select(user => new
                {
                    User = user,
                    Invoices = months.Select(m => new
                    {
                        Year = m.Year,
                        Month = m.Month,
                        MonthName = m.Name,
                        HasChargingSessions = _context.ChargingSessions
                            .Any(cs => cs.UserId == user.Id && 
                                      cs.Start.Year == m.Year && 
                                      cs.Start.Month == m.Month)
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
                var ratePerKwh = 0.22m; // €0.22 per kWh
                var totalAmount = totalKwh * ratePerKwh;

                var invoiceData = new InvoiceData
                {
                    UserId = userId,
                    UserName = $"Charging User {userId}",
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