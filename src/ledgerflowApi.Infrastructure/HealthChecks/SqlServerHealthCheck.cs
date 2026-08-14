using System.Diagnostics;
using ledgerflowApi.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace ledgerflowApi.Infrastructure.HealthChecks;

/// <summary>
/// Readiness check for SQL Server.
///
/// Uses EF Core's own <see cref="DatabaseFacade.CanConnectAsync"/> rather than a hand-rolled
/// ADO.NET connection — it exercises the exact same connection string, retry policy, and
/// provider the rest of the app uses, so a "Healthy" result here means the app can actually
/// talk to the database, not just that *some* connection string is reachable.
///
/// Deliberately does not run a query against application tables: connectivity is what matters
/// for readiness, not data. Bounded to a short timeout so a stalled DB doesn't hang the whole
/// health check pipeline.
/// </summary>
public sealed class SqlServerHealthCheck : IHealthCheck
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(3);

    private readonly ApplicationDbContext _dbContext;

    public SqlServerHealthCheck(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(ConnectTimeout);

        try
        {
            var canConnect = await _dbContext.Database.CanConnectAsync(timeoutCts.Token);
            stopwatch.Stop();

            var data = new Dictionary<string, object> { ["responseTimeMs"] = stopwatch.ElapsedMilliseconds };

            return canConnect
                ? HealthCheckResult.Healthy("SQL Server is reachable.", data)
                : HealthCheckResult.Unhealthy("SQL Server did not accept a connection.", data: data);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return HealthCheckResult.Unhealthy(
                $"SQL Server did not respond within {ConnectTimeout.TotalSeconds:0}s.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("SQL Server connectivity check failed.", ex);
        }
    }
}
