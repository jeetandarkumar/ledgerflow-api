using ledgerflowApi.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ledgerflowApi.Infrastructure.Persistence.Configurations;

public class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("AuditLogs");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();

        // AuditLog inherits from TenantEntity → BaseEntity, so it also has DomainEvents
        builder.Ignore(a => a.DomainEvents);

        builder.Property(a => a.TenantId).IsRequired();

        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(a => a.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(a => a.TenantId)
            .HasDatabaseName("IX_AuditLogs_TenantId");

        builder.Property(a => a.UserId);
        builder.Property(a => a.UserDisplayName).HasMaxLength(200);

        builder.HasIndex(a => new { a.TenantId, a.UserId })
            .HasDatabaseName("IX_AuditLogs_TenantId_UserId");

        builder.Property(a => a.Action)
            .IsRequired()
            .HasMaxLength(30)
            .HasConversion<string>();

        builder.Property(a => a.EntityType).IsRequired().HasMaxLength(100);
        builder.Property(a => a.EntityId).IsRequired();

        builder.HasIndex(a => new { a.TenantId, a.EntityType, a.EntityId })
            .HasDatabaseName("IX_AuditLogs_Entity");

        builder.Property(a => a.Description).IsRequired().HasMaxLength(1000);
        builder.Property(a => a.StateBefore).HasColumnType("nvarchar(max)");
        builder.Property(a => a.StateAfter).HasColumnType("nvarchar(max)");
        builder.Property(a => a.IpAddress).HasMaxLength(45);
        builder.Property(a => a.UserAgent).HasMaxLength(500);
        builder.Property(a => a.CorrelationId).HasMaxLength(100);
        builder.Property(a => a.Metadata).HasColumnType("nvarchar(max)");
        builder.Property(a => a.CreatedAt).IsRequired();

        // AuditLogs are immutable — no UpdatedAt, no soft-delete filter
        // (they must always be visible regardless of IsDeleted)
        builder.Property(a => a.IsDeleted).IsRequired();
        builder.Property(a => a.DeletedAt);

        builder.HasIndex(a => a.CreatedAt)
            .HasDatabaseName("IX_AuditLogs_CreatedAt");

        builder.HasIndex(a => new { a.TenantId, a.Action, a.CreatedAt })
            .HasDatabaseName("IX_AuditLogs_TenantId_Action_CreatedAt");
    }
}
