using ledgerflowApi.Domain.Entities;
using ledgerflowApi.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace ledgerflowApi.Infrastructure.Persistence.Repositories;

/// <summary>
/// EF Core repository for User entities.
/// ALL queries include TenantId — email uniqueness is tenant-scoped,
/// and returning users from the wrong tenant would be a data breach.
/// </summary>
public class UserRepository : BaseRepository<User>, IUserRepository
{
    public UserRepository(ApplicationDbContext context) : base(context) { }

    /// <inheritdoc/>
    /// <remarks>
    /// Email lookup is always tenant-scoped. Two tenants can share an email address
    /// (e.g. a consultant who works with multiple companies on this platform).
    /// Without the TenantId filter, the first match from any tenant would be returned — wrong.
    /// </remarks>
    public async Task<User?> GetByEmailAsync(
        Guid tenantId,
        string email,
        CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .FirstOrDefaultAsync(
                u => u.TenantId == tenantId
                  && u.Email == email.ToLowerInvariant(),
                cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<bool> EmailExistsAsync(
        Guid tenantId,
        string email,
        CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .AnyAsync(
                u => u.TenantId == tenantId
                  && u.Email == email.ToLowerInvariant(),
                cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<IEnumerable<User>> GetByTenantAsync(
        Guid tenantId,
        bool includeInactive = false,
        CancellationToken cancellationToken = default)
    {
        var query = _dbSet.Where(u => u.TenantId == tenantId);

        if (!includeInactive)
            query = query.Where(u => u.IsActive);

        return await query
            .OrderBy(u => u.LastName)
            .ThenBy(u => u.FirstName)
            .ToListAsync(cancellationToken);
    }
}
