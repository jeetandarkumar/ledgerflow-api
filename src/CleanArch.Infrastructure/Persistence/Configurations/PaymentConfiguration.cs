using CleanArch.Domain.Entities;
using CleanArch.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CleanArch.Infrastructure.Persistence.Configurations;

public class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.ToTable("Payments");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();

        // ── Non-mapped properties ─────────────────────────────────────────────
        builder.Ignore(p => p.DomainEvents);
        builder.Ignore(p => p.IsRefund); // computed from Type

        // ── Tenant FK ─────────────────────────────────────────────────────────
        builder.Property(p => p.TenantId).IsRequired();

        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(p => p.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        // ── Invoice FK ────────────────────────────────────────────────────────
        builder.Property(p => p.InvoiceId).IsRequired();

        builder.HasOne(p => p.Invoice)
            .WithMany()
            .HasForeignKey(p => p.InvoiceId)
            .OnDelete(DeleteBehavior.Restrict);

        // ── Self-referential refund FK ────────────────────────────────────────
        builder.Property(p => p.RefundedPaymentId);

        builder.HasOne<Payment>()
            .WithMany()
            .HasForeignKey(p => p.RefundedPaymentId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);

        // ── Amount (owned Money → two columns) ────────────────────────────────
        builder.OwnsOne(p => p.Amount, money =>
        {
            money.Property(m => m.Amount)
                .HasColumnName("Amount")
                .HasPrecision(18, 4)
                .IsRequired();
            money.Property(m => m.Currency)
                .HasColumnName("Currency")
                .HasMaxLength(3)
                .IsFixedLength()
                .IsRequired();
        });

        // ── Status (value object → string conversion) ─────────────────────────
        builder.Property(p => p.Status)
            .IsRequired()
            .HasMaxLength(20)
            .HasConversion(
                s => s.Value,
                v => PaymentStatus.From(v));

        // ── Type (enum → string) ──────────────────────────────────────────────
        builder.Property(p => p.Type)
            .IsRequired()
            .HasMaxLength(20)
            .HasConversion<string>();

        // ── Metadata ──────────────────────────────────────────────────────────
        builder.Property(p => p.PaymentMethod).IsRequired().HasMaxLength(50);
        builder.Property(p => p.ExternalReference).HasMaxLength(200);
        builder.Property(p => p.Notes).HasMaxLength(1000);
        builder.Property(p => p.FailureReason).HasMaxLength(500);
        builder.Property(p => p.CompletedAt);

        // ── InitiatedBy FK (nullable) ─────────────────────────────────────────
        builder.Property(p => p.InitiatedByUserId);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(p => p.InitiatedByUserId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);

        // ── Timestamps ────────────────────────────────────────────────────────
        builder.Property(p => p.CreatedAt).IsRequired();
        builder.Property(p => p.UpdatedAt);
        builder.Property(p => p.IsDeleted).IsRequired();
        builder.Property(p => p.DeletedAt);

        // ── Indexes ───────────────────────────────────────────────────────────

        // Webhook idempotency: unique non-null ExternalReference
        builder.HasIndex(p => p.ExternalReference)
            .IsUnique()
            .HasFilter("[ExternalReference] IS NOT NULL AND [IsDeleted] = 0")
            .HasDatabaseName("UX_Payments_ExternalReference");

        // Primary query pattern: payments for a specific invoice, oldest first
        builder.HasIndex(p => new { p.TenantId, p.InvoiceId, p.CreatedAt })
            .HasDatabaseName("IX_Payments_TenantId_InvoiceId_CreatedAt");

        // Revenue reporting: completed payments by tenant in a date range
        builder.HasIndex(p => new { p.TenantId, p.CompletedAt })
            .HasFilter("[Status] = 'Completed' AND [IsDeleted] = 0")
            .HasDatabaseName("IX_Payments_TenantId_CompletedAt_Completed");

        // Refund lookup: find all refunds for an original payment
        builder.HasIndex(p => p.RefundedPaymentId)
            .HasFilter("[RefundedPaymentId] IS NOT NULL")
            .HasDatabaseName("IX_Payments_RefundedPaymentId");
    }
}
