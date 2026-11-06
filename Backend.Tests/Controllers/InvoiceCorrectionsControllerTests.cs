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

public class InvoiceCorrectionsControllerTests : IDisposable
{
    private readonly ApplicationDbContext _db;
    private readonly Mock<IJwtService> _jwtMock;

    public InvoiceCorrectionsControllerTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new ApplicationDbContext(options);
        _jwtMock = new Mock<IJwtService>();
    }

    public void Dispose() => _db.Dispose();

    private InvoiceCorrectionsController BuildController(string? userId = "1")
    {
        _jwtMock.Setup(j => j.ValidateToken(It.IsAny<string>())).Returns(userId);
        var controller = new InvoiceCorrectionsController(_db, _jwtMock.Object, NullLogger<InvoiceCorrectionsController>.Instance);
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers.Authorization = "Bearer t";
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        return controller;
    }

    [Fact]
    public async Task Create_NonAdmin_ReturnsUnauthorized()
    {
        _db.Users.Add(new User { Id = 2, Email = "u@test.com", Name = "U", GoogleId = "g2", Role = UserRole.Base });
        await _db.SaveChangesAsync();

        var result = await BuildController("2").Create(new InvoiceCorrectionRequest
        {
            UserId = 2,
            Date = new DateOnly(2026, 11, 6),
            Description = "Extra",
            Amount = 1
        });

        Assert.IsType<UnauthorizedResult>(result);
        Assert.Empty(_db.InvoiceCorrections);
    }

    [Fact]
    public async Task Create_AddsCorrectionForTheUsersMonth()
    {
        _db.Users.Add(new User { Id = 1, Email = "admin@test.com", Name = "Admin", GoogleId = "g1", Role = UserRole.Admin });
        _db.Users.Add(new User { Id = 2, Email = "u@test.com", Name = "Member", GoogleId = "g2", Role = UserRole.Base });
        await _db.SaveChangesAsync();

        var result = await BuildController().Create(new InvoiceCorrectionRequest
        {
            UserId = 2,
            Date = new DateOnly(2026, 11, 6),
            Description = "  Missed kWh  ",
            Amount = 3.456m
        });

        Assert.IsType<OkObjectResult>(result);
        var saved = Assert.Single(_db.InvoiceCorrections);
        Assert.Equal(2, saved.UserId);
        Assert.Equal(new DateOnly(2026, 11, 6), saved.Date);
        Assert.Equal("Missed kWh", saved.Description);
        Assert.Equal(3.46m, saved.Amount);
    }

    [Fact]
    public async Task Create_RejectsZeroAmount()
    {
        _db.Users.Add(new User { Id = 1, Email = "admin@test.com", Name = "Admin", GoogleId = "g1", Role = UserRole.Admin });
        await _db.SaveChangesAsync();

        var result = await BuildController().Create(new InvoiceCorrectionRequest
        {
            UserId = 1,
            Date = new DateOnly(2026, 11, 6),
            Description = "Nothing",
            Amount = 0
        });

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Contains("zero", bad.Value!.ToString());
    }
}
