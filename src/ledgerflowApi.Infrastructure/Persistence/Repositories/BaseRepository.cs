using ledgerflowApi.Domain.Common;
using ledgerflowApi.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;

namespace ledgerflowApi.Infrastructure.Persistence.Repositories;

/// <summary>
/// Generic EF Core repository base.
///
/// FIX: SaveChangesAsync is intentionally NOT called in AddAsync, UpdateAsync, or DeleteAsync.
/// All persistence is flushed by IUnitOfWork.ExecuteInTransactionAsync, which calls
/// SaveChangesAsync exactly once at commit time. Calling it here would:
///   - Create multiple DB round-trips inside a single transaction.
///   - Break the "flush at commit" contract — AuditLog and entity writes must be
///     atomic; a crash between two SaveChanges calls would leave partial state.
///
/// For callers outside a UnitOfWork transaction (background jobs, one-off writes),
/// call SaveChangesAsync on the DbContext explicitly after the repository operation.
/// </summary>
public class BaseRepository<T> : IRepository<T> where T : BaseEntity
{
    protected readonly ApplicationDbContext _context;
    protected readonly DbSet<T> _dbSet;

    public BaseRepository(ApplicationDbContext context)
    {
        _context = context;
        _dbSet = context.Set<T>();
    }

    public virtual async Task<T?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => await _dbSet.FirstOrDefaultAsync(e => e.Id == id, cancellationToken);

    public virtual async Task<IEnumerable<T>> GetAllAsync(CancellationToken cancellationToken = default)
        => await _dbSet.ToListAsync(cancellationToken);

    public virtual async Task<IEnumerable<T>> FindAsync(
        Expression<Func<T, bool>> predicate,
        CancellationToken cancellationToken = default)
        => await _dbSet.Where(predicate).ToListAsync(cancellationToken);

    /// <remarks>
    /// Stages the entity for insert. Does NOT call SaveChangesAsync.
    /// UnitOfWork.ExecuteInTransactionAsync flushes all staged changes at commit time.
    /// </remarks>
    public virtual async Task<T> AddAsync(T entity, CancellationToken cancellationToken = default)
    {
        await _dbSet.AddAsync(entity, cancellationToken);
        return entity;
    }

    /// <remarks>
    /// Marks the entity as modified. Does NOT call SaveChangesAsync.
    /// </remarks>
    public virtual Task UpdateAsync(T entity, CancellationToken cancellationToken = default)
    {
        _dbSet.Update(entity);
        return Task.CompletedTask;
    }

    /// <remarks>
    /// Marks the entity for deletion. Does NOT call SaveChangesAsync.
    /// </remarks>
    public virtual Task DeleteAsync(T entity, CancellationToken cancellationToken = default)
    {
        _dbSet.Remove(entity);
        return Task.CompletedTask;
    }

    public virtual async Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken = default)
        => await _dbSet.AnyAsync(e => e.Id == id, cancellationToken);
}
