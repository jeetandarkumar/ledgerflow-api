using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace ledgerflowApi.API.Extensions;

/// <summary>
/// Rate limiting configuration.
///
/// Three policies:
///   "auth"    — Applied to login/register. Strict: 10 req/min per IP.
///               Protects against credential-stuffing and brute-force attacks.
///               Uses a fixed window so bursts after a window reset don't sneak through.
///
///   "api"     — Applied to all other authenticated endpoints. 120 req/min per user.
///               Generous enough for legitimate use, tight enough to prevent abuse.
///               Keyed on user ID so one bad actor doesn't throttle other users.
///
///   "strict"  — Applied to sensitive mutations (void invoice, change role).
///               30 req/min per user. Additional layer on top of the "api" policy.
///
/// 429 responses include a Retry-After header so clients know when to retry.
/// </summary>
public static class RateLimitingExtensions
{
    public const string AuthPolicy   = "auth";
    public const string ApiPolicy    = "api";
    public const string StrictPolicy = "strict";

    public static IServiceCollection AddRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            // ── Auth endpoints ─────────────────────────────────────────────────
            options.AddPolicy(AuthPolicy, context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 10,
                        Window = TimeSpan.FromMinutes(1),
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = 0   // No queuing — reject immediately when limit hit
                    }));

            // ── General API endpoints ──────────────────────────────────────────
            options.AddPolicy(ApiPolicy, context =>
            {
                // Authenticated: key on user ID for per-user rate limiting
                var userId = context.User?.FindFirst(
                    System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

                if (userId is not null)
                    return RateLimitPartition.GetSlidingWindowLimiter(
                        partitionKey: $"user:{userId}",
                        factory: _ => new SlidingWindowRateLimiterOptions
                        {
                            PermitLimit = 120,
                            Window = TimeSpan.FromMinutes(1),
                            SegmentsPerWindow = 6,   // 6×10-second segments
                            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                            QueueLimit = 5
                        });

                // Unauthenticated: key on IP
                return RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 30,
                        Window = TimeSpan.FromMinutes(1),
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = 0
                    });
            });

            // ── Sensitive mutation endpoints ───────────────────────────────────
            options.AddPolicy(StrictPolicy, context =>
            {
                var userId = context.User?.FindFirst(
                    System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                    ?? context.Connection.RemoteIpAddress?.ToString()
                    ?? "unknown";

                return RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: $"strict:{userId}",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 30,
                        Window = TimeSpan.FromMinutes(1),
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = 0
                    });
            });

            // 429 response with Retry-After header
            options.OnRejected = async (context, cancellationToken) =>
            {
                context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;

                if (context.Lease.TryGetMetadata(
                    MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter =
                        ((int)retryAfter.TotalSeconds).ToString();
                }

                context.HttpContext.Response.ContentType = "application/problem+json";
                await context.HttpContext.Response.WriteAsync(
                    System.Text.Json.JsonSerializer.Serialize(new
                    {
                        type = "https://httpstatuses.io/429",
                        title = "Too Many Requests",
                        status = 429,
                        detail = "Rate limit exceeded. Check the Retry-After header."
                    }),
                    cancellationToken);
            };
        });

        return services;
    }
}
