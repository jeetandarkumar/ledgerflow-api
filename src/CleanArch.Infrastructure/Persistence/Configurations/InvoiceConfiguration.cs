using CleanArch.Domain.Entities;
using CleanArch.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CleanArch.Infrastructure.Persistence.Configurations;

public class InvoiceConfiguration : IEntityTypeConfiguration<Invoice>
{
    public void Configure(EntityTypeBuilder<Invoice> builder)
    {
        builder.ToTable("Invoices");
        builder.HasKey(i => i.Id);
        builder.Property(i => i.Id).ValueGeneratedNever();

        // ── Non-mapped properties ─────────────────────────────────────────────
        builder.Ignore(i => i.DomainEvents);

        // Computed Money properties — derived from LineItems at runtime, not stored
        builder.Ignore(i => i.Subtotal);
        builder.Ignore(i => i.InvoiceDiscountAmount);
        builder.Ignore(i => i.DiscountedSubtotal);
        builder.Ignore(i => i.TaxAmount);
        builder.Ignore(i => i.TotalAmount);
        builder.Ignore(i => i.OutstandingAmount);
        builder.Ignore(i => i.IsPayable);

        // ── Concurrency ───────────────────────────────────────────────────────
        builder.Property(i => i.UpdatedAt).IsConcurrencyToken();

        // ── Tenant FK ─────────────────────────────────────────────────────────
        builder.Property(i => i.TenantId).IsRequired();

        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(i => i.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        // ── Identity ──────────────────────────────────────────────────────────
        builder.Property(i => i.InvoiceNumber).IsRequired().HasMaxLength(50);

        builder.HasIndex(i => new { i.TenantId, i.InvoiceNumber })
            .IsUnique()
            .HasDatabaseName("UX_Invoices_TenantId_InvoiceNumber");

        // ── Customer snapshot ─────────────────────────────────────────────────
        builder.Property(i => i.CustomerName).IsRequired().HasMaxLength(200);
        builder.Property(i => i.CustomerEmail).IsRequired().HasMaxLength(256);

        // ── Status (value object → string conversion) ─────────────────────────
        builder.Property(i => i.Status)
            .IsRequired()
            .HasMaxLength(20)
            .HasConversion(
                status => status.Value,
                value  => InvoiceStatus.From(value));

        builder.HasIndex(i => new { i.TenantId, i.Status })
            .HasDatabaseName("IX_Invoices_TenantId_Status");

        builder.HasIndex(i => new { i.Status, i.DueDate })
            .HasDatabaseName("IX_Invoices_Status_DueDate");

        // ── Dates ─────────────────────────────────────────────────────────────
        builder.Property(i => i.IssuedAt);
        builder.Property(i => i.DueDate);
        builder.Property(i => i.PaidAt);
        builder.Property(i => i.CreatedAt).IsRequired();

        // ── Financial rates ───────────────────────────────────────────────────
        builder.Property(i => i.Currency).IsRequired().HasMaxLength(3).IsFixedLength();
        builder.Property(i => i.TaxRatePercentage).HasPrecision(5, 2);
        builder.Property(i => i.DiscountPercentage).HasPrecision(5, 2);

        // ── PaidAmount (owned Money value object → two columns) ───────────────
        builder.OwnsOne(i => i.PaidAmount, money =>
        {
            money.Property(m => m.Amount)
                .HasColumnName("PaidAmount")
                .HasPrecision(18, 4)
                .IsRequired();
            money.Property(m => m.Currency)
                .HasColumnName("PaidCurrency")
                .HasMaxLength(3)
                .IsFixedLength()
                .IsRequired();
        });

        // ── Notes ─────────────────────────────────────────────────────────────
        builder.Property(i => i.Notes).HasMaxLength(2000);

        // ── CreatedByUser FK ──────────────────────────────────────────────────
        builder.Property(i => i.CreatedByUserId).IsRequired();

        builder.HasOne(i => i.CreatedByUser)
            .WithMany()
            .HasForeignKey(i => i.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        // ── BillingAddress (owned entity → flat columns, nullable) ────────────
        builder.OwnsOne(i => i.BillingAddress, addr =>
        {
            addr.Property(a => a.Line1).HasColumnName("BillingAddress_Line1").HasMaxLength(200);
            addr.Property(a => a.Line2).HasColumnName("BillingAddress_Line2").HasMaxLength(200);
            addr.Property(a => a.City).HasColumnName("BillingAddress_City").HasMaxLength(100);
            addr.Property(a => a.State).HasColumnName("BillingAddress_State").HasMaxLength(100);
            addr.Property(a => a.CountryCode).HasColumnName("BillingAddress_CountryCode").HasMaxLength(2).IsFixedLength();
            addr.Property(a => a.PostalCode).HasColumnName("BillingAddress_PostalCode").HasMaxLength(20);
        });

        // ── LineItems (JSON column via EF 8 owned collection) ─────────────────
        builder.OwnsMany(i => i.LineItems, li =>
        {
            li.ToJson("LineItems");
            li.Property(l => l.Description).HasMaxLength(500).IsRequired();
            li.Property(l => l.Quantity).HasPrecision(18, 4).IsRequired();
            li.Property(l => l.DiscountPercentage).HasPrecision(5, 2).IsRequired();
            li.Property(l => l.ProductReference).HasMaxLength(100);

            li.OwnsOne(l => l.UnitPrice, price =>
            {
                price.Property(p => p.Amount)
                    .HasColumnName("UnitPrice")
                    .HasPrecision(18, 4)
                    .IsRequired();
                price.Property(p => p.Currency)
                    .HasColumnName("UnitPriceCurrency")
                    .HasMaxLength(3)
                    .IsRequired();
            });

            // InvoiceLineItem computed properties — not stored in JSON
            li.Ignore(l => l.GrossAmount);
            li.Ignore(l => l.DiscountAmount);
            li.Ignore(l => l.NetAmount);
        });

        // ── Soft delete ───────────────────────────────────────────────────────
        builder.Property(i => i.IsDeleted).IsRequired();
        builder.Property(i => i.DeletedAt);

        builder.HasQueryFilter(i => !i.IsDeleted);

        // ── Additional indexes ────────────────────────────────────────────────
        builder.HasIndex(i => new { i.TenantId, i.CustomerEmail })
            .HasDatabaseName("IX_Invoices_TenantId_CustomerEmail");

        builder.HasIndex(i => new { i.TenantId, i.CreatedAt })
            .HasDatabaseName("IX_Invoices_TenantId_CreatedAt");
        builder.ToTable("Invoices", tb => tb.HasTrigger("trg_Invoices_AfterUpdate"));
        builder.ToTable("Invoices", tb => tb.HasTrigger("trg_Invoices_AfterDelete"));
    }
}
