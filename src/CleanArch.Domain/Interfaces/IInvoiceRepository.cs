using CleanArch.Domain.Entities;
using CleanArch.Domain.ValueObjects;

namespace CleanArch.Domain.Interfaces;

/// <summary>
/// Repository contract for Invoice aggregates.
/// All queries are scoped to a single tenant — never return cross-tenant data.
/// </summary>
public interface IInvoiceRepository : IRepository<Invoice>
{
    /// <summary>
    /// Returns an invoice by its human-readable number within a tenant.
    /// InvoiceNumber is unique per tenant but NOT globally unique.
    /// </summary>
    Task<Invoice?> GetByInvoiceNumberAsync(Guid tenantId, string invoiceNumber, CancellationToken cancellationToken = default);

    /// <summary>Returns all invoices for a given tenant, optionally filtered by status.</summary>
    Task<IEnumerable<Invoice>> GetByTenantAsync(Guid tenantId, InvoiceStatus? status = null, CancellationToken cancellationToken = default);

    /// <summary>Returns all unpaid invoices that are past their due date, for the overdue job.</summary>
    Task<IEnumerable<Invoice>> GetOverdueInvoicesAsync(DateTime asOf, CancellationToken cancellationToken = default);

    /// <summary>Returns all invoices created by a specific user within a tenant.</summary>
    Task<IEnumerable<Invoice>> GetByCreatedUserAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the next sequential invoice number for a tenant.
    /// Used to generate gap-free INV-YYYY-NNNN style numbers.
    /// </summary>
    Task<int> GetNextInvoiceSequenceAsync(Guid tenantId, CancellationToken cancellationToken = default);
}
