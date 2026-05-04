using CleanArch.Application.Common.Interfaces;
using CleanArch.Domain.Common;
using CleanArch.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;
using System.Reflection;

namespace CleanArch.Infrastructure.Persistence;

public class ApplicationDbContext : DbContext, IApplicationDbContext
{
    private readonly IMediator _mediator;

    public ApplicationDbContext(
        DbContextOptions<ApplicationDbContext> options,
        IMediator mediator) : base(options)
    {
        _mediator = mediator;
    }

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        // Apply all IEntityTypeConfiguration<T> implementations in this assembly.
        // Each configuration explicitly calls builder.Ignore(e => e.DomainEvents)
        // and any other non-mapped properties. That is the correct, discoverable pattern.
        builder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());

        base.OnModelCreating(builder);
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        // Collect and clear domain events from all tracked aggregates BEFORE saving.
        // Clearing before Save prevents a re-entrant SaveChanges call (from an event handler)
        // from re-dispatching the same events.
        var domainEvents = ChangeTracker
            .Entries<BaseEntity>()
            .SelectMany(e =>
            {
                var events = e.Entity.DomainEvents.ToList();
                e.Entity.ClearDomainEvents();
                return events;
            })
            .ToList();

        var result = await base.SaveChangesAsync(cancellationToken);

        // Dispatch AFTER the database write succeeds so handlers see committed data.
        // If dispatch fails the DB changes are already committed — domain events are
        // side effects and must be idempotent + retriable (email, read-model update, etc.)
        foreach (var domainEvent in domainEvents)
            await _mediator.Publish(domainEvent, cancellationToken);

        return result;
    }
}
