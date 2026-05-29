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

        builder.Ignore(u => u.DomainEvents);
        builder.Ignore(u => u.FullName);

        builder.Property(u => u.UpdatedAt).IsConcurrencyToken();
        builder.Property(u => u.TenantId).IsRequired();

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

        // FIX: BCrypt.Net-Next hashes are 60 chars for $2a$ but can reach 72.
        // Using 128 gives headroom for future algorithm changes (Argon2, etc.)
        // without requiring a migration at that point.
        builder.Property(u => u.PasswordHash).IsRequired().HasMaxLength(128);

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
