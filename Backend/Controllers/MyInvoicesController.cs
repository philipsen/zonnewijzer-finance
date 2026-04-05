using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Backend.Data;
using Backend.Models;
using Backend.Services;

namespace Backend.Controllers
{
    [ApiController]
    [Route("api/my-invoices")]
    public class MyInvoicesController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly IJwtService _jwtService;
        private readonly IMollieService _mollieService;
        private readonly ILogger<MyInvoicesController> _logger;

        public MyInvoicesController(ApplicationDbContext context, IJwtService jwtService, IMollieService mollieService, ILogger<MyInvoicesController> logger)
        {
            _context = context;
            _jwtService = jwtService;
            _mollieService = mollieService;
            _logger = logger;
        }

        [HttpGet]
        public async Task<IActionResult> GetMyInvoices()
        {
            try
            {
                var token = Request.Headers.Authorization.ToString().Replace("Bearer ", "");
                var userIdString = _jwtService.ValidateToken(token);

                if (userIdString == null || !int.TryParse(userIdString, out int currentUserId))
                    return Unauthorized();

                var currentUser = await _context.Users.FindAsync(currentUserId);
                if (currentUser == null)
                    return Unauthorized();

                if (currentUser.ChargingUserId == null)
                    return Ok(new { userName = currentUser.Name, months = new List<object>() });

                var chargingUserId = currentUser.ChargingUserId.Value;

                // Get all months with sessions for this user
                var months = await _context.ChargingSessions
                    .Where(cs => cs.UserId == chargingUserId)
                    .Select(cs => new { cs.Start.Year, cs.Start.Month })
                    .Distinct()
                    .OrderByDescending(d => d.Year).ThenByDescending(d => d.Month)
                    .ToListAsync();

                // Load all sessions at once
                var allSessions = await _context.ChargingSessions
                    .Where(cs => cs.UserId == chargingUserId)
                    .ToListAsync();

                // Load all payments for this user
                var allPayments = await _context.MolliePayments
                    .Where(p => p.ChargingUserId == chargingUserId)
                    .ToListAsync();

                // Refresh open/pending statuses from Mollie
                var stale = allPayments.Where(p => p.Status == "open" || p.Status == "pending").ToList();
                foreach (var p in stale)
                {
                    try
                    {
                        var latest = await _mollieService.GetPaymentStatusAsync(p.MolliePaymentId);
                        if (latest != p.Status) { p.Status = latest; p.UpdatedAt = DateTime.UtcNow; }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Could not refresh Mollie status for {PaymentId}", p.MolliePaymentId);
                    }
                }
                if (stale.Any(p => p.UpdatedAt.HasValue))
                    await _context.SaveChangesAsync();

                // Load energy prices for rate lookup
                var energyPrices = await _context.EnergyPrices
                    .OrderByDescending(p => p.Year).ThenByDescending(p => p.Month)
                    .ToListAsync();

                decimal GetRate(int year, int month) =>
                    energyPrices
                        .Where(p => p.Year < year || (p.Year == year && p.Month <= month))
                        .Select(p => (decimal?)p.PricePerKwh)
                        .FirstOrDefault() ?? 0.22m;

                var result = months.Select(m =>
                {
                    var sessions = allSessions
                        .Where(cs => cs.Start.Year == m.Year && cs.Start.Month == m.Month)
                        .ToList();
                    var rate = GetRate(m.Year, m.Month);
                    var totalKwh = Math.Round(sessions.Sum(cs => cs.ChargedKwh), 2);
                    var totalAmount = Math.Round(totalKwh * rate, 2);
                    var payment = allPayments.FirstOrDefault(p => p.Year == m.Year && p.Month == m.Month);
                    return new
                    {
                        Year = m.Year,
                        Month = m.Month,
                        MonthName = new DateTime(m.Year, m.Month, 1).ToString("MMMM yyyy"),
                        InvoiceNumber = $"INV-{m.Year:D4}-{m.Month:D2}-{chargingUserId:D4}",
                        TotalKwh = totalKwh,
                        RatePerKwh = rate,
                        TotalAmount = totalAmount,
                        PaymentStatus = payment?.Status,
                        CheckoutUrl = payment?.CheckoutUrl,
                        IsPaid = payment?.Status == "paid"
                    };
                }).ToList();

                return Ok(new { userName = currentUser.Name, chargingUserId, months = result });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving personal invoices");
                return BadRequest($"Failed to retrieve invoices: {ex.Message}");
            }
        }
    }
}
