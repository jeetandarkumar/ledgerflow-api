using ledgerflowApi.Domain.Entities;
using ledgerflowApi.Domain.ValueObjects;

namespace ledgerflowApi.Domain.Interfaces;

/// <summary>
/// Repository contract for Invoice aggregates.
/// All queries are tenant-scoped — never return cross-tenant data.
/// </summary>
public interface IInvoiceRepository : IRepository<Invoice>
{
    /// <summary>
    /// Returns an invoice by its human-readable number within a tenant.
    /// </summary>
    Task<Invoice?> GetByInvoiceNumberAsync(Guid tenantId, string invoiceNumber, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns ALL invoices for a tenant with optional status filter (no pagination).
    /// Use GetByTenantPagedAsync for user-facing list endpoints.
    /// </summary>
    Task<IEnumerable<Invoice>> GetByTenantAsync(Guid tenantId, InvoiceStatus? status = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// FIX: Returns a single page of invoices. Pagination is pushed to the DB
    /// via OFFSET/FETCH so only the requested rows are loaded into memory.
    /// </summary>
    Task<IEnumerable<Invoice>> GetByTenantPagedAsync(
        Guid tenantId,
        InvoiceStatus? status,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// FIX: Returns the total count of invoices matching the filter.
    /// Used by ListInvoices to build pagination metadata without loading entities.
    /// </summary>
    Task<int> CountByTenantAsync(Guid tenantId, InvoiceStatus? status, CancellationToken cancellationToken = default);

    /// <summary>
    /// FIX: Returns aggregate financial totals (outstanding, overdue, currency)
    /// for the given tenant and filter in a single DB query.
    /// Returns (totalOutstanding, totalOverdue, currency).
    /// </summary>
    Task<(decimal TotalOutstanding, decimal TotalOverdue, string Currency)> GetAggregateTotalsAsync(
        Guid tenantId,
        InvoiceStatus? status,
        CancellationToken cancellationToken = default);

    /// <summary>Returns all unpaid invoices past their due date, for the overdue background job.</summary>
    Task<IEnumerable<Invoice>> GetOverdueInvoicesAsync(DateTime asOf, CancellationToken cancellationToken = default);

    /// <summary>Returns all invoices created by a specific user within a tenant.</summary>
    Task<IEnumerable<Invoice>> GetByCreatedUserAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the next sequential invoice number for a tenant.
    /// Calls usp_GetNextInvoiceNumber with UPDLOCK + HOLDLOCK for concurrency safety.
    /// </summary>
    Task<int> GetNextInvoiceSequenceAsync(Guid tenantId, CancellationToken cancellationToken = default);
}
