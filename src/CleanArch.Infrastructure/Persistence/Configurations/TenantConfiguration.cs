using CleanArch.Domain.Entities;
using CleanArch.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CleanArch.Infrastructure.Persistence.Configurations;

public class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> builder)
    {
        builder.ToTable("Tenants");

        builder.HasKey(t => t.Id);

        builder.Property(t => t.Id)
            .ValueGeneratedNever();

        builder.Property(t => t.UpdatedAt)
            .IsConcurrencyToken();

        builder.Property(t => t.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(t => t.Slug)
            .IsRequired()
            .HasMaxLength(63);

        builder.HasIndex(t => t.Slug)
            .IsUnique()
            .HasDatabaseName("UX_Tenants_Slug");

        builder.Property(t => t.BillingEmail)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(t => t.Status)
            .IsRequired()
            .HasMaxLength(20)
            .HasConversion<string>();

        builder.Property(t => t.DefaultCurrency)
            .IsRequired()
            .HasMaxLength(3)
            .IsFixedLength();

        builder.Property(t => t.TrialEndsAt);
        builder.Property(t => t.CancelledAt);

        builder.OwnsOne(t => t.BillingAddress, address =>
        {
            address.Property(a => a.Line1).HasColumnName("BillingAddress_Line1").HasMaxLength(200);
            address.Property(a => a.Line2).HasColumnName("BillingAddress_Line2").HasMaxLength(200);
            address.Property(a => a.City).HasColumnName("BillingAddress_City").HasMaxLength(100);
            address.Property(a => a.State).HasColumnName("BillingAddress_State").HasMaxLength(100);
            address.Property(a => a.CountryCode).HasColumnName("BillingAddress_CountryCode").HasMaxLength(2).IsFixedLength();
            address.Property(a => a.PostalCode).HasColumnName("BillingAddress_PostalCode").HasMaxLength(20);
        });

        builder.HasQueryFilter(t => !t.IsDeleted);
    }
}
