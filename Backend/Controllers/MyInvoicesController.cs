using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Backend;
using Backend.Data;
using Backend.Models;
using Backend.Services;

namespace Backend.Controllers;

[ApiController]
[Route("api/my-invoices")]
public class MyInvoicesController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly IJwtService _jwtService;
    private readonly IMollieService _mollieService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<MyInvoicesController> _logger;
    private static readonly HashSet<string> FinalPaymentStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "paid",
        "failed",
        "canceled"
    };

    public MyInvoicesController(ApplicationDbContext context, IJwtService jwtService, IMollieService mollieService, IConfiguration configuration, ILogger<MyInvoicesController> logger)
    {
        _context = context;
        _jwtService = jwtService;
        _mollieService = mollieService;
        _configuration = configuration;
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

            var myCardIds = await UserChargeCardQuery.GetChargeCardIdsForUserAsync(_context, currentUserId);
            if (myCardIds.Count == 0)
                return Ok(new
                {
                    userName = currentUser.Name,
                    appUserId = currentUserId,
                    chargeCardIds = myCardIds,
                    months = new List<object>()
                });

            var allSessions = await _context.ChargingSessions
                .Where(cs => myCardIds.Contains(cs.ChargeCardId))
                .ToListAsync();

            var monthKeys = allSessions
                .Where(cs => cs.ToTimestamp.HasValue)
                .Select(cs =>
                {
                    var (y, mo) = BrusselsTime.BrusselsYearMonthFromUnixMilliseconds(cs.ToTimestamp!.Value);
                    return new { Year = y, Month = mo };
                })
                .Distinct()
                .OrderByDescending(x => x.Year)
                .ThenByDescending(x => x.Month)
                .ToList();

            var allPayments = await _context.MolliePayments
                .Where(p => p.AppUserId == currentUserId)
                .ToListAsync();

            var stale = allPayments.Where(p => !FinalPaymentStatuses.Contains(p.Status)).ToList();
            foreach (var p in stale)
            {
                try
                {
                    var latest = await _mollieService.GetPaymentStatusAsync(p.MolliePaymentId);
                    if (latest != p.Status)
                    {
                        p.Status = latest;
                        p.UpdatedAt = DateTime.UtcNow;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not refresh Mollie status for {PaymentId}", p.MolliePaymentId);
                }
            }

            if (stale.Any(p => p.UpdatedAt.HasValue))
                await _context.SaveChangesAsync();

            var energyPrices = await _context.EnergyPrices
                .OrderByDescending(p => p.Year).ThenByDescending(p => p.Month)
                .ToListAsync();

            decimal GetRate(int year, int month) =>
                energyPrices
                    .Where(p => p.Year < year || (p.Year == year && p.Month <= month))
                    .Select(p => (decimal?)p.PricePerKwh)
                    .FirstOrDefault() ?? 0.22m;

            var result = monthKeys.Select(m =>
            {
                var sessions = allSessions
                    .Where(cs =>
                        myCardIds.Contains(cs.ChargeCardId) &&
                        BrusselsTime.IsEndedInBrusselsMonth(cs.ToTimestamp, m.Year, m.Month))
                    .ToList();
                var rate = GetRate(m.Year, m.Month);
                var totalKwh = Math.Round(sessions.Sum(ChargingSessionEnergy.ChargedKwh), 2);
                var totalAmount = Math.Round(totalKwh * rate, 2);
                var payment = allPayments.FirstOrDefault(p =>
                    p.Year == m.Year && p.Month == m.Month);
                return new
                {
                    appUserId = currentUserId,
                    Year = m.Year,
                    Month = m.Month,
                    MonthName = new DateTime(m.Year, m.Month, 1, 0, 0, 0, DateTimeKind.Unspecified)
                        .ToString("MMMM yyyy"),
                    InvoiceNumber = $"INV-{m.Year:D4}-{m.Month:D2}-U{currentUserId}",
                    TotalKwh = totalKwh,
                    RatePerKwh = rate,
                    TotalAmount = totalAmount,
                    PaymentStatus = payment?.Status,
                    CheckoutUrl = payment?.CheckoutUrl,
                    IsPaid = payment?.Status == "paid",
                    IsOngoingMonth = BrusselsTime.IsCurrentBrusselsMonth(m.Year, m.Month)
                };
            }).ToList();

            return Ok(new
            {
                userName = currentUser.Name,
                appUserId = currentUserId,
                chargeCardIds = myCardIds,
                months = result,
                enableOnlinePaymentLinks = string.Equals(
                    _configuration["Invoices:EnableOnlinePaymentLinks"],
                    "true",
                    StringComparison.OrdinalIgnoreCase)
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving personal invoices");
            return BadRequest($"Failed to retrieve invoices: {ex.Message}");
        }
    }
}
