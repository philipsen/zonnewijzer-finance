using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Backend.Contracts;
using Backend.Data;
using Backend.Models;
using Backend.Services;
using Backend.Services.GnuCash;

namespace Backend.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class InvoicesController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly IJwtService _jwtService;
        private readonly IPdfService _pdfService;
        private readonly IMollieService _mollieService;
        private readonly IInvoiceComputationService _invoiceComputation;
        private readonly IGnuCashSyncClient _gnuCashSyncClient;
        private readonly IConfiguration _configuration;
        private readonly ILogger<InvoicesController> _logger;
        private static readonly HashSet<string> FinalPaymentStatuses = new(StringComparer.OrdinalIgnoreCase)
        {
            "paid",
            "failed",
            "canceled"
        };

        public InvoicesController(
            ApplicationDbContext context,
            IJwtService jwtService,
            IPdfService pdfService,
            IMollieService mollieService,
            IInvoiceComputationService invoiceComputation,
            IGnuCashSyncClient gnuCashSyncClient,
            IConfiguration configuration,
            ILogger<InvoicesController> logger)
        {
            _context = context;
            _jwtService = jwtService;
            _pdfService = pdfService;
            _mollieService = mollieService;
            _invoiceComputation = invoiceComputation;
            _gnuCashSyncClient = gnuCashSyncClient;
            _configuration = configuration;
            _logger = logger;
        }

        /// <summary>Admin: insert computed invoices into GnuCash via the Python service (insert-only).</summary>
        [HttpPost("gnucash/insert")]
        public async Task<IActionResult> InsertGnucashInvoices([FromBody] GnuCashInsertBody body, CancellationToken cancellationToken)
        {
            try
            {
                var token = Request.Headers.Authorization.ToString().Replace("Bearer ", "");
                var userIdString = _jwtService.ValidateToken(token);
                if (userIdString == null || !int.TryParse(userIdString, out int currentUserId))
                    return Unauthorized();

                var requestingUser = await _context.Users.FirstOrDefaultAsync(u => u.Id == currentUserId, cancellationToken);
                if (requestingUser == null || requestingUser.Role != UserRole.Admin)
                    return Forbid();

                if (body.Month is < 1 or > 12 || body.Year < 2000 || body.Year > 2100)
                    return BadRequest("Invalid year or month.");

                if (string.IsNullOrWhiteSpace(_configuration["GnuCashSync:BaseUrl"]))
                    return BadRequest("GnuCash sync is not configured (GnuCashSync:BaseUrl).");

                var userIds = await _invoiceComputation.GetChargingUserIdsForMonthAsync(body.Year, body.Month, body.ChargingUserId, cancellationToken);
                var linkedEmails = await _context.Users
                    .Where(u => u.ChargingUserId != null && userIds.Contains(u.ChargingUserId!.Value))
                    .Select(u => new { u.ChargingUserId, u.Email })
                    .ToListAsync(cancellationToken);

                var invoices = new List<GnuCashInvoiceDto>();
                foreach (var uid in userIds)
                {
                    var data = await _invoiceComputation.BuildInvoiceDataReadOnlyAsync(uid, body.Year, body.Month, cancellationToken);
                    if (data == null)
                        continue;
                    var email = linkedEmails.FirstOrDefault(x => x.ChargingUserId == uid)?.Email;
                    invoices.Add(GnuCashInvoiceMapper.ToDto(data, email));
                }

                if (invoices.Count == 0)
                    return NotFound("No charging sessions for that period.");

                var correlationId = Request.Headers["X-Correlation-Id"].FirstOrDefault()
                    ?? $"{Guid.NewGuid():N}";
                Response.Headers["X-Correlation-Id"] = correlationId;

                var request = new GnuCashInsertRequest { SchemaVersion = 1, Invoices = invoices };
                var pyResponse = await _gnuCashSyncClient.InsertInvoicesAsync(request, correlationId, body.DryRun, cancellationToken);
                if (pyResponse == null)
                    return BadRequest("GnuCash service did not return a response.");

                _context.EventLogs.Add(new EventLog
                {
                    Timestamp = DateTime.UtcNow,
                    UserName = requestingUser.Name,
                    Action = $"GnuCash insert {body.Year}-{body.Month:D2} (dryRun={body.DryRun}): {pyResponse.Results.Count(r => r.Status == "created")} created, {pyResponse.Results.Count(r => r.Status == "post_verify_failed")} post-verify-failed, {pyResponse.Results.Count(r => r.Status == "amount_verify_failed")} amount-verify-failed, {pyResponse.Results.Count(r => r.Status == "skipped_already_exists")} skipped, {pyResponse.Results.Count(r => r.Status == "dry_run_would_create")} dry-run"
                });
                await _context.SaveChangesAsync(cancellationToken);

                return Ok(pyResponse);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GnuCash insert failed for {Year}-{Month}", body.Year, body.Month);
                return BadRequest($"GnuCash insert failed: {ex.Message}");
            }
        }

        /// <summary>Admin: create the GnuCash customer for a linked app user (customer id CU-{chargingUserId}, same as invoices).</summary>
        [HttpPost("gnucash/customer/{appUserId:int}")]
        public async Task<IActionResult> EnsureGnucashCustomer(int appUserId, [FromQuery] bool dryRun = false, CancellationToken cancellationToken = default)
        {
            try
            {
                var token = Request.Headers.Authorization.ToString().Replace("Bearer ", "");
                var userIdString = _jwtService.ValidateToken(token);
                if (userIdString == null || !int.TryParse(userIdString, out int currentUserId))
                    return Unauthorized();

                var requestingUser = await _context.Users.FirstOrDefaultAsync(u => u.Id == currentUserId, cancellationToken);
                if (requestingUser == null || requestingUser.Role != UserRole.Admin)
                    return Forbid();

                if (string.IsNullOrWhiteSpace(_configuration["GnuCashSync:BaseUrl"]))
                    return BadRequest("GnuCash sync is not configured (GnuCashSync:BaseUrl).");

                var target = await _context.Users.FirstOrDefaultAsync(u => u.Id == appUserId, cancellationToken);
                if (target == null)
                    return NotFound("User not found.");

                if (target.ChargingUserId is null)
                    return BadRequest("This user has no linked charging account. Link a charging user first so the GnuCash customer id matches invoices (CU-…).");

                var profile = await _context.ChargingUserProfiles.FirstOrDefaultAsync(p => p.Id == target.ChargingUserId.Value, cancellationToken);
                var customerId = $"CU-{target.ChargingUserId.Value}";
                var pyRequest = new GnuCashEnsureCustomerRequest
                {
                    CustomerId = customerId,
                    CustomerName = target.Name,
                    Email = target.Email,
                    Street = profile?.Street,
                    HouseNumber = profile?.HouseNumber,
                    PostalCode = profile?.PostalCode,
                    PlaceName = profile?.PlaceName,
                    Currency = "EUR"
                };

                var correlationId = Request.Headers["X-Correlation-Id"].FirstOrDefault()
                    ?? $"{Guid.NewGuid():N}";
                Response.Headers["X-Correlation-Id"] = correlationId;

                var pyResponse = await _gnuCashSyncClient.EnsureCustomerAsync(pyRequest, correlationId, dryRun, cancellationToken);
                if (pyResponse == null)
                    return BadRequest("GnuCash service did not return a response.");

                _context.EventLogs.Add(new EventLog
                {
                    Timestamp = DateTime.UtcNow,
                    UserName = requestingUser.Name,
                    Action = $"GnuCash ensure customer {customerId} for app user {appUserId} (dryRun={dryRun}): {pyResponse.Status}"
                });
                await _context.SaveChangesAsync(cancellationToken);

                return Ok(pyResponse);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GnuCash ensure customer failed for app user {AppUserId}", appUserId);
                return BadRequest($"GnuCash ensure customer failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Admin: remove app-created GnuCash data — invoices with id prefix INV- and customers with id prefix CU-
        /// (via gnucash-sync). Does not touch other GnuCash business objects.
        /// </summary>
        [HttpPost("gnucash/wipe-app")]
        public async Task<IActionResult> WipeGnucashAppBusiness([FromQuery] bool dryRun = false, CancellationToken cancellationToken = default)
        {
            try
            {
                var token = Request.Headers.Authorization.ToString().Replace("Bearer ", "");
                var userIdString = _jwtService.ValidateToken(token);
                if (userIdString == null || !int.TryParse(userIdString, out int currentUserId))
                    return Unauthorized();

                var requestingUser = await _context.Users.FirstOrDefaultAsync(u => u.Id == currentUserId, cancellationToken);
                if (requestingUser == null || requestingUser.Role != UserRole.Admin)
                    return Forbid();

                if (string.IsNullOrWhiteSpace(_configuration["GnuCashSync:BaseUrl"]))
                    return BadRequest("GnuCash sync is not configured (GnuCashSync:BaseUrl).");

                var correlationId = Request.Headers["X-Correlation-Id"].FirstOrDefault()
                    ?? $"{Guid.NewGuid():N}";
                Response.Headers["X-Correlation-Id"] = correlationId;

                var pyResponse = await _gnuCashSyncClient.WipeAppBusinessAsync(correlationId, dryRun, cancellationToken);
                if (pyResponse == null)
                    return BadRequest("GnuCash service did not return a response.");

                _context.EventLogs.Add(new EventLog
                {
                    Timestamp = DateTime.UtcNow,
                    UserName = requestingUser.Name,
                    Action = $"GnuCash wipe-app (dryRun={dryRun}): {pyResponse.InvoicesRemoved} INV-* invoices, {pyResponse.CustomersRemoved} CU-* customers removed"
                });
                await _context.SaveChangesAsync(cancellationToken);

                return Ok(pyResponse);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GnuCash wipe-app failed");
                return BadRequest($"GnuCash wipe-app failed: {ex.Message}");
            }
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

                var requestingUser = await _context.Users.FindAsync(currentUserId);
                if (requestingUser == null || requestingUser.Role != UserRole.Admin)
                    return Forbid();

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

                var invoiceData = await _invoiceComputation.BuildInvoiceDataReadOnlyAsync(userId, year, month);
                if (invoiceData == null)
                    return NotFound("No charging sessions found for this user and month");

                var molliePayment = await _context.MolliePayments
                    .FirstOrDefaultAsync(p => p.ChargingUserId == userId && p.Year == year && p.Month == month);

                // Create a payment record on first PDF download
                if (molliePayment == null)
                {
                    try
                    {
                        var invoiceNumber = invoiceData.InvoiceNumber;
                        var monthName = new DateTime(year, month, 1).ToString("MMMM yyyy");
                        var description = $"Charging invoice {invoiceNumber} – {invoiceData.UserName} – {monthName}";

                        var redirectUrl = _configuration["Mollie:RedirectUrl"];
                        if (string.IsNullOrWhiteSpace(redirectUrl))
                            redirectUrl = $"{Request.Scheme}://{Request.Host}";

                        var baseUrl = _configuration["Mollie:BaseUrl"];
                        var webhookUrl = (!string.IsNullOrWhiteSpace(baseUrl) && baseUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                            ? $"{baseUrl.TrimEnd('/')}/api/invoices/payments/webhook"
                            : "";

                        var result = await _mollieService.CreatePaymentAsync(
                            Math.Round(invoiceData.TotalAmount, 2), description, invoiceNumber, redirectUrl, webhookUrl);

                        molliePayment = new MolliePayment
                        {
                            ChargingUserId = userId,
                            Year = year,
                            Month = month,
                            MolliePaymentId = result.PaymentId,
                            CheckoutUrl = result.CheckoutUrl,
                            Amount = Math.Round(invoiceData.TotalAmount, 2),
                            Status = result.Status,
                            CreatedAt = DateTime.UtcNow
                        };
                        _context.MolliePayments.Add(molliePayment);
                        _logger.LogInformation("Auto-created payment link on PDF download for user {UserId}, {Year}-{Month}", userId, year, month);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Could not auto-create Mollie payment on PDF download for user {UserId}, {Year}-{Month}", userId, year, month);
                    }
                }

                molliePayment = await _context.MolliePayments
                    .FirstOrDefaultAsync(p => p.ChargingUserId == userId && p.Year == year && p.Month == month);
                invoiceData.PaymentUrl = molliePayment?.CheckoutUrl;
                invoiceData.IsPaid = molliePayment?.Status == "paid";

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

        [HttpPost("{userId}/{year}/{month}/payment-link")]
        public async Task<IActionResult> CreatePaymentLink(int userId, int year, int month)
        {
            try
            {
                var token = Request.Headers.Authorization.ToString().Replace("Bearer ", "");
                var userIdString = _jwtService.ValidateToken(token);

                if (userIdString == null || !int.TryParse(userIdString, out int currentUserId))
                    return Unauthorized();

                // Check for existing payment link and sync latest status first
                var existing = await _context.MolliePayments
                    .FirstOrDefaultAsync(p => p.ChargingUserId == userId && p.Year == year && p.Month == month);

                if (existing != null)
                {
                    try
                    {
                        var latestStatus = await _mollieService.GetPaymentStatusAsync(existing.MolliePaymentId);
                        if (!string.Equals(existing.Status, latestStatus, StringComparison.OrdinalIgnoreCase))
                        {
                            existing.Status = latestStatus;
                            existing.UpdatedAt = DateTime.UtcNow;
                            await _context.SaveChangesAsync();
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Could not refresh Mollie status for {PaymentId}", existing.MolliePaymentId);
                    }

                    // Reuse active or paid links; replace terminal unpaid links with a fresh payment below.
                    if (existing.Status == "open" || existing.Status == "pending" || existing.Status == "paid")
                        return Ok(new { checkoutUrl = existing.CheckoutUrl, paymentId = existing.MolliePaymentId, status = existing.Status });
                }

                // Get charging sessions to calculate amount
                var chargingSessions = await _context.ChargingSessions
                    .Where(cs => cs.UserId == userId && cs.Start.Year == year && cs.Start.Month == month)
                    .ToListAsync();

                if (!chargingSessions.Any())
                    return NotFound("No charging sessions found for this user and month");

                var totalKwh = chargingSessions.Sum(cs => cs.ChargedKwh);
                var ratePerKwh = await _context.EnergyPrices
                    .Where(p => p.Year < year || (p.Year == year && p.Month <= month))
                    .OrderByDescending(p => p.Year).ThenByDescending(p => p.Month)
                    .Select(p => (decimal?)p.PricePerKwh)
                    .FirstOrDefaultAsync() ?? 0.22m;

                var totalAmount = Math.Round(totalKwh * ratePerKwh, 2);
                var invoiceNumber = $"INV-{year:D4}-{month:D2}-{userId:D4}";
                var monthName = new DateTime(year, month, 1).ToString("MMMM yyyy");

                var profile = await _context.ChargingUserProfiles.FirstOrDefaultAsync(p => p.Id == userId);
                var linkedUser = await _context.Users.FirstOrDefaultAsync(u => u.ChargingUserId == userId);
                var userName = linkedUser?.Name ?? profile?.DisplayName ?? $"Charging User {userId}";

                var redirectUrl = _configuration["Mollie:RedirectUrl"];
                if (string.IsNullOrWhiteSpace(redirectUrl))
                {
                    redirectUrl = Request.Headers["Referer"].ToString();
                    if (string.IsNullOrWhiteSpace(redirectUrl))
                        redirectUrl = $"{Request.Scheme}://{Request.Host}";
                }

                var baseUrl = _configuration["Mollie:BaseUrl"];
                var webhookUrl = (!string.IsNullOrWhiteSpace(baseUrl) && baseUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                    ? $"{baseUrl.TrimEnd('/')}/api/invoices/payments/webhook"
                    : "";
                var description = $"Charging invoice {invoiceNumber} – {userName} – {monthName}";

                var result = await _mollieService.CreatePaymentAsync(totalAmount, description, invoiceNumber, redirectUrl, webhookUrl);

                if (existing != null)
                {
                    _context.MolliePayments.Remove(existing);
                }

                var molliePayment = new MolliePayment
                {
                    ChargingUserId = userId,
                    Year = year,
                    Month = month,
                    MolliePaymentId = result.PaymentId,
                    CheckoutUrl = result.CheckoutUrl,
                    Amount = totalAmount,
                    Status = result.Status,
                    CreatedAt = DateTime.UtcNow
                };

                _context.MolliePayments.Add(molliePayment);

                var requester = await _context.Users.FindAsync(currentUserId);
                _context.EventLogs.Add(new EventLog
                {
                    Timestamp = DateTime.UtcNow,
                    UserName = requester?.Name ?? $"User {currentUserId}",
                    Action = $"Payment link created for {userName} – {monthName}"
                });

                await _context.SaveChangesAsync();

                _logger.LogInformation("Payment link created for charging user {UserId}, {Year}-{Month}: {PaymentId}", userId, year, month, result.PaymentId);

                return Ok(new { checkoutUrl = result.CheckoutUrl, paymentId = result.PaymentId, status = result.Status });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating payment link for user {UserId}, {Year}-{Month}", userId, year, month);
                return BadRequest($"Failed to create payment link: {ex.Message}");
            }
        }

        [HttpGet("{userId}/{year}/{month}/payment-link")]
        public async Task<IActionResult> GetPaymentLink(int userId, int year, int month)
        {
            try
            {
                var token = Request.Headers.Authorization.ToString().Replace("Bearer ", "");
                var userIdString = _jwtService.ValidateToken(token);

                if (userIdString == null || !int.TryParse(userIdString, out _))
                    return Unauthorized();

                var payment = await _context.MolliePayments
                    .FirstOrDefaultAsync(p => p.ChargingUserId == userId && p.Year == year && p.Month == month);

                if (payment == null)
                    return NotFound();

                // Refresh status from Mollie while not in a terminal state
                if (!FinalPaymentStatuses.Contains(payment.Status))
                {
                    try
                    {
                        var latestStatus = await _mollieService.GetPaymentStatusAsync(payment.MolliePaymentId);
                        if (latestStatus != payment.Status)
                        {
                            payment.Status = latestStatus;
                            payment.UpdatedAt = DateTime.UtcNow;
                            await _context.SaveChangesAsync();
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Could not refresh Mollie payment status for {PaymentId}", payment.MolliePaymentId);
                    }
                }

                return Ok(new { checkoutUrl = payment.CheckoutUrl, paymentId = payment.MolliePaymentId, status = payment.Status, amount = payment.Amount });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving payment link for user {UserId}, {Year}-{Month}", userId, year, month);
                return BadRequest($"Failed to retrieve payment link: {ex.Message}");
            }
        }

        [HttpGet("payments")]
        public async Task<IActionResult> GetAllPayments()
        {
            try
            {
                var token = Request.Headers.Authorization.ToString().Replace("Bearer ", "");
                var userIdString = _jwtService.ValidateToken(token);

                if (userIdString == null || !int.TryParse(userIdString, out int requestingUserId))
                    return Unauthorized();

                var requestingUser = await _context.Users.FindAsync(requestingUserId);
                if (requestingUser == null || requestingUser.Role != UserRole.Admin)
                    return Forbid();

                var payments = await _context.MolliePayments
                    .OrderByDescending(p => p.CreatedAt)
                    .ToListAsync();

                // Resolve display names (loaded early so the refresh loop can use them)
                var profiles = await _context.ChargingUserProfiles.ToListAsync();
                var linkedUsers = await _context.Users
                    .Where(u => u.ChargingUserId != null)
                    .Select(u => new { u.ChargingUserId, u.Name })
                    .ToListAsync();

                // Refresh non-final statuses from Mollie
                var stalePayments = payments.Where(p => !FinalPaymentStatuses.Contains(p.Status)).ToList();
                foreach (var p in stalePayments)
                {
                    try
                    {
                        var latest = await _mollieService.GetPaymentStatusAsync(p.MolliePaymentId);
                        if (latest != p.Status)
                        {
                            var previousStatus = p.Status;
                            p.Status = latest;
                            p.UpdatedAt = DateTime.UtcNow;

                            var linked = linkedUsers.FirstOrDefault(u => u.ChargingUserId == p.ChargingUserId);
                            var profile = profiles.FirstOrDefault(pr => pr.Id == p.ChargingUserId);
                            var name = linked?.Name ?? profile?.DisplayName ?? $"Charging User {p.ChargingUserId}";
                            var monthName = new DateTime(p.Year, p.Month, 1).ToString("MMMM yyyy");

                            _context.EventLogs.Add(new EventLog
                            {
                                Timestamp = DateTime.UtcNow,
                                UserName = "Admin refresh",
                                Action = $"Payment status changed: {name} – {monthName} – {previousStatus} → {latest}"
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Could not refresh Mollie status for {PaymentId}", p.MolliePaymentId);
                    }
                }
                if (stalePayments.Any(p => p.UpdatedAt.HasValue))
                    await _context.SaveChangesAsync();

                var result = payments.Select(p =>
                {
                    var linked = linkedUsers.FirstOrDefault(u => u.ChargingUserId == p.ChargingUserId);
                    var profile = profiles.FirstOrDefault(pr => pr.Id == p.ChargingUserId);
                    var name = linked?.Name ?? profile?.DisplayName ?? $"Charging User {p.ChargingUserId}";
                    return new
                    {
                        p.Id,
                        p.ChargingUserId,
                        UserName = name,
                        p.Year,
                        p.Month,
                        p.MolliePaymentId,
                        p.CheckoutUrl,
                        p.Amount,
                        p.Status,
                        p.CreatedAt,
                        p.UpdatedAt
                    };
                }).ToList();

                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving all payments");
                return BadRequest($"Failed to retrieve payments: {ex.Message}");
            }
        }

        [HttpPost("payments/webhook")]
        public async Task<IActionResult> MollieWebhook()
        {
            try
            {
                // Mollie sends the payment id as a form field named "id"
                var molliePaymentId = Request.Form["id"].ToString();
                if (string.IsNullOrWhiteSpace(molliePaymentId))
                    return BadRequest();

                var payment = await _context.MolliePayments
                    .FirstOrDefaultAsync(p => p.MolliePaymentId == molliePaymentId);

                if (payment == null)
                    return Ok(); // Unknown payment, ignore

                var latestStatus = await _mollieService.GetPaymentStatusAsync(molliePaymentId);
                if (latestStatus != payment.Status)
                {
                    var previousStatus = payment.Status;
                    payment.Status = latestStatus;
                    payment.UpdatedAt = DateTime.UtcNow;

                    var profile = await _context.ChargingUserProfiles.FirstOrDefaultAsync(p => p.Id == payment.ChargingUserId);
                    var linkedUser = await _context.Users.FirstOrDefaultAsync(u => u.ChargingUserId == payment.ChargingUserId);
                    var userName = linkedUser?.Name ?? profile?.DisplayName ?? $"Charging User {payment.ChargingUserId}";
                    var monthName = new DateTime(payment.Year, payment.Month, 1).ToString("MMMM yyyy");

                    _context.EventLogs.Add(new EventLog
                    {
                        Timestamp = DateTime.UtcNow,
                        UserName = "Mollie",
                        Action = $"Payment status changed: {userName} – {monthName} – {previousStatus} → {latestStatus}"
                    });

                    await _context.SaveChangesAsync();
                    _logger.LogInformation("Mollie webhook: payment {PaymentId} status updated to {Status}", molliePaymentId, latestStatus);
                }

                return Ok();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing Mollie webhook");
                return Ok(); // Always return 200 to Mollie to avoid retries on our errors
            }
        }

        [HttpDelete("payments/{id:int}")]
        public async Task<IActionResult> DeletePayment(int id)
        {
            try
            {
                var token = Request.Headers.Authorization.ToString().Replace("Bearer ", "");
                var userIdString = _jwtService.ValidateToken(token);

                if (userIdString == null || !int.TryParse(userIdString, out int requestingUserId))
                    return Unauthorized();

                var requestingUser = await _context.Users.FindAsync(requestingUserId);
                if (requestingUser == null || requestingUser.Role != UserRole.Admin)
                    return Forbid();

                var payment = await _context.MolliePayments.FirstOrDefaultAsync(p => p.Id == id);
                if (payment == null)
                    return NotFound();

                var profile = await _context.ChargingUserProfiles.FirstOrDefaultAsync(p => p.Id == payment.ChargingUserId);
                var linkedUser = await _context.Users.FirstOrDefaultAsync(u => u.ChargingUserId == payment.ChargingUserId);
                var userName = linkedUser?.Name ?? profile?.DisplayName ?? $"Charging User {payment.ChargingUserId}";
                var monthName = new DateTime(payment.Year, payment.Month, 1).ToString("MMMM yyyy");

                _context.MolliePayments.Remove(payment);
                _context.EventLogs.Add(new EventLog
                {
                    Timestamp = DateTime.UtcNow,
                    UserName = requestingUser.Name,
                    Action = $"Payment deleted: {userName} – {monthName}"
                });

                await _context.SaveChangesAsync();
                return NoContent();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting payment {PaymentId}", id);
                return BadRequest($"Failed to delete payment: {ex.Message}");
            }
        }
    }
}