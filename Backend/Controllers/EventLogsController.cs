using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Backend.Data;

namespace Backend.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Roles = "Admin,Owner")]
    public class EventLogsController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<EventLogsController> _logger;

        public EventLogsController(ApplicationDbContext context, ILogger<EventLogsController> logger)
        {
            _context = context;
            _logger = logger;
        }

        [HttpGet]
        public async Task<IActionResult> GetEventLogs()
        {
            try
            {
                var logs = await _context.EventLogs
                    .OrderByDescending(e => e.Timestamp)
                    .Select(e => new
                    {
                        id = e.Id,
                        timestamp = e.Timestamp,
                        userName = e.UserName,
                        action = e.Action
                    })
                    .ToListAsync();

                return Ok(logs);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving event logs");
                return StatusCode(500, "Failed to retrieve event logs");
            }
        }
    }
}
