using ledgerflowApi.API.BackgroundServices;
using ledgerflowApi.API.Extensions;
using ledgerflowApi.API.HealthChecks;
using ledgerflowApi.API.Middleware;
using ledgerflowApi.API.Persistence;
using ledgerflowApi.Application.Common.Interfaces;
using ledgerflowApi.Application.DependencyInjection;
using ledgerflowApi.Infrastructure.DependencyInjection;
using ledgerflowApi.Infrastructure.HealthChecks;
using ledgerflowApi.Infrastructure.Persistence;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    Log.Information("Starting LedgerFlow API");

    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog((ctx, svc, cfg) => cfg
        .ReadFrom.Configuration(ctx.Configuration)
        .ReadFrom.Services(svc)
        .Enrich.FromLogContext()
        .Enrich.WithEnvironmentName()
        .Enrich.WithThreadId());

    builder.Services.AddControllers();
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerConfiguration();

    builder.Services.AddApplicationServices();
    builder.Services.AddInfrastructureServices(builder.Configuration);
    builder.Services.AddJwtAuthentication(builder.Configuration);
    builder.Services.AddRateLimiting();

    builder.Services.AddCors(options =>
    {
        options.AddPolicy("DefaultPolicy", policy =>
        {
            var origins = builder.Configuration
                .GetSection("Cors:AllowedOrigins")
                .Get<string[]>()
                ?? ["http://localhost:3000"];

            policy.WithOrigins(origins)
                  .AllowAnyHeader()
                  .AllowAnyMethod()
                  .AllowCredentials();
        });
    });

    builder.Services
        .AddHealthChecks()
        .AddCheck<SqlServerHealthCheck>("sql-server", tags: ["ready"])
        .AddCheck<RedisHealthCheck>("redis", tags: ["ready"]);

    // Skipped in the "Testing" environment (WebApplicationFactory-based integration tests)
    // so tests can invoke ProcessOverdueInvoicesAsync directly and deterministically instead
    // of racing a background timer.
    if (!builder.Environment.IsEnvironment("Testing"))
        builder.Services.AddHostedService<OverdueInvoiceProcessingService>();

    var app = builder.Build();

    if (app.Environment.IsDevelopment())
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var seederLogger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

        await db.Database.MigrateAsync();
        await DbSeeder.SeedAsync(db, hasher, seederLogger);
    }

    app.UseMiddleware<GlobalExceptionHandlingMiddleware>();

    app.UseSerilogRequestLogging(opts =>
        opts.MessageTemplate = "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000} ms");

    if (app.Environment.IsDevelopment())
        app.UseSwaggerConfiguration();

    app.UseCors("DefaultPolicy");
    app.UseRateLimiter();
    app.UseAuthentication();
    app.UseMiddleware<TenantResolutionMiddleware>();
    app.UseAuthorization();

    app.MapControllers();

    // Liveness: is the process itself up and able to handle a request? No dependency checks —
    // this is what an orchestrator should use to decide "restart the container", since a
    // transient DB blip shouldn't trigger a restart of an otherwise-healthy process.
    app.MapHealthChecks("/health/live", new HealthCheckOptions
    {
        Predicate = _ => false,
        ResponseWriter = HealthCheckResponseWriter.WriteResponse
    });

    // Readiness: can the app actually serve traffic right now? Runs every check tagged
    // "ready" (SQL Server, Redis) — this is what should gate load-balancer routing.
    app.MapHealthChecks("/health/ready", new HealthCheckOptions
    {
        Predicate = check => check.Tags.Contains("ready"),
        ResponseWriter = HealthCheckResponseWriter.WriteResponse
    });

    // Kept for backward compatibility with the existing Docker healthcheck, Postman
    // collection, and TenantResolutionMiddleware's bypass list — behaves the same as
    // /health/ready.
    app.MapHealthChecks("/health", new HealthCheckOptions
    {
        Predicate = check => check.Tags.Contains("ready"),
        ResponseWriter = HealthCheckResponseWriter.WriteResponse
    });

    app.Run();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}

public partial class Program { }
