namespace ledgerflowApi.Application.Common.Interfaces;

/// <summary>
/// Abstracts database transaction management from the application layer.
///
/// Why this exists:
/// The application layer needs to coordinate multi-repository writes in a single
/// atomic transaction (e.g. Invoice + AuditLog) without taking a dependency on
/// EF Core or any specific persistence library. This interface lets the handler
/// say "do all of this together or none of it" without knowing how that's implemented.
///
/// Usage in handlers:
///   await _unitOfWork.ExecuteInTransactionAsync(async () =>
///   {
///       await _invoiceRepo.AddAsync(invoice, ct);
///       await _auditRepo.AddAsync(auditEntry, ct);
///   }, cancellationToken);
///
/// The infrastructure implementation wraps the lambda in a DbContext transaction,
/// calls SaveChangesAsync once at the end, and rolls back on any exception.
/// </summary>
public interface IUnitOfWork
{
    /// <summary>
    /// Executes <paramref name="operation"/> inside a database transaction.
    /// Commits if the operation completes without throwing; rolls back otherwise.
    /// Any exception from <paramref name="operation"/> propagates to the caller
    /// after the rollback — never swallowed.
    /// </summary>
    Task ExecuteInTransactionAsync(
        Func<Task> operation,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Variant that returns a value from the transactional operation.
    /// </summary>
    Task<T> ExecuteInTransactionAsync<T>(
        Func<Task<T>> operation,
        CancellationToken cancellationToken = default);
}
