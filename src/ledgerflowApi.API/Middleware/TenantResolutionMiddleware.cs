using System.Net;
using System.Text.Json;
using ledgerflowApi.Infrastructure.Services;

namespace ledgerflowApi.API.Middleware;

/// <summary>
/// Validates that every authenticated request carries a recognisable tenant context.
/// Runs after UseAuthentication() so HttpContext.User is already populated.
///
/// Rejects authenticated requests that have no tenant_id claim with 401.
/// Unauthenticated requests (anonymous endpoints) pass through untouched.
/// Does NOT query the database — tenant existence is verified in command handlers.
/// </summary>
public class TenantResolutionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<TenantResolutionMiddleware> _logger;

    private static readonly HashSet<string> BypassPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "/health",
        "/swagger",
        "/swagger/index.html",
        "/swagger/v1/swagger.json",
    };

    public TenantResolutionMiddleware(RequestDelegate next, ILogger<TenantResolutionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.User.Identity?.IsAuthenticated is not true)
        {
            await _next(context);
            return;
        }

        var path = context.Request.Path.Value ?? string.Empty;
        if (BypassPaths.Any(bp => path.StartsWith(bp, StringComparison.OrdinalIgnoreCase)))
        {
            await _next(context);
            return;
        }

        var tenantClaim = context.User.FindFirst(CurrentUserService.TenantIdClaimType)?.Value;

        if (string.IsNullOrWhiteSpace(tenantClaim) || !Guid.TryParse(tenantClaim, out var tenantId))
        {
            _logger.LogWarning(
                "Authenticated request from {RemoteIp} rejected — missing or invalid tenant_id claim. Path: {Path}",
                context.Connection.RemoteIpAddress,
                context.Request.Path);

            context.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
            context.Response.ContentType = "application/problem+json";

            await context.Response.WriteAsync(JsonSerializer.Serialize(new
            {
                type = "https://httpstatuses.io/401",
                title = "Invalid authentication token",
                detail = "The authentication token does not contain a valid tenant identifier. Please log in again.",
                status = 401,
                traceId = context.TraceIdentifier
            }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
            return;
        }

        context.Items["TenantId"] = tenantId;

        _logger.LogDebug("Request for tenant {TenantId}: {Method} {Path}",
            tenantId, context.Request.Method, context.Request.Path);

        await _next(context);
    }
}
