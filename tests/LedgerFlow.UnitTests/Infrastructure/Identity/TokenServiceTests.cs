using System.IdentityModel.Tokens.Jwt;
using FluentAssertions;
using ledgerflowApi.Domain.Entities;
using ledgerflowApi.Domain.Enums;
using ledgerflowApi.Infrastructure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace LedgerFlow.UnitTests.Infrastructure.Identity;

/// <summary>
/// Unit tests for TokenService (JWT generation and validation).
/// Uses a test configuration with a known secret so we can assert on
/// token contents without touching any real key material.
/// </summary>
public class TokenServiceTests
{
    private const string TestSecret = "super-secret-key-for-unit-tests-minimum-32-chars!!";
    private const string TestIssuer = "ledgerflow-test";
    private const string TestAudience = "ledgerflow-api-test";

    private TokenService CreateService(int expirationMinutes = 60)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["JwtSettings:SecretKey"] = TestSecret,
                ["JwtSettings:Issuer"] = TestIssuer,
                ["JwtSettings:Audience"] = TestAudience,
                ["JwtSettings:ExpirationMinutes"] = expirationMinutes.ToString()
            })
            .Build();

        return new TokenService(config, Mock.Of<ILogger<TokenService>>());
    }

    private static User CreateTestUser(Guid? tenantId = null)
        => User.Create(
            tenantId: tenantId ?? Guid.NewGuid(),
            firstName: "Alice",
            lastName: "Smith",
            email: "alice@acme.com",
            passwordHash: "$2a$11$dummyhashXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXX",
            role: UserRole.Admin);

    // ── GenerateAccessToken ───────────────────────────────────────────────────

    [Fact]
    public void GenerateAccessToken_ReturnsNonEmptyString()
    {
        var service = CreateService();
        var user = CreateTestUser();

        var token = service.GenerateAccessToken(user);

        token.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void GenerateAccessToken_ProducesValidJwt()
    {
        var service = CreateService();
        var user = CreateTestUser();

        var tokenString = service.GenerateAccessToken(user);

        var handler = new JwtSecurityTokenHandler();
        handler.CanReadToken(tokenString).Should().BeTrue();

        var jwt = handler.ReadJwtToken(tokenString);
        jwt.Issuer.Should().Be(TestIssuer);
    }

    [Fact]
    public void GenerateAccessToken_ContainsExpectedClaims()
    {
        var service = CreateService();
        var user = CreateTestUser();

        var tokenString = service.GenerateAccessToken(user);

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(tokenString);

        jwt.Claims.Should().Contain(c => c.Type == "email" && c.Value == user.Email);
        jwt.Claims.Should().Contain(c => c.Value == user.TenantId.ToString());
        jwt.Claims.Should().Contain(c => c.Value == user.Role.ToString());
    }

    [Fact]
    public void GenerateAccessToken_TwoCallsProduceDifferentTokens()
    {
        // jti (JWT ID) should be unique per issuance
        var service = CreateService();
        var user = CreateTestUser();

        var token1 = service.GenerateAccessToken(user);
        var token2 = service.GenerateAccessToken(user);

        token1.Should().NotBe(token2);
    }

    // ── GenerateRefreshToken ──────────────────────────────────────────────────

    [Fact]
    public void GenerateRefreshToken_ReturnsBase64String()
    {
        var service = CreateService();
        var token = service.GenerateRefreshToken();

        var act = () => Convert.FromBase64String(token);
        act.Should().NotThrow();
    }

    [Fact]
    public void GenerateRefreshToken_TwoCallsProduceDifferentTokens()
    {
        var service = CreateService();
        service.GenerateRefreshToken().Should().NotBe(service.GenerateRefreshToken());
    }

    [Fact]
    public void GenerateRefreshToken_Is64BytesWhenDecoded()
    {
        var service = CreateService();
        var decoded = Convert.FromBase64String(service.GenerateRefreshToken());
        decoded.Should().HaveCount(64);
    }

    // ── ValidateToken ─────────────────────────────────────────────────────────

    [Fact]
    public void ValidateToken_ValidToken_ReturnsUserId()
    {
        var service = CreateService();
        var user = CreateTestUser();
        var token = service.GenerateAccessToken(user);

        var result = service.ValidateToken(token);

        result.Should().Be(user.Id);
    }

    [Fact]
    public void ValidateToken_TamperedToken_ReturnsNull()
    {
        var service = CreateService();
        var user = CreateTestUser();
        var token = service.GenerateAccessToken(user) + "tampered";

        var result = service.ValidateToken(token);

        result.Should().BeNull();
    }

    [Fact]
    public void ValidateToken_EmptyString_ThrowsArgumentNullException()
    {
        var service = CreateService();

        var act = () => service.ValidateToken("");

        act.Should()
            .Throw<ArgumentNullException>()
            .WithMessage("*token*");
    }

    // ── Configuration validation ──────────────────────────────────────────────

    [Fact]
    public void GenerateAccessToken_MissingSecretKey_ThrowsInvalidOperationException()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["JwtSettings:Issuer"] = TestIssuer,
                ["JwtSettings:Audience"] = TestAudience
            })
            .Build();

        var service = new TokenService(config, Mock.Of<ILogger<TokenService>>());
        var user = CreateTestUser();

        var act = () => service.GenerateAccessToken(user);
        act.Should().Throw<InvalidOperationException>().WithMessage("*SecretKey*");
    }

    [Fact]
    public void GenerateAccessToken_ShortSecretKey_ThrowsInvalidOperationException()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["JwtSettings:SecretKey"] = "tooshort", // < 32 bytes
                ["JwtSettings:Issuer"] = TestIssuer,
                ["JwtSettings:Audience"] = TestAudience
            })
            .Build();

        var service = new TokenService(config, Mock.Of<ILogger<TokenService>>());
        var user = CreateTestUser();

        var act = () => service.GenerateAccessToken(user);
        act.Should().Throw<InvalidOperationException>().WithMessage("*32*");
    }
}
