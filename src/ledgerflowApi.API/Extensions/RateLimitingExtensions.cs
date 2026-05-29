using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace ledgerflowApi.API.Extensions;

/// <summary>
/// Three rate limiting policies:
///   "auth"    — 10 req/min per IP. Applied to login/register.
///   "api"     — 120 req/min per authenticated user (30/min for anonymous by IP).
///   "strict"  — 30 req/min per user. Applied to sensitive mutations.
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
            options.AddPolicy(AuthPolicy, context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 10,
                        Window = TimeSpan.FromMinutes(1),
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = 0
                    }));

            options.AddPolicy(ApiPolicy, context =>
            {
                var userId = context.User?.FindFirst(
                    System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

                if (userId is not null)
                    return RateLimitPartition.GetSlidingWindowLimiter(
                        partitionKey: $"user:{userId}",
                        factory: _ => new SlidingWindowRateLimiterOptions
                        {
                            PermitLimit = 120,
                            Window = TimeSpan.FromMinutes(1),
                            SegmentsPerWindow = 6,
                            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                            QueueLimit = 5
                        });

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

            options.AddPolicy(StrictPolicy, context =>
            {
                var key = context.User?.FindFirst(
                    System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                    ?? context.Connection.RemoteIpAddress?.ToString()
                    ?? "unknown";

                return RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: $"strict:{key}",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 30,
                        Window = TimeSpan.FromMinutes(1),
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = 0
                    });
            });

            options.OnRejected = async (context, cancellationToken) =>
            {
                context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;

                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                    context.HttpContext.Response.Headers.RetryAfter = ((int)retryAfter.TotalSeconds).ToString();

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
