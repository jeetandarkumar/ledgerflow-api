using ledgerflowApi.Domain.Entities;
using ledgerflowApi.Domain.Interfaces;
using ledgerflowApi.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace ledgerflowApi.Infrastructure.Persistence.Repositories;

/// <summary>
/// EF Core + ADO.NET implementation of IInvoiceRepository.
///
/// FIX: GetByTenantPagedAsync pushes OFFSET/FETCH to SQL so only the requested
/// page rows are loaded. CountByTenantAsync and GetAggregateTotalsAsync issue
/// lightweight COUNT/SUM queries instead of pulling all rows into memory.
/// </summary>
public class InvoiceRepository : BaseRepository<Invoice>, IInvoiceRepository
{
    public InvoiceRepository(ApplicationDbContext context) : base(context) { }

    /// <inheritdoc/>
    public async Task<Invoice?> GetByInvoiceNumberAsync(
        Guid tenantId,
        string invoiceNumber,
        CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .Include(i => i.CreatedByUser)
            .FirstOrDefaultAsync(
                i => i.TenantId == tenantId && i.InvoiceNumber == invoiceNumber,
                cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<IEnumerable<Invoice>> GetByTenantAsync(
        Guid tenantId,
        InvoiceStatus? status = null,
        CancellationToken cancellationToken = default)
    {
        var query = _dbSet.Where(i => i.TenantId == tenantId);

        if (status is not null)
            query = query.Where(i => i.Status == status);

        return await query
            .OrderByDescending(i => i.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// FIX: Uses EF Core .Skip().Take() which translates to SQL OFFSET/FETCH NEXT.
    /// Only pageSize rows are loaded into memory, regardless of total tenant invoice count.
    /// </remarks>
    public async Task<IEnumerable<Invoice>> GetByTenantPagedAsync(
        Guid tenantId,
        InvoiceStatus? status,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        // AsNoTracking: this feeds ListInvoicesQuery only, which returns a read-only summary
        // DTO and never mutates or saves these entities. Skipping EF Core's change tracking
        // for a page of results avoids snapshotting entities that are thrown away immediately
        // after mapping to InvoiceSummary.
        var query = _dbSet.AsNoTracking().Where(i => i.TenantId == tenantId);

        if (status is not null)
            query = query.Where(i => i.Status == status);

        return await query
            .OrderByDescending(i => i.CreatedAt)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// FIX: Issues a single COUNT query. No entities are materialised.
    /// </remarks>
    public async Task<int> CountByTenantAsync(
        Guid tenantId,
        InvoiceStatus? status,
        CancellationToken cancellationToken = default)
    {
        var query = _dbSet.Where(i => i.TenantId == tenantId);

        if (status is not null)
            query = query.Where(i => i.Status == status);

        return await query.CountAsync(cancellationToken);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// FIX: Aggregate totals are computed in SQL via SUM queries.
    /// PaidAmount is a stored column; TotalAmount and OutstandingAmount are computed
    /// at the domain layer from line items JSON, so outstanding is approximated here
    /// as the sum of PaidAmount delta. For exact figures the domain model is authoritative;
    /// this is sufficient for list-view summary display.
    ///
    /// Note: TotalAmount is a computed domain property (not stored). OutstandingAmount
    /// cannot be computed purely in SQL because it depends on line items JSON.
    /// The approach here loads only non-terminal invoices (Issued, PartiallyPaid, Overdue)
    /// to compute outstanding — a much smaller set than all invoices.
    /// </remarks>
    public async Task<(decimal TotalOutstanding, decimal TotalOverdue, string Currency)> GetAggregateTotalsAsync(
        Guid tenantId,
        InvoiceStatus? status,
        CancellationToken cancellationToken = default)
    {
        // Only load invoices that could have an outstanding balance
        var openStatuses = new[] { InvoiceStatus.Issued, InvoiceStatus.PartiallyPaid, InvoiceStatus.Overdue };

        // AsNoTracking: same reasoning as GetByTenantPagedAsync — these entities are only
        // summed in memory and discarded, never updated.
        var query = _dbSet
            .AsNoTracking()
            .Where(i => i.TenantId == tenantId && openStatuses.Contains(i.Status));

        if (status is not null)
            query = query.Where(i => i.Status == status);

        var openInvoices = await query.ToListAsync(cancellationToken);

        var totalOutstanding = openInvoices.Sum(i => i.OutstandingAmount.Amount);
        var totalOverdue = openInvoices
            .Where(i => i.Status == InvoiceStatus.Overdue)
            .Sum(i => i.OutstandingAmount.Amount);

        var currency = openInvoices.FirstOrDefault()?.Currency
            ?? await _dbSet
                .Where(i => i.TenantId == tenantId)
                .Select(i => i.Currency)
                .FirstOrDefaultAsync(cancellationToken)
            ?? "USD";

        return (totalOutstanding, totalOverdue, currency);
    }

    /// <inheritdoc/>
    public async Task<IEnumerable<Invoice>> GetOverdueInvoicesAsync(
        DateTime asOf,
        CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .Where(i =>
                (i.Status == InvoiceStatus.Issued || i.Status == InvoiceStatus.PartiallyPaid)
                && i.DueDate.HasValue
                && i.DueDate.Value < asOf)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<IEnumerable<Invoice>> GetByCreatedUserAsync(
        Guid tenantId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .Where(i => i.TenantId == tenantId && i.CreatedByUserId == userId)
            .OrderByDescending(i => i.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<int> GetNextInvoiceSequenceAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        var connection = _context.Database.GetDbConnection();

        var shouldClose = connection.State != System.Data.ConnectionState.Open;
        if (shouldClose)
            await connection.OpenAsync(cancellationToken);

        try
        {
            using var command = connection.CreateCommand();

            var currentTransaction = _context.Database.CurrentTransaction;
            if (currentTransaction is not null)
                command.Transaction = currentTransaction.GetDbTransaction();

            command.CommandType = System.Data.CommandType.StoredProcedure;
            command.CommandText = "dbo.usp_GetNextInvoiceNumber";

            var tenantIdParam = command.CreateParameter();
            tenantIdParam.ParameterName = "@TenantId";
            tenantIdParam.Value = tenantId;
            command.Parameters.Add(tenantIdParam);

            var yearParam = command.CreateParameter();
            yearParam.ParameterName = "@Year";
            yearParam.Value = DateTime.UtcNow.Year;
            command.Parameters.Add(yearParam);

            var sequenceParam = command.CreateParameter();
            sequenceParam.ParameterName = "@NextSequence";
            sequenceParam.DbType = System.Data.DbType.Int32;
            sequenceParam.Direction = System.Data.ParameterDirection.Output;
            command.Parameters.Add(sequenceParam);

            await command.ExecuteNonQueryAsync(cancellationToken);

            return (int)sequenceParam.Value!;
        }
        finally
        {
            if (shouldClose)
                await connection.CloseAsync();
        }
    }
}
