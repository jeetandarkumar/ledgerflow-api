using ledgerflowApi.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ledgerflowApi.Infrastructure.Persistence.Configurations;

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("Users");
        builder.HasKey(u => u.Id);
        builder.Property(u => u.Id).ValueGeneratedNever();

        // Ignore domain infrastructure
        builder.Ignore(u => u.DomainEvents);

        // FullName is a computed property — not a column
        builder.Ignore(u => u.FullName);

        builder.Property(u => u.UpdatedAt).IsConcurrencyToken();
        builder.Property(u => u.TenantId).IsRequired();

        // Tenant navigation — FK only, no cascade delete
        // We explicitly ignore the reverse navigation since Tenant.Ignore(Users) handles it
        builder.HasOne(u => u.Tenant)
            .WithMany()
            .HasForeignKey(u => u.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(u => u.FirstName).IsRequired().HasMaxLength(100);
        builder.Property(u => u.LastName).IsRequired().HasMaxLength(100);
        builder.Property(u => u.Email).IsRequired().HasMaxLength(256);

        builder.HasIndex(u => new { u.TenantId, u.Email })
            .IsUnique()
            .HasDatabaseName("UX_Users_TenantId_Email");

        // BCrypt $2a$ hashes are always 60 chars
        builder.Property(u => u.PasswordHash).IsRequired().HasMaxLength(60);

        builder.Property(u => u.Role)
            .IsRequired()
            .HasMaxLength(20)
            .HasConversion<string>();

        builder.Property(u => u.IsActive).IsRequired();
        builder.Property(u => u.FailedLoginAttempts).IsRequired().HasDefaultValue(0);
        builder.Property(u => u.LockedUntil);
        builder.Property(u => u.LastLoginAt);
        builder.Property(u => u.CreatedAt).IsRequired();
        builder.Property(u => u.IsDeleted).IsRequired();
        builder.Property(u => u.DeletedAt);

        builder.HasQueryFilter(u => !u.IsDeleted);

        builder.HasIndex(u => u.TenantId)
            .HasDatabaseName("IX_Users_TenantId");

        builder.HasIndex(u => new { u.TenantId, u.IsActive })
            .HasDatabaseName("IX_Users_TenantId_IsActive");
    }
}
