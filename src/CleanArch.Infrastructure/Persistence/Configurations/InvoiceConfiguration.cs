using CleanArch.Domain.Entities;
using CleanArch.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CleanArch.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core mapping for the Invoice aggregate.
///
/// Key decisions:
/// - Money value objects (PaidAmount) are mapped as owned types with two columns:
///   Amount (decimal) + Currency (string). EF Core Owned Entities handle this natively.
/// - InvoiceStatus is stored as a string column (not int) for readability in the DB
///   and resilience against enum reordering bugs.
/// - LineItems are stored as a JSON column (EF 8 ToJson()) rather than a separate table.
///   They are value objects with no independent identity, queried only through their
///   parent invoice, and the JSON column keeps the schema simple and reads fast.
/// - The soft-delete global query filter ensures IsDeleted=true rows are invisible
///   to all LINQ queries unless explicitly overridden with IgnoreQueryFilters().
/// - Precision(18,4) on all decimal financial columns: 18 digits total, 4 decimal places.
///   Standard for financial systems — enough precision for any real-world amount,
///   4dp supports currencies like KWD (3dp) with room to spare.
/// </summary>
public class InvoiceConfiguration : IEntityTypeConfiguration<Invoice>
{
    public void Configure(EntityTypeBuilder<Invoice> builder)
    {
        builder.ToTable("Invoices");

        // ── Primary Key ───────────────────────────────────────────────────────
        builder.HasKey(i => i.Id);

        builder.Property(i => i.Id)
            .ValueGeneratedNever(); // We assign GUIDs in the domain, not the DB

        // ── Concurrency Token ─────────────────────────────────────────────────
        // Prevents lost-update bugs when two requests modify the same invoice simultaneously.
        // EF Core includes this in every UPDATE WHERE clause automatically.
        builder.Property(i => i.UpdatedAt)
            .IsConcurrencyToken();

        // ── Tenant FK ─────────────────────────────────────────────────────────
        builder.Property(i => i.TenantId)
            .IsRequired();

        // Index for the most common query pattern: "all invoices for tenant X"
        builder.HasIndex(i => i.TenantId)
            .HasDatabaseName("IX_Invoices_TenantId");

        // ── Invoice Number ────────────────────────────────────────────────────
        // Unique within a tenant but NOT globally — composite unique index.
        builder.Property(i => i.InvoiceNumber)
            .IsRequired()
            .HasMaxLength(50);

        builder.HasIndex(i => new { i.TenantId, i.InvoiceNumber })
            .IsUnique()
            .HasDatabaseName("UX_Invoices_TenantId_InvoiceNumber");

        // ── Customer ──────────────────────────────────────────────────────────
        builder.Property(i => i.CustomerName)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(i => i.CustomerEmail)
            .IsRequired()
            .HasMaxLength(256);

        // Index to efficiently find all invoices for a specific customer email
        builder.HasIndex(i => new { i.TenantId, i.CustomerEmail })
            .HasDatabaseName("IX_Invoices_TenantId_CustomerEmail");

        // ── Status ────────────────────────────────────────────────────────────
        // Store as string for DB readability; parse back via InvoiceStatus.From()
        builder.Property(i => i.Status)
            .IsRequired()
            .HasMaxLength(20)
            .HasConversion(
                status => status.Value,
                value => InvoiceStatus.From(value));

        builder.HasIndex(i => new { i.TenantId, i.Status })
            .HasDatabaseName("IX_Invoices_TenantId_Status");

        // ── Dates ─────────────────────────────────────────────────────────────
        builder.Property(i => i.CreatedAt).IsRequired();
        builder.Property(i => i.IssuedAt);
        builder.Property(i => i.DueDate);
        builder.Property(i => i.PaidAt);

        // Composite index for the overdue job: find Issued/PartiallyPaid with past DueDate
        builder.HasIndex(i => new { i.Status, i.DueDate })
            .HasDatabaseName("IX_Invoices_Status_DueDate");

        // ── Financial Rates ───────────────────────────────────────────────────
        builder.Property(i => i.Currency)
            .IsRequired()
            .HasMaxLength(3)
            .IsFixedLength(); // CHAR(3) — always 3 chars, slightly more efficient than VARCHAR

        builder.Property(i => i.TaxRatePercentage)
            .HasPrecision(5, 2); // e.g. 99.99%

        builder.Property(i => i.DiscountPercentage)
            .HasPrecision(5, 2);

        // ── PaidAmount (Owned Money value object) ─────────────────────────────
        // Maps to two columns: PaidAmount_Amount and PaidAmount_Currency
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
        builder.Property(i => i.Notes)
            .HasMaxLength(2000);

        // ── Created By ────────────────────────────────────────────────────────
        builder.Property(i => i.CreatedByUserId)
            .IsRequired();

        // ── Billing Address (Owned type — stored as flat columns) ─────────────
        // NULL columns when BillingAddress is not set.
        builder.OwnsOne(i => i.BillingAddress, address =>
        {
            address.Property(a => a.Line1)
                .HasColumnName("BillingAddress_Line1")
                .HasMaxLength(200);

            address.Property(a => a.Line2)
                .HasColumnName("BillingAddress_Line2")
                .HasMaxLength(200);

            address.Property(a => a.City)
                .HasColumnName("BillingAddress_City")
                .HasMaxLength(100);

            address.Property(a => a.State)
                .HasColumnName("BillingAddress_State")
                .HasMaxLength(100);

            address.Property(a => a.CountryCode)
                .HasColumnName("BillingAddress_CountryCode")
                .HasMaxLength(2)
                .IsFixedLength();

            address.Property(a => a.PostalCode)
                .HasColumnName("BillingAddress_PostalCode")
                .HasMaxLength(20);
        });

        // ── Line Items (JSON column — EF 8 owned entity collection) ──────────
        // Stored as a single JSON column. No separate table, no JOIN needed.
        // Each line item's Money (UnitPrice) is also flattened into the JSON.
        builder.OwnsMany(i => i.LineItems, lineItem =>
        {
            lineItem.ToJson("LineItems");

            lineItem.Property(li => li.Description)
                .HasMaxLength(500)
                .IsRequired();

            lineItem.Property(li => li.Quantity)
                .HasPrecision(18, 4)
                .IsRequired();

            lineItem.Property(li => li.DiscountPercentage)
                .HasPrecision(5, 2)
                .IsRequired();

            lineItem.Property(li => li.ProductReference)
                .HasMaxLength(100);

            lineItem.OwnsOne(li => li.UnitPrice, unitPrice =>
            {
                unitPrice.Property(m => m.Amount)
                    .HasColumnName("UnitPrice")
                    .HasPrecision(18, 4)
                    .IsRequired();

                unitPrice.Property(m => m.Currency)
                    .HasColumnName("UnitPriceCurrency")
                    .HasMaxLength(3)
                    .IsRequired();
            });
        });

        // ── Soft Delete ───────────────────────────────────────────────────────
        // Applied automatically to all LINQ queries on this entity.
        // Use .IgnoreQueryFilters() when you need to see deleted records (admin tools, auditing).
        builder.HasQueryFilter(i => !i.IsDeleted);

        // ── Navigation: do NOT configure a cascade delete from Tenant → Invoice ──
        // Tenant cancellation does NOT delete invoices. Financial records are retained.
        // The Tenant FK is configured with NoAction — application code must handle this.
        builder.HasOne<Domain.Entities.Tenant>()
            .WithMany()
            .HasForeignKey(i => i.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(i => i.CreatedByUser)
            .WithMany()
            .HasForeignKey(i => i.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
