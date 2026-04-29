using CleanArch.Domain.Entities;
using CleanArch.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CleanArch.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core mapping for AuditLog.
///
/// Design decisions:
/// - No soft-delete query filter: audit logs are never "deleted" from queries.
///   They are facts — everything is always visible.
/// - No concurrency token: audit logs are append-only and immutable by contract.
///   Concurrent INSERT is fine; conflicting UPDATE is impossible by design.
/// - StateBefore/StateAfter/Metadata are NVARCHAR(MAX) — JSON snapshots can be large
///   for complex entities, and we can't predict the upper bound at schema design time.
/// - Action is stored as string (not int) for the same reasons as InvoiceStatus.
/// </summary>
public class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("AuditLogs");

        builder.HasKey(a => a.Id);

        builder.Property(a => a.Id)
            .ValueGeneratedNever();

        // ── Tenant ────────────────────────────────────────────────────────────
        builder.Property(a => a.TenantId).IsRequired();

        builder.HasIndex(a => a.TenantId)
            .HasDatabaseName("IX_AuditLogs_TenantId");

        // ── Who ───────────────────────────────────────────────────────────────
        builder.Property(a => a.UserId);

        builder.Property(a => a.UserDisplayName)
            .HasMaxLength(200);

        // Index for "all actions by this user" queries (security reviews)
        builder.HasIndex(a => new { a.TenantId, a.UserId })
            .HasDatabaseName("IX_AuditLogs_TenantId_UserId");

        // ── What ──────────────────────────────────────────────────────────────
        builder.Property(a => a.Action)
            .IsRequired()
            .HasMaxLength(30)
            .HasConversion<string>();

        builder.Property(a => a.EntityType)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(a => a.EntityId)
            .IsRequired();

        // Index for "all audit events for this specific record" queries
        builder.HasIndex(a => new { a.TenantId, a.EntityType, a.EntityId })
            .HasDatabaseName("IX_AuditLogs_Entity");

        builder.Property(a => a.Description)
            .IsRequired()
            .HasMaxLength(1000);

        // ── State Snapshots ───────────────────────────────────────────────────
        builder.Property(a => a.StateBefore)
            .HasColumnType("nvarchar(max)");

        builder.Property(a => a.StateAfter)
            .HasColumnType("nvarchar(max)");

        // ── Request Context ───────────────────────────────────────────────────
        builder.Property(a => a.IpAddress)
            .HasMaxLength(45); // Max IPv6 length

        builder.Property(a => a.UserAgent)
            .HasMaxLength(500);

        builder.Property(a => a.CorrelationId)
            .HasMaxLength(100);

        builder.Property(a => a.Metadata)
            .HasColumnType("nvarchar(max)");

        // ── Timestamps ────────────────────────────────────────────────────────
        builder.Property(a => a.CreatedAt).IsRequired();

        builder.HasIndex(a => a.CreatedAt)
            .HasDatabaseName("IX_AuditLogs_CreatedAt");
    }
}
