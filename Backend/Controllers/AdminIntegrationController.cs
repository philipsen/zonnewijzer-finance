using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Controllers;

[ApiController]
[Route("api/admin")]
[Authorize(Roles = "Admin,Owner")]
public class AdminIntegrationController : ControllerBase
{
    public AdminIntegrationController() { }

    [HttpPost("smappee/refresh/service-location-info")]
    public IActionResult RefreshSmappeeServiceLocationInfo() =>
        StatusCode(410, new { error = "Manual Smappee refresh is handled by the integration daemon." });

    [HttpPost("smappee/refresh/service-locations-list")]
    public IActionResult RefreshSmappeeServiceLocationsList() =>
        StatusCode(410, new { error = "Manual Smappee refresh is handled by the integration daemon." });

    [HttpPost("smappee/refresh/rfids")]
    public IActionResult RefreshSmappeeRfids() =>
        StatusCode(410, new { error = "Manual Smappee refresh is handled by the integration daemon." });

    [HttpPost("sigenergy/refresh/energy-flow")]
    public IActionResult RefreshSigenergy() =>
        StatusCode(410, new { error = "Manual Sigenergy refresh is handled by the integration daemon." });
}
