using System.IdentityModel.Tokens.Jwt;
using Backend.Models;
using Backend.Services;
using Microsoft.Extensions.Configuration;

namespace Backend.Tests.Services;

public class JwtServiceTests
{
    private static JwtService CreateService(string key = "super-secret-key-for-testing-only-32ch")
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Key"] = key,
                ["Jwt:Issuer"] = "test-issuer",
                ["Jwt:Audience"] = "test-audience"
            })
            .Build();
        return new JwtService(config);
    }

    private static User MakeUser(int id = 1, UserRole role = UserRole.Base) => new()
    {
        Id = id,
        GoogleId = "google-123",
        Email = "test@example.com",
        Name = "Test User",
        Role = role
    };

    [Fact]
    public void GenerateToken_ReturnsNonEmptyString()
    {
        var service = CreateService();
        var token = service.GenerateToken(MakeUser());
        Assert.False(string.IsNullOrWhiteSpace(token));
    }

    [Fact]
    public void GenerateToken_ContainsExpectedSubClaim()
    {
        var service = CreateService();
        var token = service.GenerateToken(MakeUser(id: 42));

        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(token);
        var sub = jwt.Claims.FirstOrDefault(c => c.Type == JwtRegisteredClaimNames.Sub)?.Value;

        Assert.Equal("42", sub);
    }

    [Fact]
    public void GenerateToken_ContainsEmailClaim()
    {
        var service = CreateService();
        var token = service.GenerateToken(MakeUser());

        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(token);
        var email = jwt.Claims.FirstOrDefault(c => c.Type == JwtRegisteredClaimNames.Email)?.Value;

        Assert.Equal("test@example.com", email);
    }

    [Fact]
    public void GenerateToken_ContainsRoleClaim()
    {
        var service = CreateService();
        var token = service.GenerateToken(MakeUser(role: UserRole.Admin));

        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(token);
        var role = jwt.Claims.FirstOrDefault(c => c.Type == System.Security.Claims.ClaimTypes.Role)?.Value;

        Assert.Equal("Admin", role);
    }

    [Fact]
    public void ValidateToken_WithValidToken_ReturnsUserId()
    {
        var service = CreateService();
        var token = service.GenerateToken(MakeUser(id: 7));
        var result = service.ValidateToken(token);
        Assert.Equal("7", result);
    }

    [Fact]
    public void ValidateToken_WithGarbageString_ReturnsNull()
    {
        var service = CreateService();
        var result = service.ValidateToken("not.a.token");
        Assert.Null(result);
    }

    [Fact]
    public void ValidateToken_WithEmptyString_ReturnsNull()
    {
        var service = CreateService();
        var result = service.ValidateToken("");
        Assert.Null(result);
    }

    [Fact]
    public void ValidateToken_WithTamperedSignature_ReturnsNull()
    {
        var service = CreateService();
        var token = service.GenerateToken(MakeUser());
        var tampered = token[..^5] + "XXXXX";
        var result = service.ValidateToken(tampered);
        Assert.Null(result);
    }

    [Fact]
    public void ValidateToken_WithTokenSignedByDifferentKey_ReturnsNull()
    {
        var serviceA = CreateService("key-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
        var serviceB = CreateService("key-bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb");
        var token = serviceA.GenerateToken(MakeUser());
        var result = serviceB.ValidateToken(token);
        Assert.Null(result);
    }
}
