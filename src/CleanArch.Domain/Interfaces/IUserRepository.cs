using CleanArch.Domain.Entities;

namespace CleanArch.Domain.Interfaces;

/// <summary>Repository contract for User entities.</summary>
public interface IUserRepository : IRepository<User>
{
    /// <summary>
    /// Finds a user by email within a specific tenant.
    /// Email uniqueness is tenant-scoped — the same email can exist across tenants.
    /// </summary>
    Task<User?> GetByEmailAsync(Guid tenantId, string email, CancellationToken cancellationToken = default);

    /// <summary>Checks whether an email is already registered within a tenant.</summary>
    Task<bool> EmailExistsAsync(Guid tenantId, string email, CancellationToken cancellationToken = default);

    /// <summary>Returns all active users for a tenant.</summary>
    Task<IEnumerable<User>> GetByTenantAsync(Guid tenantId, bool includeInactive = false, CancellationToken cancellationToken = default);
}
