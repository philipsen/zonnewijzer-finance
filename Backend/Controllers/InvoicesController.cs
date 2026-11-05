using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Backend;
using Backend.Contracts;
using Backend.Data;
using Backend.Models;
using Backend.Services;
using System.Globalization;
using System.Net;
using System.Text;

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
        private readonly IEmailService _emailService;
        private readonly IConfiguration _configuration;
        private readonly ILogger<InvoicesController> _logger;

        /// <param name="appUserId">Target app user id (invoice is per user, not per charge card).</param>
        private async Task<bool> CanAccessAppUserInvoiceAsync(
            int requesterId,
            int appUserId,
            CancellationToken cancellationToken = default)
        {
            var u = await _context.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == requesterId, cancellationToken);
            if (u == null)
                return false;
            if (u.Role.IsAppAdministrator())
                return true;
            return requesterId == appUserId;
        }

        private async Task<string> AppUserDisplayNameAsync(int appUserId)
        {
            var u = await _context.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == appUserId);
            return u?.Name ?? $"User {appUserId}";
        }

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
            IEmailService emailService,
            IConfiguration configuration,
            ILogger<InvoicesController> logger)
        {
            _context = context;
            _jwtService = jwtService;
            _pdfService = pdfService;
            _mollieService = mollieService;
            _invoiceComputation = invoiceComputation;
            _emailService = emailService;
            _configuration = configuration;
            _logger = logger;
        }

        private bool InvoicesEnableOnlinePaymentLinks() =>
            string.Equals(_configuration["Invoices:EnableOnlinePaymentLinks"], "true", StringComparison.OrdinalIgnoreCase);

        /// <summary>Mollie checkout amount. The invoice total itself stays unchanged.</summary>
        private static decimal WeroChargeAmount(decimal invoiceTotal, decimal surcharge) =>
            Math.Round(invoiceTotal + surcharge, 2, MidpointRounding.AwayFromZero);

        private static bool CanReuseMolliePayment(MolliePayment payment, decimal chargeAmount) =>
            payment.Status is "paid" or "pending"
            || (payment.Status == "open" && payment.Amount == chargeAmount);

        /// <summary>Dutch bank-transfer notice. A Mollie checkout link is kept when one is available.</summary>
        private static void ApplyBankTransferInvoicePresentation(InvoiceData invoiceData)
        {
            invoiceData.UsePaymentRequestNotice = true;
        }

        private static bool IsOngoingInvoiceMonth(int year, int month) =>
            BrusselsTime.IsCurrentBrusselsMonth(year, month);

        private static IActionResult? RejectIfOngoingInvoiceMonth(int year, int month)
        {
            if (!IsOngoingInvoiceMonth(year, month))
                return null;
            return new BadRequestObjectResult(
                "Email and payment links are not available for the current month until the billing period ends.");
        }

        [HttpGet]
        public async Task<IActionResult> GetInvoicesGrid(CancellationToken cancellationToken)
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

                var requestingUser = await _context.Users.FindAsync(new object[] { currentUserId }, cancellationToken);
                if (requestingUser == null || !requestingUser.Role.IsAppAdministrator())
                    return Forbid();

                _logger.LogInformation("User {UserId} requesting invoices grid", currentUserId);

                // One row per app user with sessions (only cards linked to an account; sessions are combined per user).
                var allSessions = await _context.ChargingSessions
                    .ToListAsync(cancellationToken);

                var cardIdsWithSessions = allSessions.Select(s => s.ChargeCardId).Distinct().ToList();
                var appUserIdsWithActivity = await _context.UserChargeCards
                    .AsNoTracking()
                    .Where(ucc => cardIdsWithSessions.Contains(ucc.ChargeCardId))
                    .Select(ucc => ucc.UserId)
                    .Distinct()
                    .OrderBy(id => id)
                    .ToListAsync(cancellationToken);

                var appUserRecords = await _context.Users
                    .AsNoTracking()
                    .Where(u => appUserIdsWithActivity.Contains(u.Id))
                    .OrderBy(u => u.Id)
                    .ToListAsync(cancellationToken);

                var users = appUserRecords.Select(u => new
                {
                    Id = u.Id,
                    Name = u.Name,
                    Email = u.Email
                }).ToList();

                var userCardLinks = await _context.UserChargeCards
                    .AsNoTracking()
                    .Where(ucc => appUserIdsWithActivity.Contains(ucc.UserId))
                    .Select(ucc => new { ucc.UserId, ucc.ChargeCardId })
                    .ToListAsync(cancellationToken);
                var cardsByAppUser = userCardLinks
                    .GroupBy(x => x.UserId)
                    .ToDictionary(
                        g => g.Key,
                        g => g.Select(x => x.ChargeCardId).ToList());

                var sessionEndTimestamps = allSessions
                    .Where(cs => cs.ToTimestamp.HasValue)
                    .Select(cs => cs.ToTimestamp!.Value)
                    .ToList();
                var rawMonths = sessionEndTimestamps
                    .Select(BrusselsTime.BrusselsYearMonthFromUnixMilliseconds)
                    .Distinct()
                    .OrderBy(d => d.Year)
                    .ThenBy(d => d.Month)
                    .Select(d => new { d.Year, d.Month })
                    .ToList();

                // Pre-load all energy prices
                var energyPrices = await _context.EnergyPrices
                    .OrderByDescending(p => p.Year).ThenByDescending(p => p.Month)
                    .ToListAsync(cancellationToken);

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
                    RatePerKwh = Math.Round(GetRate(d.Year, d.Month), 2),
                    IsOngoingMonth = IsOngoingInvoiceMonth(d.Year, d.Month)
                }).ToList();

                var gridData = users.Select(user => new
                {
                    User = user,
                    Invoices = months.Select(m =>
                    {
                        if (!cardsByAppUser.TryGetValue(user.Id, out var cids) || cids.Count == 0)
                        {
                            return new
                            {
                                Year = m.Year,
                                Month = m.Month,
                                MonthName = m.Name,
                                HasChargingSessions = false,
                                TotalKwh = 0m,
                                RatePerKwh = GetRate(m.Year, m.Month),
                                TotalAmount = 0m,
                                IsOngoingMonth = m.IsOngoingMonth
                            };
                        }
                        var hasCharging = allSessions
                            .Any(cs => cids.Contains(cs.ChargeCardId) &&
                                      BrusselsTime.IsEndedInBrusselsMonth(cs.ToTimestamp, m.Year, m.Month));
                        var kwh = allSessions
                            .Where(cs => cids.Contains(cs.ChargeCardId) &&
                                        BrusselsTime.IsEndedInBrusselsMonth(cs.ToTimestamp, m.Year, m.Month))
                            .Sum(ChargingSessionEnergy.ChargedKwh);
                        return new
                        {
                            Year = m.Year,
                            Month = m.Month,
                            MonthName = m.Name,
                            HasChargingSessions = hasCharging,
                            TotalKwh = Math.Round(kwh, 2),
                            RatePerKwh = GetRate(m.Year, m.Month),
                            TotalAmount = Math.Round(kwh * GetRate(m.Year, m.Month), 2),
                            IsOngoingMonth = m.IsOngoingMonth
                        };
                    }).ToList()
                }).ToList();

                return Ok(new
                {
                    Users = users,
                    Months = months,
                    GridData = gridData,
                    EnableOnlinePaymentLinks = InvoicesEnableOnlinePaymentLinks()
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving invoices grid");
                return BadRequest($"Failed to retrieve invoices: {ex.Message}");
            }
        }

        private static bool TryValidateCombinedRange(
            int startYear, int startMonth, int endYear, int endMonth,
            out string? error)
        {
            error = null;
            if (startMonth is < 1 or > 12 || endMonth is < 1 or > 12)
            {
                error = "Invalid month.";
                return false;
            }
            if (startYear < 2000 || startYear > 2100 || endYear < 2000 || endYear > 2100)
            {
                error = "Invalid year.";
                return false;
            }
            var startOrdinal = startYear * 12 + startMonth;
            var endOrdinal = endYear * 12 + endMonth;
            if (startOrdinal > endOrdinal)
            {
                error = "Start period must be on or before end period.";
                return false;
            }
            return true;
        }

        [HttpGet("combined/sessions")]
        public async Task<IActionResult> GetCombinedInvoiceSessions(
            [FromQuery] int startYear,
            [FromQuery] int startMonth,
            [FromQuery] int endYear,
            [FromQuery] int endMonth,
            CancellationToken cancellationToken)
        {
            try
            {
                var token = Request.Headers.Authorization.ToString().Replace("Bearer ", "");
                var userIdString = _jwtService.ValidateToken(token);
                if (userIdString == null || !int.TryParse(userIdString, out int currentUserId))
                    return Unauthorized();

                var requestingUser = await _context.Users.FindAsync(new object[] { currentUserId }, cancellationToken);
                if (requestingUser == null || !requestingUser.Role.IsAppAdministrator())
                    return Forbid();

                if (!TryValidateCombinedRange(startYear, startMonth, endYear, endMonth, out var rangeError))
                    return BadRequest(rangeError);

                var fetch = await _invoiceComputation.FetchCombinedSessionsFromSmappeeAsync(
                    startYear, startMonth, endYear, endMonth, cancellationToken);
                var (periodLabel, users) = await BuildCombinedUsersAsync(
                    fetch, startYear, startMonth, endYear, endMonth, cancellationToken);

                return Ok(new
                {
                    PeriodLabel = periodLabel,
                    FetchedAtUtc = DateTime.UtcNow,
                    SessionCount = fetch.Sessions.Count,
                    SkippedUnmapped = fetch.SkippedUnmapped,
                    SkippedInvalid = fetch.SkippedInvalid,
                    Users = users
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching combined invoice sessions from Smappee");
                return BadRequest($"Failed to fetch combined invoice sessions: {ex.Message}");
            }
        }

        [HttpGet("combined")]
        public async Task<IActionResult> GetCombinedInvoices(
            [FromQuery] int startYear,
            [FromQuery] int startMonth,
            [FromQuery] int endYear,
            [FromQuery] int endMonth,
            CancellationToken cancellationToken)
        {
            try
            {
                var token = Request.Headers.Authorization.ToString().Replace("Bearer ", "");
                var userIdString = _jwtService.ValidateToken(token);
                if (userIdString == null || !int.TryParse(userIdString, out int currentUserId))
                    return Unauthorized();

                var requestingUser = await _context.Users.FindAsync(new object[] { currentUserId }, cancellationToken);
                if (requestingUser == null || !requestingUser.Role.IsAppAdministrator())
                    return Forbid();

                if (!TryValidateCombinedRange(startYear, startMonth, endYear, endMonth, out var rangeError))
                    return BadRequest(rangeError);

                var fetch = await _invoiceComputation.FetchCombinedSessionsFromSmappeeAsync(
                    startYear, startMonth, endYear, endMonth, cancellationToken);
                var (periodLabel, users) = await BuildCombinedUsersAsync(
                    fetch, startYear, startMonth, endYear, endMonth, cancellationToken);

                return Ok(new { PeriodLabel = periodLabel, Users = users });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving combined invoices list");
                return BadRequest($"Failed to retrieve combined invoices: {ex.Message}");
            }
        }

        private async Task<(string PeriodLabel, List<object> Users)> BuildCombinedUsersAsync(
            CombinedInvoiceSmappeeFetchResult fetch,
            int startYear,
            int startMonth,
            int endYear,
            int endMonth,
            CancellationToken cancellationToken)
        {
            var periodLabel = InvoiceComputationService.CombinedPeriodLabel(startYear, startMonth, endYear, endMonth);
            if (fetch.ByAppUserId.Count == 0)
                return (periodLabel, new List<object>());

            var appUserIds = fetch.ByAppUserId.Keys.OrderBy(id => id).ToList();
            var appUsers = await _context.Users
                .AsNoTracking()
                .Where(u => appUserIds.Contains(u.Id))
                .OrderBy(u => u.Id)
                .ToListAsync(cancellationToken);

            var users = new List<object>();
            foreach (var appUser in appUsers)
            {
                if (!fetch.ByAppUserId.TryGetValue(appUser.Id, out var userSessions))
                    continue;

                var data = await _invoiceComputation.BuildCombinedInvoiceDataFromSessionsAsync(
                    appUser.Id, userSessions, startYear, startMonth, endYear, endMonth, cancellationToken);
                if (data == null)
                    continue;

                users.Add(new
                {
                    Id = appUser.Id,
                    Name = appUser.Name,
                    Email = appUser.Email,
                    TotalKwh = data.TotalKwh,
                    TotalAmount = data.TotalAmount,
                    SessionCount = userSessions.Count,
                    HasChargingSessions = true,
                    InvoiceNumber = data.InvoiceNumber
                });
            }

            return (periodLabel, users);
        }

        [HttpGet("combined/{userId:int}")]
        public async Task<IActionResult> GenerateCombinedInvoicePdf(
            int userId,
            [FromQuery] int startYear,
            [FromQuery] int startMonth,
            [FromQuery] int endYear,
            [FromQuery] int endMonth,
            CancellationToken cancellationToken)
        {
            try
            {
                var token = Request.Headers.Authorization.ToString().Replace("Bearer ", "");
                var userIdString = _jwtService.ValidateToken(token);
                if (userIdString == null || !int.TryParse(userIdString, out int currentUserId))
                    return Unauthorized();

                if (!await CanAccessAppUserInvoiceAsync(currentUserId, userId, cancellationToken))
                    return Forbid();

                if (!TryValidateCombinedRange(startYear, startMonth, endYear, endMonth, out var rangeError))
                    return BadRequest(rangeError);

                _logger.LogInformation(
                    "Generating combined invoice for app user {UserId}, {StartYear}-{StartMonth} to {EndYear}-{EndMonth}",
                    userId, startYear, startMonth, endYear, endMonth);

                var invoiceData = await _invoiceComputation.BuildCombinedInvoiceDataForAppUserAsync(
                    userId, startYear, startMonth, endYear, endMonth, cancellationToken);
                if (invoiceData == null)
                    return NotFound("No charging sessions found for this user and period");

                var pdfBytes = _pdfService.GenerateInvoicePdf(invoiceData);
                var fileName = $"Invoice_{invoiceData.InvoiceNumber}.pdf";

                var requester = await _context.Users.FindAsync(new object[] { currentUserId }, cancellationToken);
                _context.EventLogs.Add(new EventLog
                {
                    Timestamp = DateTime.UtcNow,
                    UserName = requester?.Name ?? $"User {currentUserId}",
                    Action = $"Combined invoice download for user {userId} – {invoiceData.MonthName}"
                });
                await _context.SaveChangesAsync(cancellationToken);

                return File(pdfBytes, "application/pdf", fileName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating combined invoice PDF for user {UserId}", userId);
                return BadRequest($"Failed to generate combined invoice: {ex.Message}");
            }
        }

        [HttpGet("{userId}/{year}/{month}")]
        public async Task<IActionResult> GenerateInvoicePdf(int userId, int year, int month, CancellationToken cancellationToken)
        {
            try
            {
                var token = Request.Headers.Authorization.ToString().Replace("Bearer ", "");
                var userIdString = _jwtService.ValidateToken(token);
                
                if (userIdString == null || !int.TryParse(userIdString, out int currentUserId))
                {
                    return Unauthorized();
                }

                if (!await CanAccessAppUserInvoiceAsync(currentUserId, userId, cancellationToken))
                    return Forbid();

                _logger.LogInformation("Generating on-the-fly invoice for app user {UserId}, {Year}-{Month}", userId, year, month);

                var prepared = await BuildInvoicePdfAsync(userId, year, month, createPaymentIfMissing: true, cancellationToken);
                if (prepared == null)
                    return NotFound("No charging sessions found for this user and month");

                var (invoiceData, pdfBytes) = prepared.Value;
                var fileName = $"Invoice_{invoiceData.InvoiceNumber}_{year}_{month:D2}.pdf";

                var requester = await _context.Users.FindAsync(new object[] { currentUserId }, cancellationToken);
                _context.EventLogs.Add(new EventLog
                {
                    Timestamp = DateTime.UtcNow,
                    UserName = requester?.Name ?? $"User {currentUserId}",
                    Action = $"Invoice download for user {userId} – {invoiceData.MonthName}"
                });
                await _context.SaveChangesAsync(cancellationToken);

                return File(pdfBytes, "application/pdf", fileName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating invoice PDF for user {UserId}, {Year}-{Month}", userId, year, month);
                return BadRequest($"Failed to generate invoice: {ex.Message}");
            }
        }

        [HttpGet("{year:int}/{month:int}/overview.csv")]
        public async Task<IActionResult> DownloadMonthOverviewCsv(int year, int month, CancellationToken cancellationToken)
        {
            try
            {
                var token = Request.Headers.Authorization.ToString().Replace("Bearer ", "");
                var userIdString = _jwtService.ValidateToken(token);

                if (userIdString == null || !int.TryParse(userIdString, out int currentUserId))
                    return Unauthorized();

                var requestingUser = await _context.Users.FirstOrDefaultAsync(u => u.Id == currentUserId, cancellationToken);
                if (requestingUser == null || !requestingUser.Role.IsAppAdministrator())
                    return Forbid();

                if (month is < 1 or > 12 || year < 2000 || year > 2100)
                    return BadRequest("Invalid year or month.");

                var appUserIds = await _invoiceComputation.GetAppUserIdsForMonthAsync(
                    year, month, null, cancellationToken);
                if (appUserIds.Count == 0)
                    return NotFound("No charging sessions for that period.");

                var culture = CultureInfo.GetCultureInfo("nl-NL");
                var sb = new StringBuilder();
                sb.AppendLine("Factuurnummer;Gebruiker;kWh;Bedrag excl. BTW;BTW;Bedrag incl. BTW");

                decimal totalKwh = 0m;
                decimal totalExcl = 0m;
                decimal totalVat = 0m;
                decimal totalIncl = 0m;
                var rowCount = 0;

                foreach (var appUserId in appUserIds)
                {
                    var data = await _invoiceComputation.BuildInvoiceDataForAppUserAsync(
                        appUserId, year, month, cancellationToken);
                    if (data == null)
                        continue;

                    var (exclVat, vatAmount) = InvoiceVat.SplitInclusive(data.TotalAmount);
                    sb.Append(CsvEscape(data.InvoiceNumber)).Append(';')
                        .Append(CsvEscape(data.UserName)).Append(';')
                        .Append(data.TotalKwh.ToString("0.00", culture)).Append(';')
                        .Append(exclVat.ToString("0.00", culture)).Append(';')
                        .Append(vatAmount.ToString("0.00", culture)).Append(';')
                        .Append(data.TotalAmount.ToString("0.00", culture))
                        .AppendLine();

                    totalKwh += data.TotalKwh;
                    totalExcl += exclVat;
                    totalVat += vatAmount;
                    totalIncl += data.TotalAmount;
                    rowCount++;
                }

                if (rowCount == 0)
                    return NotFound("No charging sessions for that period.");

                sb.Append(CsvEscape("TOTAAL")).Append(';')
                    .Append(';')
                    .Append(totalKwh.ToString("0.00", culture)).Append(';')
                    .Append(totalExcl.ToString("0.00", culture)).Append(';')
                    .Append(totalVat.ToString("0.00", culture)).Append(';')
                    .Append(totalIncl.ToString("0.00", culture))
                    .AppendLine();

                var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
                var fileName = $"InvoiceOverview_{year}-{month:D2}.csv";
                return File(bytes, "text/csv; charset=utf-8", fileName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating invoice overview CSV for {Year}-{Month}", year, month);
                return BadRequest($"Failed to generate overview CSV: {ex.Message}");
            }
        }

        /// <summary>
        /// Mutatie-import for e-Boekhouden.nl. Column names match the import manual so they can be mapped directly.
        /// The date is the first day of the month after the billed month. Tegenrekening is 4301. Kostenplaats is PV Warmoestuin.
        /// </summary>
        [HttpGet("{year:int}/{month:int}/eboekhouden.csv")]
        public async Task<IActionResult> DownloadEboekhoudenCsv(int year, int month, CancellationToken cancellationToken)
        {
            try
            {
                var token = Request.Headers.Authorization.ToString().Replace("Bearer ", "");
                var userIdString = _jwtService.ValidateToken(token);

                if (userIdString == null || !int.TryParse(userIdString, out int currentUserId))
                    return Unauthorized();

                var requestingUser = await _context.Users.FirstOrDefaultAsync(u => u.Id == currentUserId, cancellationToken);
                if (requestingUser == null || !requestingUser.Role.IsAppAdministrator())
                    return Forbid();

                if (month is < 1 or > 12 || year < 2000 || year > 2100)
                    return BadRequest("Invalid year or month.");

                var appUserIds = await _invoiceComputation.GetAppUserIdsForMonthAsync(
                    year, month, null, cancellationToken);
                if (appUserIds.Count == 0)
                    return NotFound("No charging sessions for that period.");

                var culture = CultureInfo.GetCultureInfo("nl-NL");
                var invoiceDate = new DateTime(year, month, 1).AddMonths(1).ToString("d-M-yyyy", CultureInfo.InvariantCulture);
                var monthLabel = new DateTime(year, month, 1).ToString("MMMM yyyy", culture);
                var sb = new StringBuilder();
                sb.AppendLine("Datum;Mutatiesoort;Rekening;Omschrijving;Boekstuk;Bedrag excl;BTW-code;Bedrag incl;Tegenrekening;Relatie;Factuurnummer;Betalingstermijn;Kostenplaats");

                var people = await _context.Users.AsNoTracking()
                    .Select(u => new { u.Id, u.IsActive, u.EboekhoudenCode })
                    .ToListAsync(cancellationToken);
                var relationCodes = people.ToDictionary(
                    u => u.Id,
                    u => EboekhoudenRelationCodes.Normalize(u.EboekhoudenCode));
                var inactiveUserIds = people.Where(u => !u.IsActive).Select(u => u.Id).ToHashSet();

                var rowCount = 0;
                foreach (var appUserId in appUserIds)
                {
                    if (inactiveUserIds.Contains(appUserId))
                        continue;

                    var data = await _invoiceComputation.BuildInvoiceDataForAppUserAsync(
                        appUserId, year, month, cancellationToken);
                    if (data == null)
                        continue;

                    var (exclVat, _) = InvoiceVat.SplitInclusive(data.TotalAmount);
                    var description = $"Laadsessies {monthLabel}, {data.TotalKwh.ToString("0.00", culture)} kWh";
                    sb.Append(CsvEscape(invoiceDate)).Append(';')
                        .Append(CsvEscape("Factuur verstuurd")).Append(';')
                        .Append(';')
                        .Append(CsvEscape(description)).Append(';')
                        .Append(';')
                        .Append(exclVat.ToString("0.00", culture)).Append(';')
                        .Append(CsvEscape("HOOG_VERK_21")).Append(';')
                        .Append(data.TotalAmount.ToString("0.00", culture)).Append(';')
                        .Append("4301;")
                        .Append(CsvEscape(relationCodes.GetValueOrDefault(appUserId))).Append(';')
                        .Append(CsvEscape(data.InvoiceNumber)).Append(';')
                        .Append("30;")
                        .Append(CsvEscape("PV Warmoestuin"))
                        .AppendLine();
                    rowCount++;
                }

                if (rowCount == 0)
                    return NotFound("No charging sessions for that period.");

                var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
                var fileName = $"eboekhouden_{year}-{month:D2}.csv";
                return File(bytes, "text/csv; charset=utf-8", fileName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating e-Boekhouden CSV for {Year}-{Month}", year, month);
                return BadRequest($"Failed to generate e-Boekhouden CSV: {ex.Message}");
            }
        }

        private static string CsvEscape(string? value)
        {
            var text = value ?? string.Empty;
            if (text.Contains('"') || text.Contains(';') || text.Contains('\n') || text.Contains('\r'))
                return $"\"{text.Replace("\"", "\"\"")}\"";
            return text;
        }

        [HttpPost("send-email")]
        public async Task<IActionResult> SendMonthInvoiceEmails([FromBody] SendMonthInvoicesBody body, CancellationToken cancellationToken)
        {
            try
            {
                var token = Request.Headers.Authorization.ToString().Replace("Bearer ", "");
                var userIdString = _jwtService.ValidateToken(token);

                if (userIdString == null || !int.TryParse(userIdString, out int currentUserId))
                    return Unauthorized();

                var requestingUser = await _context.Users.FirstOrDefaultAsync(u => u.Id == currentUserId, cancellationToken);
                if (requestingUser == null || !requestingUser.Role.IsAppAdministrator())
                    return Forbid();

                if (body == null || body.Month is < 1 or > 12 || body.Year < 2000 || body.Year > 2100)
                    return BadRequest("Invalid year or month.");

                if (RejectIfOngoingInvoiceMonth(body.Year, body.Month) is { } ongoingReject)
                    return ongoingReject;

                var appUserIds = await _invoiceComputation.GetAppUserIdsForMonthAsync(
                    body.Year, body.Month, null, cancellationToken);
                if (appUserIds.Count == 0)
                    return NotFound("No charging sessions for that period.");

                var targetUsers = await _context.Users
                    .Where(u => appUserIds.Contains(u.Id))
                    .OrderBy(u => u.Id)
                    .ToListAsync(cancellationToken);

                var results = new List<object>();
                var sent = 0;
                var skippedNoEmail = 0;
                var skippedNoSessions = 0;
                var failed = 0;
                string? smtpError = null;

                foreach (var targetUser in targetUsers)
                {
                    try
                    {
                        var attempt = await SendInvoiceEmailToUserAsync(
                            targetUser, body.Year, body.Month, requestingUser.Name, cancellationToken);
                        results.Add(new
                        {
                            attempt.AppUserId,
                            attempt.Email,
                            attempt.Status,
                            attempt.Detail
                        });
                        switch (attempt.Status)
                        {
                            case "sent":
                                sent++;
                                break;
                            case "skipped_no_email":
                                skippedNoEmail++;
                                break;
                            case "skipped_no_sessions":
                                skippedNoSessions++;
                                break;
                            default:
                                failed++;
                                break;
                        }
                    }
                    catch (InvalidOperationException ex) when (ex.Message.Contains("SMTP", StringComparison.OrdinalIgnoreCase))
                    {
                        smtpError = ex.Message;
                        _logger.LogError(ex, "SMTP not configured while sending month invoices {Year}-{Month}", body.Year, body.Month);
                        failed++;
                        results.Add(new
                        {
                            AppUserId = targetUser.Id,
                            Email = targetUser.Email,
                            Status = "failed",
                            Detail = "Email is not configured."
                        });
                        break;
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        _logger.LogError(ex, "Error emailing invoice for user {UserId}, {Year}-{Month}", targetUser.Id, body.Year, body.Month);
                        results.Add(new
                        {
                            AppUserId = targetUser.Id,
                            Email = targetUser.Email,
                            Status = "failed",
                            Detail = ex.Message
                        });
                    }
                }

                if (sent == 0 && smtpError != null)
                    return BadRequest("Email is not configured. Set Smtp:Host, Smtp:Username, Smtp:Password, and Smtp:FromEmail.");

                var monthLabel = new DateTime(body.Year, body.Month, 1).ToString("MMMM yyyy");
                _context.EventLogs.Add(new EventLog
                {
                    Timestamp = DateTime.UtcNow,
                    UserName = requestingUser.Name,
                    Action = $"Invoices emailed for {monthLabel}: {sent} sent, {skippedNoEmail} no email, {skippedNoSessions} no sessions, {failed} failed"
                });
                await _context.SaveChangesAsync(cancellationToken);

                return Ok(new
                {
                    message = $"Sent {sent} invoice(s) for {monthLabel}.",
                    sent,
                    skippedNoEmail,
                    skippedNoSessions,
                    failed,
                    results
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error emailing invoices for {Year}-{Month}", body.Year, body.Month);
                return BadRequest($"Failed to send invoice emails: {ex.Message}");
            }
        }

        [HttpPost("{userId}/{year}/{month}/send-email")]
        public async Task<IActionResult> SendInvoiceEmail(int userId, int year, int month, CancellationToken cancellationToken)
        {
            try
            {
                var token = Request.Headers.Authorization.ToString().Replace("Bearer ", "");
                var userIdString = _jwtService.ValidateToken(token);

                if (userIdString == null || !int.TryParse(userIdString, out int currentUserId))
                    return Unauthorized();

                var requestingUser = await _context.Users.FirstOrDefaultAsync(u => u.Id == currentUserId, cancellationToken);
                if (requestingUser == null || !requestingUser.Role.IsAppAdministrator())
                    return Forbid();

                if (RejectIfOngoingInvoiceMonth(year, month) is { } ongoingReject)
                    return ongoingReject;

                var targetUser = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
                if (targetUser == null)
                    return NotFound("User not found.");

                var attempt = await SendInvoiceEmailToUserAsync(
                    targetUser, year, month, requestingUser.Name, cancellationToken);
                return attempt.Status switch
                {
                    "sent" => Ok(new { message = $"Invoice sent to {targetUser.Email}." }),
                    "skipped_no_email" => BadRequest("This user has no email address."),
                    "skipped_no_sessions" => NotFound("No charging sessions found for this user and month"),
                    _ => BadRequest(attempt.Detail ?? "Failed to send invoice email.")
                };
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("SMTP", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogError(ex, "SMTP not configured for invoice email to user {UserId}, {Year}-{Month}", userId, year, month);
                return BadRequest("Email is not configured. Set Smtp:Host, Smtp:Username, Smtp:Password, and Smtp:FromEmail.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error emailing invoice for user {UserId}, {Year}-{Month}", userId, year, month);
                return BadRequest($"Failed to send invoice email: {ex.Message}");
            }
        }

        private sealed record InvoiceEmailSendAttempt(int AppUserId, string? Email, string Status, string? Detail);

        private async Task<InvoiceEmailSendAttempt> SendInvoiceEmailToUserAsync(
            User targetUser,
            int year,
            int month,
            string requestingUserName,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(targetUser.Email))
                return new InvoiceEmailSendAttempt(targetUser.Id, targetUser.Email, "skipped_no_email", "This user has no email address.");

            _logger.LogInformation("Sending invoice email for app user {UserId}, {Year}-{Month}", targetUser.Id, year, month);

            var prepared = await BuildInvoicePdfAsync(targetUser.Id, year, month, createPaymentIfMissing: true, cancellationToken);
            if (prepared == null)
                return new InvoiceEmailSendAttempt(targetUser.Id, targetUser.Email, "skipped_no_sessions", "No charging sessions found for this user and month");

            var (invoiceData, pdfBytes) = prepared.Value;
            var fileName = $"Invoice_{invoiceData.InvoiceNumber}_{year}_{month:D2}.pdf";
            var encodedName = WebUtility.HtmlEncode(invoiceData.UserName);
            var encodedMonth = WebUtility.HtmlEncode(invoiceData.MonthName);
            var encodedNumber = WebUtility.HtmlEncode(invoiceData.InvoiceNumber);
            var subject = $"Factuur {invoiceData.InvoiceNumber} – {invoiceData.MonthName}";
            var body = $"""
                <p>Beste {encodedName},</p>
                <p>Bijgaand vindt u uw factuur <strong>{encodedNumber}</strong> voor laadsessies in {encodedMonth}.</p>
                <p>Totaalbedrag: <strong>€{invoiceData.TotalAmount:F2}</strong></p>
                <p>Heb je vragen over de factuur, je laadverbruik of ZonneWijzer? Neem dan gerust contact op via <a href="mailto:zonnewijzer@warmoestuin.nl">zonnewijzer@warmoestuin.nl</a>.</p>
                <p>Dank dat je gebruik maakt van ZonneWijzer en daarmee bijdraagt aan het lokaal opwekken én delen van duurzame energie.</p>
                <p>Warme groet,</p>
                <p>Namens collectief ZonneWijzer<br>Administratie</p>
                """;

            var invoiceBcc = _configuration["Smtp:InvoiceBcc"]?.Trim();

            await _emailService.SendAsync(
                targetUser.Email.Trim(),
                subject,
                body,
                new EmailAttachment(fileName, pdfBytes),
                invoiceBcc);

            _context.EventLogs.Add(new EventLog
            {
                Timestamp = DateTime.UtcNow,
                UserName = requestingUserName,
                Action = $"Invoice emailed to {targetUser.Email} for user {targetUser.Id} – {invoiceData.MonthName}"
            });
            await _context.SaveChangesAsync(cancellationToken);

            return new InvoiceEmailSendAttempt(targetUser.Id, targetUser.Email, "sent", null);
        }

        private async Task<(InvoiceData InvoiceData, byte[] PdfBytes)?> BuildInvoicePdfAsync(
            int userId,
            int year,
            int month,
            bool createPaymentIfMissing,
            CancellationToken cancellationToken)
        {
            var invoiceData = await _invoiceComputation.BuildInvoiceDataForAppUserAsync(userId, year, month, cancellationToken);
            if (invoiceData == null)
                return null;

            var molliePayment = await _context.MolliePayments
                .FirstOrDefaultAsync(p => p.AppUserId == userId && p.Year == year && p.Month == month, cancellationToken);
            var chargeAmount = WeroChargeAmount(invoiceData.TotalAmount, invoiceData.MollieSurchargeEur);

            if (createPaymentIfMissing && (molliePayment == null || !CanReuseMolliePayment(molliePayment, chargeAmount)))
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
                        chargeAmount, description, invoiceNumber, redirectUrl, webhookUrl);

                    if (molliePayment != null)
                        _context.MolliePayments.Remove(molliePayment);

                    molliePayment = new MolliePayment
                    {
                        AppUserId = userId,
                        Year = year,
                        Month = month,
                        MolliePaymentId = result.PaymentId,
                        CheckoutUrl = result.CheckoutUrl,
                        Amount = chargeAmount,
                        Status = result.Status,
                        CreatedAt = DateTime.UtcNow
                    };
                    _context.MolliePayments.Add(molliePayment);
                    await _context.SaveChangesAsync(cancellationToken);
                    _logger.LogInformation("Auto-created payment link for user {UserId}, {Year}-{Month}", userId, year, month);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not auto-create Mollie payment for user {UserId}, {Year}-{Month}", userId, year, month);
                }
            }

            molliePayment = await _context.MolliePayments
                .FirstOrDefaultAsync(p => p.AppUserId == userId && p.Year == year && p.Month == month, cancellationToken);
            invoiceData.PaymentUrl = molliePayment?.CheckoutUrl;
            invoiceData.IsPaid = molliePayment?.Status == "paid";

            if (!InvoicesEnableOnlinePaymentLinks())
                ApplyBankTransferInvoicePresentation(invoiceData);

            var pdfBytes = _pdfService.GenerateInvoicePdf(invoiceData);
            return (invoiceData, pdfBytes);
        }

        [HttpPost("{userId}/{year}/{month}/payment-link")]
        public async Task<IActionResult> CreatePaymentLink(int userId, int year, int month, CancellationToken cancellationToken)
        {
            try
            {
                var token = Request.Headers.Authorization.ToString().Replace("Bearer ", "");
                var userIdString = _jwtService.ValidateToken(token);

                if (userIdString == null || !int.TryParse(userIdString, out int currentUserId))
                    return Unauthorized();

                if (!await CanAccessAppUserInvoiceAsync(currentUserId, userId, cancellationToken))
                    return Forbid();

                if (RejectIfOngoingInvoiceMonth(year, month) is { } ongoingReject)
                    return ongoingReject;

                // Check for existing payment link and sync latest status first
                var existing = await _context.MolliePayments
                    .FirstOrDefaultAsync(p => p.AppUserId == userId && p.Year == year && p.Month == month);

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

                    if (existing.Status is "paid" or "pending")
                        return Ok(new { checkoutUrl = existing.CheckoutUrl, paymentId = existing.MolliePaymentId, status = existing.Status });
                }

                var invoicePreview = await _invoiceComputation.BuildInvoiceDataForAppUserAsync(userId, year, month, cancellationToken);
                if (invoicePreview == null)
                {
                    if (existing is { Status: "open" })
                        return Ok(new { checkoutUrl = existing.CheckoutUrl, paymentId = existing.MolliePaymentId, status = existing.Status });
                    return NotFound("No charging sessions found for this user and month");
                }

                var totalAmount = WeroChargeAmount(invoicePreview.TotalAmount, invoicePreview.MollieSurchargeEur);
                if (existing is { Status: "open" } && existing.Amount == totalAmount)
                    return Ok(new { checkoutUrl = existing.CheckoutUrl, paymentId = existing.MolliePaymentId, status = existing.Status });
                var invoiceNumber = invoicePreview.InvoiceNumber;
                var monthName = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Unspecified)
                    .ToString("MMMM yyyy");
                var userName = invoicePreview.UserName;

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
                    AppUserId = userId,
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

                _logger.LogInformation("Payment link created for app user {UserId}, {Year}-{Month}: {PaymentId}", userId, year, month, result.PaymentId);

                return Ok(new { checkoutUrl = result.CheckoutUrl, paymentId = result.PaymentId, status = result.Status });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating payment link for user {UserId}, {Year}-{Month}", userId, year, month);
                return BadRequest($"Failed to create payment link: {ex.Message}");
            }
        }

        [HttpGet("{userId}/{year}/{month}/payment-link")]
        public async Task<IActionResult> GetPaymentLink(int userId, int year, int month, CancellationToken cancellationToken)
        {
            try
            {
                var token = Request.Headers.Authorization.ToString().Replace("Bearer ", "");
                var userIdString = _jwtService.ValidateToken(token);

                if (userIdString == null || !int.TryParse(userIdString, out int currentUserId))
                    return Unauthorized();

                if (!await CanAccessAppUserInvoiceAsync(currentUserId, userId, cancellationToken))
                    return Forbid();

                var payment = await _context.MolliePayments
                    .FirstOrDefaultAsync(p => p.AppUserId == userId && p.Year == year && p.Month == month);

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
                if (requestingUser == null || !requestingUser.Role.IsAppAdministrator())
                    return Forbid();

                var payments = await _context.MolliePayments
                    .OrderByDescending(p => p.CreatedAt)
                    .ToListAsync();

                var appUsers = await _context.Users.AsNoTracking().ToListAsync();

                // Refresh non-final statuses from Mollie
                var stalePayments = payments.Where(p => !FinalPaymentStatuses.Contains(p.Status)).ToList();
                var anyStatusChanged = false;
                foreach (var p in stalePayments)
                {
                    try
                    {
                        var latest = await _mollieService.GetPaymentStatusAsync(p.MolliePaymentId);
                        if (latest != p.Status)
                        {
                            anyStatusChanged = true;
                            var previousStatus = p.Status;
                            p.Status = latest;
                            p.UpdatedAt = DateTime.UtcNow;

                            var name = appUsers.FirstOrDefault(u => u.Id == p.AppUserId)?.Name ?? $"User {p.AppUserId}";
                            var monthName = new DateTime(p.Year, p.Month, 1, 0, 0, 0, DateTimeKind.Unspecified)
                                .ToString("MMMM yyyy");

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
                if (anyStatusChanged)
                    await _context.SaveChangesAsync();

                var result = payments.Select(p =>
                {
                    var name = appUsers.FirstOrDefault(u => u.Id == p.AppUserId)?.Name ?? $"User {p.AppUserId}";
                    return new
                    {
                        p.Id,
                        p.AppUserId,
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

                    var userName = await AppUserDisplayNameAsync(payment.AppUserId);
                    var monthName = new DateTime(payment.Year, payment.Month, 1, 0, 0, 0, DateTimeKind.Unspecified)
                        .ToString("MMMM yyyy");

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
                if (requestingUser == null || !requestingUser.Role.IsAppAdministrator())
                    return Forbid();

                var payment = await _context.MolliePayments.FirstOrDefaultAsync(p => p.Id == id);
                if (payment == null)
                    return NotFound();

                var userName = await AppUserDisplayNameAsync(payment.AppUserId);
                var monthName = new DateTime(payment.Year, payment.Month, 1, 0, 0, 0, DateTimeKind.Unspecified)
                    .ToString("MMMM yyyy");

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