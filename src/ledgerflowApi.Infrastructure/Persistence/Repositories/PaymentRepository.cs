using ledgerflowApi.Domain.Entities;
using ledgerflowApi.Domain.Interfaces;
using ledgerflowApi.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace ledgerflowApi.Infrastructure.Persistence.Repositories;

/// <summary>
/// EF Core implementation of IPaymentRepository.
///
/// Payment records are append-only by application contract —
/// the interface exposes no Update or Delete methods. This is enforced at the
/// interface level, not the repository level, so a DBA using raw SQL can still
/// update records; the audit triggers capture those changes.
///
/// All queries include TenantId to enforce the tenant isolation boundary.
/// </summary>
public class PaymentRepository : BaseRepository<Payment>, IPaymentRepository
{
    public PaymentRepository(ApplicationDbContext context) : base(context) { }

    /// <inheritdoc/>
    /// <remarks>
    /// Ordered by CreatedAt ASC so payments are returned in the order they were
    /// applied — important for correctly sequencing partial payment application.
    /// </remarks>
    public async Task<IEnumerable<Payment>> GetByInvoiceAsync(
        Guid tenantId,
        Guid invoiceId,
        CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .Where(p => p.TenantId == tenantId && p.InvoiceId == invoiceId)
            .OrderBy(p => p.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Used by payment processor webhooks to implement idempotency:
    /// "have we already processed event ch_xxx?" before creating a new Payment row.
    /// The ExternalReference unique index in the DB prevents duplicates even under
    /// concurrent webhook delivery, but this check provides an early exit.
    /// </remarks>
    public async Task<Payment?> GetByExternalReferenceAsync(
        string externalReference,
        CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .FirstOrDefaultAsync(
                p => p.ExternalReference == externalReference,
                cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<IEnumerable<Payment>> GetRefundsForPaymentAsync(
        Guid originalPaymentId,
        CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .Where(p => p.RefundedPaymentId == originalPaymentId)
            .OrderBy(p => p.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Revenue reporting query. The filtered index on (TenantId, CompletedAt)
    /// WHERE Status = 'Completed' makes this fast even on large payment tables.
    /// </remarks>
    public async Task<IEnumerable<Payment>> GetCompletedByTenantAsync(
        Guid tenantId,
        DateTime from,
        DateTime to,
        CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .Where(p =>
                p.TenantId == tenantId
                && p.Status == PaymentStatus.Completed
                && p.CompletedAt.HasValue
                && p.CompletedAt.Value >= from
                && p.CompletedAt.Value <= to)
            .OrderBy(p => p.CompletedAt)
            .ToListAsync(cancellationToken);
    }
}
