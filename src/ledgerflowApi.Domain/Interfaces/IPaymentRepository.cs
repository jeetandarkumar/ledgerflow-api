using ledgerflowApi.Domain.Entities;

namespace ledgerflowApi.Domain.Interfaces;

/// <summary>
/// Repository contract for Payment records.
/// Payments are append-only — no update or delete operations are exposed.
/// </summary>
public interface IPaymentRepository : IRepository<Payment>
{
    /// <summary>Returns all payments for a specific invoice.</summary>
    Task<IEnumerable<Payment>> GetByInvoiceAsync(Guid tenantId, Guid invoiceId, CancellationToken cancellationToken = default);

    /// <summary>Returns a payment by its external processor reference (e.g. Stripe charge ID).</summary>
    Task<Payment?> GetByExternalReferenceAsync(string externalReference, CancellationToken cancellationToken = default);

    /// <summary>Returns all refunds linked to a specific original payment.</summary>
    Task<IEnumerable<Payment>> GetRefundsForPaymentAsync(Guid originalPaymentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns all completed payments within a tenant for a given date range.
    /// Used for revenue reporting and reconciliation.
    /// </summary>
    Task<IEnumerable<Payment>> GetCompletedByTenantAsync(Guid tenantId, DateTime from, DateTime to, CancellationToken cancellationToken = default);
}
