using System.Text;
using Backend.Data;
using Backend.Models;
using Backend.Services;
using Backend.Services.Bank;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Backend.Controllers;

[ApiController]
[Route("api/invoices")]
public class BankStatementsController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly IJwtService _jwtService;
    private readonly BankStatementService _statements;
    private readonly ILogger<BankStatementsController> _logger;

    public BankStatementsController(
        ApplicationDbContext context,
        IJwtService jwtService,
        BankStatementService statements,
        ILogger<BankStatementsController> logger)
    {
        _context = context;
        _jwtService = jwtService;
        _statements = statements;
        _logger = logger;
    }

    [HttpGet("bank-transactions")]
    public async Task<IActionResult> GetReview(CancellationToken cancellationToken)
    {
        if (await RequireAdminAsync(cancellationToken) is { } denied)
            return denied;

        return Ok(await _statements.GetReviewAsync(cancellationToken));
    }

    [HttpPost("bank-csv")]
    [RequestSizeLimit(2_000_000)]
    public async Task<IActionResult> Upload(IFormFile? file, CancellationToken cancellationToken)
    {
        if (await RequireAdminAsync(cancellationToken) is { } denied)
            return denied;
        if (file == null || file.Length == 0)
            return BadRequest(new { message = "Choose a Triodos CSV file." });

        string csv;
        using (var reader = new StreamReader(file.OpenReadStream(), Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
            csv = await reader.ReadToEndAsync(cancellationToken);

        var outcome = await _statements.ImportAsync(csv, cancellationToken);
        if (outcome.Error != null)
            return BadRequest(new { message = outcome.Error });

        _logger.LogInformation(
            "Imported {Imported} Triodos rows ({Duplicates} duplicates, {Skipped} skipped)",
            outcome.Imported,
            outcome.Duplicates,
            outcome.Skipped);

        return Ok(new
        {
            outcome.Imported,
            outcome.Duplicates,
            outcome.Skipped,
            outcome.Review
        });
    }

    [HttpPost("bank-transactions/{id:int}/confirm")]
    public async Task<IActionResult> Confirm(int id, [FromBody] ConfirmBankMatchRequest? request, CancellationToken cancellationToken)
    {
        if (await RequireAdminAsync(cancellationToken) is { } denied)
            return denied;
        if (request == null)
            return BadRequest(new { message = "Choose the invoice to match." });

        var (error, review) = await _statements.ConfirmAsync(id, request, cancellationToken);
        if (error == "Payment not found.")
            return NotFound(new { message = error });
        if (error == "That invoice is already paid.")
            return Conflict(new { message = error });
        if (error != null)
            return BadRequest(new { message = error });

        return Ok(review);
    }

    [HttpPost("bank-transactions/{id:int}/ignore")]
    public async Task<IActionResult> Ignore(int id, CancellationToken cancellationToken)
    {
        if (await RequireAdminAsync(cancellationToken) is { } denied)
            return denied;

        var (error, review) = await _statements.IgnoreAsync(id, cancellationToken);
        if (error == "Payment not found.")
            return NotFound(new { message = error });
        if (error != null)
            return BadRequest(new { message = error });
        return Ok(review);
    }

    [HttpPost("bank-transactions/{id:int}/reopen")]
    public async Task<IActionResult> Reopen(int id, CancellationToken cancellationToken)
    {
        if (await RequireAdminAsync(cancellationToken) is { } denied)
            return denied;

        var (error, review) = await _statements.ReopenAsync(id, cancellationToken);
        if (error == "Payment not found.")
            return NotFound(new { message = error });
        if (error != null)
            return BadRequest(new { message = error });
        return Ok(review);
    }

    private async Task<IActionResult?> RequireAdminAsync(CancellationToken cancellationToken)
    {
        var token = Request.Headers.Authorization.ToString().Replace("Bearer ", "");
        var userIdString = _jwtService.ValidateToken(token);
        if (userIdString == null || !int.TryParse(userIdString, out var userId))
            return Unauthorized();

        var user = await _context.Users.AsNoTracking().FirstOrDefaultAsync(row => row.Id == userId, cancellationToken);
        if (user == null || !user.Role.IsAppAdministrator())
            return Forbid();
        return null;
    }
}
