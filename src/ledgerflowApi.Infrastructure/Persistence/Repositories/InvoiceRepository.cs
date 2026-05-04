using ledgerflowApi.Domain.Entities;
using ledgerflowApi.Domain.Interfaces;
using ledgerflowApi.Domain.ValueObjects;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace ledgerflowApi.Infrastructure.Persistence.Repositories;

/// <summary>
/// EF Core + ADO.NET implementation of IInvoiceRepository.
///
/// Most queries use LINQ/EF Core for type safety and compile-time checking.
/// The invoice sequence generation calls the stored procedure via ADO.NET
/// (SqlCommand with OUTPUT parameter) — EF Core doesn't have clean support
/// for stored procs with OUTPUT parameters, and raw ADO.NET is the right
/// tool for that specific job.
///
/// All queries include TenantId in the WHERE clause. The global query filter
/// in InvoiceConfiguration handles soft-delete filtering automatically.
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
        var query = _dbSet
            .Where(i => i.TenantId == tenantId);

        if (status is not null)
            query = query.Where(i => i.Status == status);

        return await query
            .OrderByDescending(i => i.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<IEnumerable<Invoice>> GetOverdueInvoicesAsync(
        DateTime asOf,
        CancellationToken cancellationToken = default)
    {
        // Candidates for overdue: Issued or PartiallyPaid invoices past their due date.
        // The status transition to Overdue is done by the application layer after this query.
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
    /// <remarks>
    /// Calls the usp_GetNextInvoiceNumber stored procedure via ADO.NET.
    ///
    /// Why not EF Core's ExecuteSqlRaw?
    /// EF Core doesn't provide clean support for stored procedures with OUTPUT
    /// parameters. FromSqlRaw only works for SELECT result sets. Using the raw
    /// SqlConnection gives us full control and is the standard approach for this pattern.
    ///
    /// The stored procedure uses UPDLOCK + HOLDLOCK internally to guarantee
    /// uniqueness under concurrent load — this is the correct place to enforce that.
    /// </remarks>
    public async Task<int> GetNextInvoiceSequenceAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        // Get the underlying ADO.NET connection from EF Core's DbContext.
        // We use the same connection so we participate in the ambient transaction
        // if one has been opened by IUnitOfWork.ExecuteInTransactionAsync.
        var connection = _context.Database.GetDbConnection();

        // Ensure the connection is open. EF Core manages connection lifetime but
        // we need it open for our raw command.
        var shouldClose = connection.State != System.Data.ConnectionState.Open;
        if (shouldClose)
            await connection.OpenAsync(cancellationToken);

        try
        {
            using var command = connection.CreateCommand();

            // Enlist in the current transaction if one exists.
            // This is critical — if the invoice INSERT later rolls back,
            // we want the sequence number to be consumed (gap), not rolled back
            // to a state that could be reused (duplicate).
            var currentTransaction = _context.Database.CurrentTransaction;
            if (currentTransaction is not null)
                command.Transaction = currentTransaction.GetDbTransaction();

            command.CommandType = System.Data.CommandType.StoredProcedure;
            command.CommandText = "dbo.usp_GetNextInvoiceNumber";

            // Input parameters
            var tenantIdParam = command.CreateParameter();
            tenantIdParam.ParameterName = "@TenantId";
            tenantIdParam.Value = tenantId;
            command.Parameters.Add(tenantIdParam);

            var yearParam = command.CreateParameter();
            yearParam.ParameterName = "@Year";
            yearParam.Value = DateTime.UtcNow.Year;
            command.Parameters.Add(yearParam);

            // OUTPUT parameter — this is what we read back
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
