using System.Text;
using ledgerflowApi.API.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace ledgerflowApi.API.Extensions;

public static class AuthenticationExtensions
{
    public static IServiceCollection AddJwtAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var jwtSettings = configuration.GetSection("JwtSettings");
        var secretKey   = jwtSettings["SecretKey"]
            ?? throw new InvalidOperationException(
                "JwtSettings:SecretKey is not configured. " +
                "Set it via environment variable or user secrets.");

        if (Encoding.UTF8.GetByteCount(secretKey) < 32)
            throw new InvalidOperationException(
                "JwtSettings:SecretKey must be at least 32 characters for HMAC-SHA256.");

        services
            .AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme    = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultForbidScheme       = JwtBearerDefaults.AuthenticationScheme;
            })
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    // Signature
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey        = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey)),

                    // Issuer / audience
                    ValidateIssuer   = true,
                    ValidIssuer      = jwtSettings["Issuer"],
                    ValidateAudience = true,
                    ValidAudience    = jwtSettings["Audience"],

                    // Expiry — no grace period; expired = rejected
                    ValidateLifetime = true,
                    ClockSkew        = TimeSpan.Zero,

                    // Tell ASP.NET Core which claim maps to roles (used by [Authorize(Roles=...)])
                    RoleClaimType    = System.Security.Claims.ClaimTypes.Role,
                    NameClaimType    = System.Security.Claims.ClaimTypes.Name,
                };

                options.Events = new JwtBearerEvents
                {
                    OnAuthenticationFailed = context =>
                    {
                        // Signal expired tokens so clients can use refresh token
                        // without re-prompting for credentials.
                        if (context.Exception is SecurityTokenExpiredException)
                            context.Response.Headers.Append("Token-Expired", "true");

                        return Task.CompletedTask;
                    },

                    OnForbidden = context =>
                    {
                        // Ensure 403 Forbidden has a consistent content type
                        context.Response.ContentType = "application/problem+json";
                        return Task.CompletedTask;
                    }
                };
            });

        // Register authorization policies (RequireAdmin, RequireMember, etc.)
        services.AddAuthorizationPolicies();

        return services;
    }
}
