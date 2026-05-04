using ledgerflowApi.Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;

namespace ledgerflowApi.Infrastructure.Persistence;

/// <summary>
/// EF Core implementation of IUnitOfWork.
///
/// How it works:
/// 1. Begins an explicit EF Core database transaction (maps to BEGIN TRANSACTION in SQL).
/// 2. Executes the caller's lambda — all repository operations within the lambda
///    use the same DbContext and therefore participate in the same transaction.
/// 3. On success: calls SaveChangesAsync (flush pending changes) then COMMIT.
/// 4. On any exception: rolls back, disposes the transaction, and re-throws.
///
/// SaveChanges strategy:
/// BaseRepository.AddAsync calls SaveChangesAsync individually, which in the
/// context of an open transaction just flushes to the DB but does NOT commit
/// (EF Core's SaveChanges within a transaction = flush, not commit).
/// The COMMIT only happens when UnitOfWork calls CommitAsync.
///
/// Nested transaction handling:
/// If a transaction is already open (e.g. nested UoW call), we don't open another —
/// we join the existing one. This prevents "there is already an open DataReader"
/// errors and ensures nested operations don't create savepoints unexpectedly.
/// </summary>
public class UnitOfWork : IUnitOfWork
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<UnitOfWork> _logger;

    public UnitOfWork(ApplicationDbContext context, ILogger<UnitOfWork> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task ExecuteInTransactionAsync(
        Func<Task> operation,
        CancellationToken cancellationToken = default)
    {
        await ExecuteInTransactionAsync(async () =>
        {
            await operation();
            return true; // Dummy return value for the generic overload
        }, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<T> ExecuteInTransactionAsync<T>(
        Func<Task<T>> operation,
        CancellationToken cancellationToken = default)
    {
        // If a transaction is already open (nested call), join it rather than starting a new one.
        // This respects the outermost transaction boundary.
        if (_context.Database.CurrentTransaction is not null)
        {
            _logger.LogDebug("Joining existing transaction for nested UnitOfWork scope.");
            return await operation();
        }

        // Use EF Core's CreateExecutionStrategy to handle transient SQL failures
        // (e.g. connection drops, SQL Server failover) with automatic retries.
        // This is especially important when running against Azure SQL.
        var strategy = _context.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            await using IDbContextTransaction transaction =
                await _context.Database.BeginTransactionAsync(cancellationToken);

            _logger.LogDebug(
                "Transaction {TransactionId} begun.",
                transaction.TransactionId);

            try
            {
                var result = await operation();

                // Flush all pending EF Core change tracker entries to the DB.
                // This is the final SaveChanges before the commit — all writes
                // that occurred during the operation are flushed here.
                await _context.SaveChangesAsync(cancellationToken);

                await transaction.CommitAsync(cancellationToken);

                _logger.LogDebug(
                    "Transaction {TransactionId} committed.",
                    transaction.TransactionId);

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Transaction {TransactionId} rolling back due to exception: {Message}",
                    transaction.TransactionId,
                    ex.Message);

                await transaction.RollbackAsync(cancellationToken);
                throw; // Always re-throw — never swallow transactional failures
            }
        });
    }
}
