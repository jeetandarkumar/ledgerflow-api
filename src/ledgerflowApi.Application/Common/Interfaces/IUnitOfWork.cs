namespace ledgerflowApi.Application.Common.Interfaces;

/// <summary>
/// Coordinates multi-repository writes in a single database transaction.
/// Usage: wrap multiple repo calls in ExecuteInTransactionAsync — all succeed or all roll back.
/// </summary>
public interface IUnitOfWork
{
    Task ExecuteInTransactionAsync(
        Func<Task> operation,
        CancellationToken cancellationToken = default);

    Task<T> ExecuteInTransactionAsync<T>(
        Func<Task<T>> operation,
        CancellationToken cancellationToken = default);
}
