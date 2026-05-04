using ledgerflowApi.Domain.Entities;
using ledgerflowApi.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace ledgerflowApi.Infrastructure.Persistence.Repositories;

public class TenantRepository : BaseRepository<Tenant>, ITenantRepository
{
    public TenantRepository(ApplicationDbContext context) : base(context) { }

    public async Task<Tenant?> GetBySlugAsync(string slug, CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .FirstOrDefaultAsync(t => t.Slug == slug.ToLowerInvariant(), cancellationToken);
    }

    public async Task<bool> SlugExistsAsync(string slug, CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .AnyAsync(t => t.Slug == slug.ToLowerInvariant(), cancellationToken);
    }

    public async Task<IEnumerable<Tenant>> GetExpiredTrialsAsync(
        DateTime asOf,
        CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .Where(t => t.Status == Domain.Enums.TenantStatus.Trial
                     && t.TrialEndsAt.HasValue
                     && t.TrialEndsAt.Value < asOf)
            .ToListAsync(cancellationToken);
    }
}
