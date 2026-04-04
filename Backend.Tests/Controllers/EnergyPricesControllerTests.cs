using Backend.Controllers;
using Backend.Data;
using Backend.Models;
using Backend.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Backend.Tests.Controllers;

public class EnergyPricesControllerTests : IDisposable
{
    private readonly ApplicationDbContext _db;
    private readonly Mock<IJwtService> _jwtMock;

    public EnergyPricesControllerTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new ApplicationDbContext(options);
        _jwtMock = new Mock<IJwtService>();
    }

    public void Dispose() => _db.Dispose();

    // ── helpers ────────────────────────────────────────────────────────────────

    private EnergyPricesController BuildController(string authHeader = "Bearer valid-token")
    {
        var controller = new EnergyPricesController(_db, _jwtMock.Object, NullLogger<EnergyPricesController>.Instance);
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers.Authorization = authHeader;
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        return controller;
    }

    private void SetupValidUser(int userId, UserRole role = UserRole.Admin)
    {
        _jwtMock.Setup(j => j.ValidateToken(It.IsAny<string>())).Returns(userId.ToString());
        if (!_db.Users.Any(u => u.Id == userId))
        {
            _db.Users.Add(new User
            {
                Id = userId,
                GoogleId = $"google-{userId}",
                Email = $"user{userId}@test.com",
                Name = $"User {userId}",
                Role = role
            });
            _db.SaveChanges();
        }
    }

    // ── GetAll ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAll_WithInvalidToken_ReturnsUnauthorized()
    {
        _jwtMock.Setup(j => j.ValidateToken(It.IsAny<string>())).Returns((string?)null);
        var result = await BuildController().GetAll();
        Assert.IsType<UnauthorizedResult>(result);
    }

    [Fact]
    public async Task GetAll_WithValidToken_ReturnsOkWithPrices()
    {
        SetupValidUser(1, UserRole.Base);
        _db.EnergyPrices.AddRange(
            new EnergyPrice { Year = 2026, Month = 1, PricePerKwh = 0.22m },
            new EnergyPrice { Year = 2026, Month = 2, PricePerKwh = 0.24m }
        );
        await _db.SaveChangesAsync();

        var result = await BuildController().GetAll();

        var ok = Assert.IsType<OkObjectResult>(result);
        var prices = Assert.IsAssignableFrom<IEnumerable<EnergyPrice>>(ok.Value);
        Assert.Equal(2, prices.Count());
    }

    [Fact]
    public async Task GetAll_ReturnsLatestMonthFirst()
    {
        SetupValidUser(1, UserRole.Base);
        _db.EnergyPrices.AddRange(
            new EnergyPrice { Year = 2026, Month = 1, PricePerKwh = 0.22m },
            new EnergyPrice { Year = 2026, Month = 3, PricePerKwh = 0.26m },
            new EnergyPrice { Year = 2026, Month = 2, PricePerKwh = 0.24m }
        );
        await _db.SaveChangesAsync();

        var result = await BuildController().GetAll();

        var ok = Assert.IsType<OkObjectResult>(result);
        var prices = Assert.IsAssignableFrom<IEnumerable<EnergyPrice>>(ok.Value).ToList();
        Assert.Equal(3, prices[0].Month); // newest first
    }

    // ── Upsert ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Upsert_WithNonAdmin_ReturnsUnauthorized()
    {
        SetupValidUser(1, UserRole.Base);
        var result = await BuildController().Upsert(new EnergyPriceRequest { Year = 2026, Month = 1, PricePerKwh = 0.22m });
        Assert.IsType<UnauthorizedResult>(result);
    }

    [Fact]
    public async Task Upsert_WithInvalidToken_ReturnsUnauthorized()
    {
        _jwtMock.Setup(j => j.ValidateToken(It.IsAny<string>())).Returns((string?)null);
        var result = await BuildController().Upsert(new EnergyPriceRequest { Year = 2026, Month = 1, PricePerKwh = 0.22m });
        Assert.IsType<UnauthorizedResult>(result);
    }

    [Theory]
    [InlineData(1999, 1)]
    [InlineData(2101, 1)]
    public async Task Upsert_WithInvalidYear_ReturnsBadRequest(int year, int month)
    {
        SetupValidUser(1, UserRole.Admin);
        var result = await BuildController().Upsert(new EnergyPriceRequest { Year = year, Month = month, PricePerKwh = 0.22m });
        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Contains("year", bad.Value!.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(2026, 0)]
    [InlineData(2026, 13)]
    public async Task Upsert_WithInvalidMonth_ReturnsBadRequest(int year, int month)
    {
        SetupValidUser(1, UserRole.Admin);
        var result = await BuildController().Upsert(new EnergyPriceRequest { Year = year, Month = month, PricePerKwh = 0.22m });
        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Contains("month", bad.Value!.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Upsert_WithZeroPrice_ReturnsBadRequest()
    {
        SetupValidUser(1, UserRole.Admin);
        var result = await BuildController().Upsert(new EnergyPriceRequest { Year = 2026, Month = 1, PricePerKwh = 0 });
        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Contains("price", bad.Value!.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Upsert_NewEntry_CreatesRecordAndReturnsOk()
    {
        SetupValidUser(1, UserRole.Admin);
        var result = await BuildController().Upsert(new EnergyPriceRequest { Year = 2026, Month = 5, PricePerKwh = 0.25m });
        Assert.IsType<OkResult>(result);
        var saved = await _db.EnergyPrices.FirstOrDefaultAsync(p => p.Year == 2026 && p.Month == 5);
        Assert.NotNull(saved);
        Assert.Equal(0.25m, saved.PricePerKwh);
    }

    [Fact]
    public async Task Upsert_ExistingEntry_UpdatesPriceAndReturnsOk()
    {
        SetupValidUser(1, UserRole.Admin);
        _db.EnergyPrices.Add(new EnergyPrice { Year = 2026, Month = 5, PricePerKwh = 0.22m });
        await _db.SaveChangesAsync();

        var result = await BuildController().Upsert(new EnergyPriceRequest { Year = 2026, Month = 5, PricePerKwh = 0.30m });

        Assert.IsType<OkResult>(result);
        var saved = await _db.EnergyPrices.FirstAsync(p => p.Year == 2026 && p.Month == 5);
        Assert.Equal(0.30m, saved.PricePerKwh);
        Assert.Equal(1, await _db.EnergyPrices.CountAsync()); // no duplicate created
    }

    // ── Delete ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Delete_WithNonAdmin_ReturnsUnauthorized()
    {
        SetupValidUser(1, UserRole.Base);
        var result = await BuildController().Delete(2026, 1);
        Assert.IsType<UnauthorizedResult>(result);
    }

    [Fact]
    public async Task Delete_WhenEntryNotFound_ReturnsNotFound()
    {
        SetupValidUser(1, UserRole.Admin);
        var result = await BuildController().Delete(2026, 1);
        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Delete_WhenExists_RemovesRecordAndReturnsOk()
    {
        SetupValidUser(1, UserRole.Admin);
        _db.EnergyPrices.Add(new EnergyPrice { Year = 2026, Month = 3, PricePerKwh = 0.22m });
        await _db.SaveChangesAsync();

        var result = await BuildController().Delete(2026, 3);

        Assert.IsType<OkResult>(result);
        Assert.Equal(0, await _db.EnergyPrices.CountAsync());
    }
}
