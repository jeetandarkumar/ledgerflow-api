using System.Net;
using System.Text.Json;
using CleanArch.Infrastructure.Services;

namespace CleanArch.API.Middleware;

/// <summary>
/// Validates that every authenticated request carries a recognisable tenant context.
/// Runs after UseAuthentication() so HttpContext.User is already populated.
///
/// What it does:
///   1. Skips unauthenticated requests (anonymous endpoints like /auth/login pass through).
///   2. For authenticated requests, reads the tenant_id claim from the JWT.
///   3. If the claim is missing or malformed, returns 401 — the token is invalid.
///   4. Attaches the parsed TenantId to HttpContext.Items["TenantId"] for any
///      middleware or filter that needs it without re-reading the claim.
///
/// Why not just read from CurrentUserService?
/// This middleware exists to centralise the "authenticated but no tenant = reject" rule.
/// Without it, a token issued without a tenant_id claim (e.g. a token from a different
/// service) would pass JwtBearer validation but then cause NullReferenceExceptions deep
/// inside handlers. Fail fast here with a clear error.
///
/// What it does NOT do:
///   - It does NOT query the database to verify the tenant exists on every request.
///     That would add a DB round-trip to every authenticated call. Tenant existence
///     is validated in handlers that actually need a valid tenant (login, create invoice, etc.).
///   - It does NOT validate role or permissions — that's [Authorize(Roles = ...)] and handler logic.
/// </summary>
public class TenantResolutionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<TenantResolutionMiddleware> _logger;

    // Routes that should bypass tenant resolution entirely.
    // These paths handle their own tenant context (login resolves tenant from request body).
    private static readonly HashSet<string> BypassPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "/health",
        "/swagger",
        "/swagger/index.html",
        "/swagger/v1/swagger.json",
    };

    public TenantResolutionMiddleware(
        RequestDelegate next,
        ILogger<TenantResolutionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Let unauthenticated requests through — anonymous endpoints handle their own logic.
        if (context.User.Identity?.IsAuthenticated is not true)
        {
            await _next(context);
            return;
        }

        // Skip bypass paths even if a JWT is provided.
        var path = context.Request.Path.Value ?? string.Empty;
        if (BypassPaths.Any(bp => path.StartsWith(bp, StringComparison.OrdinalIgnoreCase)))
        {
            await _next(context);
            return;
        }

        // Read tenant_id from the JWT claims (populated by TokenService.GenerateAccessToken).
        var tenantClaim = context.User.FindFirst(CurrentUserService.TenantIdClaimType)?.Value;

        if (string.IsNullOrWhiteSpace(tenantClaim) || !Guid.TryParse(tenantClaim, out var tenantId))
        {
            _logger.LogWarning(
                "Authenticated request from {RemoteIp} rejected — missing or invalid tenant_id claim. Path: {Path}",
                context.Connection.RemoteIpAddress,
                context.Request.Path);

            context.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
            context.Response.ContentType = "application/problem+json";

            var problem = new
            {
                type   = "https://httpstatuses.io/401",
                title  = "Invalid authentication token",
                detail = "The authentication token does not contain a valid tenant identifier. Please log in again.",
                status = 401,
                traceId = context.TraceIdentifier
            };

            await context.Response.WriteAsync(
                JsonSerializer.Serialize(problem, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                }));
            return;
        }

        // Attach to HttpContext.Items for middleware/filters that want it without
        // going through ICurrentUserService (avoids the service locator pattern).
        context.Items["TenantId"] = tenantId;

        _logger.LogDebug(
            "Request for tenant {TenantId}: {Method} {Path}",
            tenantId, context.Request.Method, context.Request.Path);

        await _next(context);
    }
}
