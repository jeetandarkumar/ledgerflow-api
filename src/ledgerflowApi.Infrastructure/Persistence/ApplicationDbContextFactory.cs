using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace ledgerflowApi.Infrastructure.Persistence;

/// <summary>
/// Design-time factory for ApplicationDbContext.
///
/// Required because ApplicationDbContext takes IMediator in its constructor,
/// which EF Core Tools cannot inject automatically.
///
/// When running: dotnet ef migrations add / dotnet ef database update
/// EF Tools looks for this factory in the startup-project assembly first,
/// then the project assembly. It calls CreateDbContext with the command-line args.
///
/// This factory is ONLY used by tooling — never by the runtime DI container.
/// The NoOpMediator ensures domain events are never dispatched during migrations.
/// </summary>
public class ApplicationDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args)
    {
        // When called via --startup-project src/ledgerflowApi.API, the CWD is the API project
        // When called without --startup-project, CWD is the Infrastructure project
        // Try to find appsettings in multiple locations
        var basePaths = new[]
        {
            Directory.GetCurrentDirectory(),
            Path.Combine(Directory.GetCurrentDirectory(), "src", "ledgerflowApi.API"),
            Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "ledgerflowApi.API")),
        };

        var configBuilder = new ConfigurationBuilder();
        foreach (var basePath in basePaths.Where(Directory.Exists))
        {
            configBuilder.SetBasePath(basePath);
            if (File.Exists(Path.Combine(basePath, "appsettings.json")))
            {
                configBuilder.AddJsonFile("appsettings.json", optional: true);
                configBuilder.AddJsonFile("appsettings.Development.json", optional: true);
                break;
            }
        }
        configBuilder.AddEnvironmentVariables();

        var config = configBuilder.Build();

        var connectionString =
            config.GetConnectionString("DefaultConnection")
            ?? Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
            ?? "Server=DESKTOP-72ACC7O;Database=ledgerflowApiDb;Trusted_Connection=True;TrustServerCertificate=True;";

        var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>();
        optionsBuilder.UseSqlServer(connectionString, sql =>
            sql.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.GetName().Name));

        return new ApplicationDbContext(optionsBuilder.Options, new NoOpMediator());
    }
}

/// <summary>
/// No-op IMediator implementation for use during design-time (migrations).
/// Prevents domain events from being dispatched when EF Tools instantiate the context.
/// </summary>
internal sealed class NoOpMediator : IMediator
{
    public Task Publish(object notification, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
        where TNotification : INotification
        => Task.CompletedTask;

    public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Send is not supported in design-time context.");

    public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
        where TRequest : IRequest
        => throw new NotSupportedException("Send is not supported in design-time context.");

    // ✅ ADD THIS METHOD (missing one)
    public Task<object?> Send(object request, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Send is not supported in design-time context.");

    public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("CreateStream is not supported in design-time context.");

    public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("CreateStream is not supported in design-time context.");
}