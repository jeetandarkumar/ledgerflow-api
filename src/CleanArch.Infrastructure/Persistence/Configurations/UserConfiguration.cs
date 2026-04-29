using CleanArch.Domain.Entities;
using CleanArch.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CleanArch.Infrastructure.Persistence.Configurations;

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
    	builder.ToTable("Users");
        builder.HasKey(u => u.Id);
        builder.Property(u => u.Id).ValueGeneratedNever();
        builder.Property(u => u.UpdatedAt).IsConcurrencyToken();
        builder.Property(u => u.TenantId).IsRequired();
        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(u => u.TenantId)
            .OnDelete(DeleteBehavior.Restrict); // tenant deletion doesn't cascade

        // -- Name --------------------------------------------------------------
        builder.Property(u => u.FirstName).IsRequired().HasMaxLength(100);
        builder.Property(u => u.LastName).IsRequired().HasMaxLength(100);

        // -- Email -------------------------------------------------------------
        builder.Property(u => u.Email).IsRequired().HasMaxLength(256);

        // Composite unique: email is unique WITHIN a tenant, not globally.
        builder.HasIndex(u => new { u.TenantId, u.Email })
            .IsUnique()
            .HasDatabaseName("UX_Users_TenantId_Email");

        // -- Password ----------------------------------------------------------
        // BCrypt hashes are always 60 characters for version $2a$.
        builder.Property(u => u.PasswordHash)
            .IsRequired()
            .HasMaxLength(60);

        // -- Role --------------------------------------------------------------
        // Store as string for DB readability ("Admin", not "2").
        builder.Property(u => u.Role)
            .IsRequired()
            .HasMaxLength(20)
            .HasConversion<string>();

        // -- Account State -----------------------------------------------------
        builder.Property(u => u.IsActive).IsRequired();

        // -- Security / Lockout ------------------------------------------------
        builder.Property(u => u.FailedLoginAttempts).IsRequired().HasDefaultValue(0);
        builder.Property(u => u.LockedUntil);
        builder.Property(u => u.LastLoginAt);

        // -- Soft Delete -------------------------------------------------------
        // Deactivated users are filtered out by default; use IgnoreQueryFilters() when needed.
        builder.HasQueryFilter(u => !u.IsDeleted);

        // -- Indexes -----------------------------------------------------------
        builder.HasIndex(u => u.TenantId)
            .HasDatabaseName("IX_Users_TenantId");
    }
}
