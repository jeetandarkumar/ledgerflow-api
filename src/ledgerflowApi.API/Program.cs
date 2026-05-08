using ledgerflowApi.API.Extensions;
using ledgerflowApi.API.Middleware;
using ledgerflowApi.Application.DependencyInjection;
using ledgerflowApi.Infrastructure.DependencyInjection;
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

    builder.Services.AddHealthChecks();

    var app = builder.Build();

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
    app.MapHealthChecks("/health");

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
