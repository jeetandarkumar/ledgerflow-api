using CleanArch.Domain.Entities;

namespace CleanArch.Domain.Interfaces;

/// <summary>Repository contract for Tenant — the root of the multi-tenancy hierarchy.</summary>
public interface ITenantRepository : IRepository<Tenant>
{
    /// <summary>
    /// Finds a tenant by its immutable URL slug (e.g. "acme-corp").
    /// Used to resolve tenant context from subdomain/path routing.
    /// </summary>
    Task<Tenant?> GetBySlugAsync(string slug, CancellationToken cancellationToken = default);

    /// <summary>Returns true if the slug is already taken by another tenant.</summary>
    Task<bool> SlugExistsAsync(string slug, CancellationToken cancellationToken = default);

    /// <summary>Returns all tenants whose trial period has expired but are still in Trial status.</summary>
    Task<IEnumerable<Tenant>> GetExpiredTrialsAsync(DateTime asOf, CancellationToken cancellationToken = default);
}
