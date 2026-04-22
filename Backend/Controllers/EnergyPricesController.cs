using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Backend.Data;
using Backend.Models;
using Backend.Services;

namespace Backend.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class EnergyPricesController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly IJwtService _jwtService;
        private readonly ILogger<EnergyPricesController> _logger;

        public EnergyPricesController(ApplicationDbContext context, IJwtService jwtService, ILogger<EnergyPricesController> logger)
        {
            _context = context;
            _jwtService = jwtService;
            _logger = logger;
        }

        private int? GetAdminUserId()
        {
            var token = Request.Headers.Authorization.ToString().Replace("Bearer ", "");
            var userIdString = _jwtService.ValidateToken(token);
            if (userIdString == null || !int.TryParse(userIdString, out int userId))
                return null;

            var user = _context.Users.Find(userId);
            if (user == null || !user.Role.IsAppAdministrator())
                return null;

            return userId;
        }

        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var token = Request.Headers.Authorization.ToString().Replace("Bearer ", "");
            var userIdString = _jwtService.ValidateToken(token);
            if (userIdString == null)
                return Unauthorized();

            var prices = await _context.EnergyPrices
                .OrderByDescending(p => p.Year)
                .ThenByDescending(p => p.Month)
                .ToListAsync();

            return Ok(prices);
        }

        [HttpPost]
        public async Task<IActionResult> Upsert([FromBody] EnergyPriceRequest request)
        {
            if (GetAdminUserId() is null)
                return Unauthorized();

            if (request.Year < 2000 || request.Year > 2100 || request.Month < 1 || request.Month > 12)
                return BadRequest("Invalid year or month.");

            if (request.PricePerKwh <= 0)
                return BadRequest("Price must be greater than zero.");

            var existing = await _context.EnergyPrices
                .FirstOrDefaultAsync(p => p.Year == request.Year && p.Month == request.Month);

            if (existing != null)
            {
                existing.PricePerKwh = request.PricePerKwh;
                existing.UpdatedAt = DateTime.UtcNow;
            }
            else
            {
                _context.EnergyPrices.Add(new EnergyPrice
                {
                    Year = request.Year,
                    Month = request.Month,
                    PricePerKwh = request.PricePerKwh
                });
            }

            await _context.SaveChangesAsync();
            _logger.LogInformation("Energy price set for {Year}-{Month}: €{Price}/kWh", request.Year, request.Month, request.PricePerKwh);
            return Ok();
        }

        [HttpDelete("{year}/{month}")]
        public async Task<IActionResult> Delete(int year, int month)
        {
            if (GetAdminUserId() is null)
                return Unauthorized();

            var existing = await _context.EnergyPrices
                .FirstOrDefaultAsync(p => p.Year == year && p.Month == month);

            if (existing == null)
                return NotFound();

            _context.EnergyPrices.Remove(existing);
            await _context.SaveChangesAsync();
            return Ok();
        }
    }

    public class EnergyPriceRequest
    {
        public int Year { get; set; }
        public int Month { get; set; }
        public decimal PricePerKwh { get; set; }
    }
}
