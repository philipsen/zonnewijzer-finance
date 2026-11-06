using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Backend.Data;
using Backend.Models;
using Backend.Services;

namespace Backend.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class InvoiceCorrectionsController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly IJwtService _jwtService;
        private readonly ILogger<InvoiceCorrectionsController> _logger;

        public InvoiceCorrectionsController(
            ApplicationDbContext context,
            IJwtService jwtService,
            ILogger<InvoiceCorrectionsController> logger)
        {
            _context = context;
            _jwtService = jwtService;
            _logger = logger;
        }

        private async Task<User?> GetAdminAsync()
        {
            var token = Request.Headers.Authorization.ToString().Replace("Bearer ", "");
            var userIdString = _jwtService.ValidateToken(token);
            if (userIdString == null || !int.TryParse(userIdString, out int userId))
                return null;

            var user = await _context.Users.FindAsync(userId);
            if (user == null || !user.Role.IsAppAdministrator())
                return null;

            return user;
        }

        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            if (await GetAdminAsync() is null)
                return Unauthorized();

            var stored = await _context.InvoiceCorrections
                .AsNoTracking()
                .OrderByDescending(c => c.Date)
                .ThenByDescending(c => c.Id)
                .Select(c => new
                {
                    c.Id,
                    c.UserId,
                    UserName = c.User.Name,
                    c.Date,
                    c.Description,
                    c.Amount,
                    c.CreatedAt,
                    c.UpdatedAt
                })
                .ToListAsync();

            var corrections = stored.Select(c =>
            {
                var (exclVat, vatAmount) = InvoiceVat.SplitInclusive(c.Amount);
                return new
                {
                    c.Id,
                    c.UserId,
                    c.UserName,
                    c.Date,
                    c.Description,
                    c.Amount,
                    AmountExclVat = exclVat,
                    VatAmount = vatAmount,
                    c.CreatedAt,
                    c.UpdatedAt
                };
            });

            return Ok(corrections);
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] InvoiceCorrectionRequest request)
        {
            var admin = await GetAdminAsync();
            if (admin is null)
                return Unauthorized();

            var error = await ValidateAsync(request);
            if (error != null)
                return BadRequest(error);

            var correction = new InvoiceCorrection
            {
                UserId = request.UserId,
                Date = request.Date,
                Description = request.Description.Trim(),
                Amount = RoundAmount(request.Amount)
            };
            _context.InvoiceCorrections.Add(correction);
            _context.EventLogs.Add(new EventLog
            {
                Timestamp = DateTime.UtcNow,
                UserName = admin.Name,
                Action = $"Invoice correction €{correction.Amount:0.00} for user {correction.UserId} on {correction.Date:yyyy-MM-dd}: {correction.Description}"
            });
            await _context.SaveChangesAsync();
            _logger.LogInformation(
                "Invoice correction {Id} added for user {UserId} on {Date}: €{Amount}",
                correction.Id, correction.UserId, correction.Date, correction.Amount);
            return Ok(new { correction.Id });
        }

        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] InvoiceCorrectionRequest request)
        {
            var admin = await GetAdminAsync();
            if (admin is null)
                return Unauthorized();

            var correction = await _context.InvoiceCorrections.FirstOrDefaultAsync(c => c.Id == id);
            if (correction == null)
                return NotFound();

            var error = await ValidateAsync(request);
            if (error != null)
                return BadRequest(error);

            correction.UserId = request.UserId;
            correction.Date = request.Date;
            correction.Description = request.Description.Trim();
            correction.Amount = RoundAmount(request.Amount);
            correction.UpdatedAt = DateTime.UtcNow;
            _context.EventLogs.Add(new EventLog
            {
                Timestamp = DateTime.UtcNow,
                UserName = admin.Name,
                Action = $"Invoice correction {id} updated for user {correction.UserId} on {correction.Date:yyyy-MM-dd}: €{correction.Amount:0.00}"
            });
            await _context.SaveChangesAsync();
            return Ok();
        }

        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id)
        {
            var admin = await GetAdminAsync();
            if (admin is null)
                return Unauthorized();

            var correction = await _context.InvoiceCorrections.FirstOrDefaultAsync(c => c.Id == id);
            if (correction == null)
                return NotFound();

            _context.InvoiceCorrections.Remove(correction);
            _context.EventLogs.Add(new EventLog
            {
                Timestamp = DateTime.UtcNow,
                UserName = admin.Name,
                Action = $"Invoice correction {id} deleted for user {correction.UserId} on {correction.Date:yyyy-MM-dd}: €{correction.Amount:0.00}"
            });
            await _context.SaveChangesAsync();
            return Ok();
        }

        private async Task<string?> ValidateAsync(InvoiceCorrectionRequest request)
        {
            if (request.Date.Year < 2000 || request.Date.Year > 2100)
                return "Invalid date.";

            var description = request.Description?.Trim() ?? string.Empty;
            if (description.Length == 0)
                return "Description is required.";
            if (description.Length > 500)
                return "Description is too long.";
            request.Description = description;

            if (RoundAmount(request.Amount) == 0)
                return "Amount cannot be zero.";

            var userExists = await _context.Users.AnyAsync(u => u.Id == request.UserId);
            if (!userExists)
                return "User not found.";

            return null;
        }

        private static decimal RoundAmount(decimal amount) =>
            Math.Round(amount, 2, MidpointRounding.AwayFromZero);
    }

    public class InvoiceCorrectionRequest
    {
        public int UserId { get; set; }
        public DateOnly Date { get; set; }
        public string Description { get; set; } = string.Empty;
        public decimal Amount { get; set; }
    }
}
