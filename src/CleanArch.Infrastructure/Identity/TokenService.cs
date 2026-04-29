using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using CleanArch.Application.Common.Interfaces;
using CleanArch.Domain.Entities;
using CleanArch.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

namespace CleanArch.Infrastructure.Identity;

public sealed class TokenService : ITokenService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<TokenService> _logger;

    public TokenService(IConfiguration configuration, ILogger<TokenService> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    /// <inheritdoc/>
    public string GenerateAccessToken(User user)
    {
        var settings = GetValidatedSettings();
        var claims   = BuildClaims(user);
        var key      = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(settings.SecretKey));
        var creds    = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var expiry   = DateTime.UtcNow.AddMinutes(settings.ExpirationMinutes);

        var token = new JwtSecurityToken(
            issuer:            settings.Issuer,
            audience:          settings.Audience,
            claims:            claims,
            notBefore:         DateTime.UtcNow,
            expires:           expiry,
            signingCredentials: creds);

        _logger.LogDebug(
            "Access token issued — user {UserId}, tenant {TenantId}, expires {Expiry:O}",
            user.Id, user.TenantId, expiry);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// 64 cryptographically random bytes, base64-encoded.
    /// Store a SHA-256 hash of this value in the RefreshTokens table, not the raw value —
    /// a stolen DB dump cannot be used to forge refresh tokens.
    /// </remarks>
    public string GenerateRefreshToken()
    {
        var bytes = new byte[64];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(bytes);
        return Convert.ToBase64String(bytes);
    }

    /// <inheritdoc/>
    public Guid? ValidateToken(string token)
    {
        var settings     = GetValidatedSettings();
        var tokenHandler = new JwtSecurityTokenHandler();
        var key          = Encoding.UTF8.GetBytes(settings.SecretKey);

        try
        {
            tokenHandler.ValidateToken(token, new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey        = new SymmetricSecurityKey(key),
                ValidateIssuer          = true,
                ValidIssuer             = settings.Issuer,
                ValidateAudience        = true,
                ValidAudience           = settings.Audience,
                ValidateLifetime        = true,
                ClockSkew               = TimeSpan.Zero // expired = rejected, no grace period
            }, out var validated);

            var jwt = (JwtSecurityToken)validated;
            var raw = jwt.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value;
            return Guid.TryParse(raw, out var id) ? id : null;
        }
        catch (SecurityTokenExpiredException)
        {
            _logger.LogDebug("Token validation: token has expired.");
            return null;
        }
        catch (SecurityTokenException ex)
        {
            _logger.LogWarning("Token validation failed: {Reason}", ex.Message);
            return null;
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static Claim[] BuildClaims(User user) =>
    [
        // Standard JWT registered claims
        new(JwtRegisteredClaimNames.Sub,   user.Id.ToString()),
        new(JwtRegisteredClaimNames.Email, user.Email),
        new(JwtRegisteredClaimNames.Jti,   Guid.NewGuid().ToString()),

        // ASP.NET Core identity claims — what [Authorize(Roles = "Admin")] reads
        new(ClaimTypes.NameIdentifier, user.Id.ToString()),
        new(ClaimTypes.Name,           user.FullName),
        new(ClaimTypes.Email,          user.Email),
        new(ClaimTypes.Role,           user.Role.ToString()),

        // Custom multi-tenant claims — read by CurrentUserService per request
        new(CurrentUserService.TenantIdClaimType,        user.TenantId.ToString()),
        new(CurrentUserService.DefaultCurrencyClaimType, user.Tenant?.DefaultCurrency ?? "USD"),
    ];

    private JwtSettings GetValidatedSettings()
    {
        var s = _configuration.GetSection("JwtSettings");

        var secret = s["SecretKey"]
            ?? throw new InvalidOperationException(
                "JwtSettings:SecretKey is not configured. " +
                "Provide it via environment variable or user secrets — never commit it to source control.");

        if (Encoding.UTF8.GetByteCount(secret) < 32)
            throw new InvalidOperationException(
                "JwtSettings:SecretKey must be at least 32 characters (256 bits) for HMAC-SHA256.");

        var issuer = s["Issuer"]
            ?? throw new InvalidOperationException("JwtSettings:Issuer is not configured.");

        var audience = s["Audience"]
            ?? throw new InvalidOperationException("JwtSettings:Audience is not configured.");

        if (!int.TryParse(s["ExpirationMinutes"], out var minutes) || minutes <= 0 || minutes > 1440)
            minutes = 60;

        return new JwtSettings(secret, issuer, audience, minutes);
    }

    private sealed record JwtSettings(
        string SecretKey,
        string Issuer,
        string Audience,
        int    ExpirationMinutes);
}
