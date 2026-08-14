using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;

namespace ledgerflowApi.Infrastructure.HealthChecks;

/// <summary>
/// Readiness check for Redis.
///
/// The app is designed to run without Redis (see <c>InfrastructureServiceExtensions</c> —
/// an empty <c>ConnectionStrings:Redis</c> falls back to an in-memory distributed cache), so
/// this check mirrors that intent rather than treating "no Redis configured" as a failure:
///
///   - No connection string configured -> Healthy, with a note that the in-memory fallback is
///     active. This is expected in local/dev setups and should not fail readiness.
///   - Connection string configured but unreachable -> Unhealthy. If the app was deliberately
///     configured to use Redis, being unable to reach it is a real problem worth surfacing.
///
/// Opens a short-lived, non-shared ConnectionMultiplexer scoped to the check itself rather than
/// reusing the app's cache connection, so this check never masks or is masked by application
/// cache traffic.
/// </summary>
public sealed class RedisHealthCheck : IHealthCheck
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(3);

    private readonly IConfiguration _configuration;

    public RedisHealthCheck(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var connectionString = _configuration.GetConnectionString("Redis");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return HealthCheckResult.Healthy(
                "Redis is not configured — the in-memory distributed cache fallback is active.");
        }

        var stopwatch = Stopwatch.StartNew();
        ConnectionMultiplexer? multiplexer = null;

        try
        {
            var options = ConfigurationOptions.Parse(connectionString);
            options.ConnectTimeout = (int)ConnectTimeout.TotalMilliseconds;
            options.SyncTimeout = (int)ConnectTimeout.TotalMilliseconds;
            options.AbortOnConnectFail = true;
            options.ConnectRetry = 1;

            multiplexer = await ConnectionMultiplexer.ConnectAsync(options);
            var latency = await multiplexer.GetDatabase().PingAsync();
            stopwatch.Stop();

            var data = new Dictionary<string, object>
            {
                ["responseTimeMs"] = stopwatch.ElapsedMilliseconds,
                ["pingMs"] = latency.TotalMilliseconds
            };

            return HealthCheckResult.Healthy("Redis is reachable.", data);
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy(
                "Redis is configured but not reachable.", ex);
        }
        finally
        {
            if (multiplexer is not null)
                await multiplexer.DisposeAsync();
        }
    }
}
