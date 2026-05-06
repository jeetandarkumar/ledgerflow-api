using System.Net.Http.Headers;
using System.Net.Http.Json;
using ledgerflowApi.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace LedgerFlow.IntegrationTests.Infrastructure;

/// <summary>
/// Custom WebApplicationFactory that boots the real ASP.NET pipeline but
/// swaps out SQL Server for EF Core's in-memory provider.
///
/// Each test collection gets its own factory instance (and therefore its
/// own in-memory database), so tests cannot bleed state into each other.
///
/// Design decisions:
/// - We call CreateScope() inside each test to manipulate the DB before the HTTP
///   call, rather than going via the API — that tests the seeding, not the API.
/// - Redis cache is replaced with a no-op in-memory implementation.
/// - JWT settings use a fixed test secret so we can issue tokens from tests.
/// </summary>
public class LedgerFlowWebApplicationFactory : WebApplicationFactory<Program>
{
    // Unique DB name per factory instance ensures test isolation
    private readonly string _dbName = $"ledgerflow-test-{Guid.NewGuid():N}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureServices(services =>
        {
            // ── Replace real DB context with in-memory ─────────────────────
            services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
            services.RemoveAll<ApplicationDbContext>();

            services.AddDbContext<ApplicationDbContext>(options =>
                options.UseInMemoryDatabase(_dbName));

            // ── Replace Redis with a no-op cache ──────────────────────────
            services.RemoveAll<ledgerflowApi.Application.Common.Interfaces.ICacheService>();
            services.AddSingleton<ledgerflowApi.Application.Common.Interfaces.ICacheService,
                NoOpCacheService>();
        });

        // Override configuration for JWT — tests need a known secret
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["JwtSettings:SecretKey"] = "integration-test-secret-key-minimum-32-bytes!!",
                ["JwtSettings:Issuer"] = "ledgerflow-test",
                ["JwtSettings:Audience"] = "ledgerflow-api-test",
                ["JwtSettings:ExpirationMinutes"] = "60",
                ["ConnectionStrings:DefaultConnection"] = "not-used-in-tests"
            });
        });
    }

    /// <summary>
    /// Creates a scope and seeds the in-memory database, then disposes.
    /// Call this inside tests to set up data before making HTTP requests.
    /// </summary>
    public async Task SeedAsync(Func<ApplicationDbContext, Task> seeder)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await seeder(db);
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Returns an HttpClient pre-configured with a valid JWT for the given
    /// user. Use this to make authenticated requests in tests.
    /// </summary>
    public HttpClient CreateAuthenticatedClient(string token)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}

/// <summary>
/// No-op cache service for integration tests.
/// We don't want to test Redis in integration tests — that belongs in
/// a dedicated infrastructure test. This keeps tests focused on API logic.
/// </summary>
public class NoOpCacheService : ledgerflowApi.Application.Common.Interfaces.ICacheService
{
    public Task<T?> GetAsync<T>(string key, CancellationToken ct = default)
            where T : class
            => Task.FromResult<T?>(default);
    public Task SetAsync<T>(string key, T value, TimeSpan? expiry = null, CancellationToken ct = default)
            where T : class
            => Task.CompletedTask;
    public Task RemoveAsync(string key, CancellationToken ct = default) => Task.CompletedTask;
    public Task<bool> ExistsAsync(string key, CancellationToken ct = default) => Task.FromResult(false);
}

/// <summary>
/// Base class for all integration tests.
/// Inheriting from this gives you a fresh factory (and in-memory DB) and
/// helper methods for seeding data and making authenticated requests.
/// </summary>
public abstract class IntegrationTestBase : IAsyncLifetime
{
    protected LedgerFlowWebApplicationFactory Factory { get; } = new();
    protected HttpClient Client => Factory.CreateClient();

    // ── Helpers ───────────────────────────────────────────────────────────────

    protected async Task SeedAsync(Func<ApplicationDbContext, Task> seeder)
        => await Factory.SeedAsync(seeder);

    protected async Task<string> GetAuthTokenAsync(string email, string password, Guid tenantId)
    {
        var response = await Client.PostAsJsonAsync("/api/auth/login", new
        {
            tenantId,
            email,
            password
        });

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<AuthTokenResponse>();
        return body?.AccessToken ?? throw new InvalidOperationException("No access token in response");
    }

    protected HttpClient CreateClientWithToken(string token)
        => Factory.CreateAuthenticatedClient(token);

    // IAsyncLifetime — called by xUnit
    public virtual Task InitializeAsync() => Task.CompletedTask;

    public virtual async Task DisposeAsync()
    {
        await Factory.DisposeAsync();
    }

    private sealed record AuthTokenResponse(string AccessToken, string RefreshToken);
}
