using ledgerflowApi.Domain.Entities;
using ledgerflowApi.Domain.Enums;

namespace ledgerflowApi.Domain.Interfaces;

/// <summary>
/// Repository for writing and querying audit log entries.
/// Write path is append-only — no update or delete is exposed.
/// </summary>
public interface IAuditLogRepository
{
    /// <summary>Persists a new audit log entry. Returns immediately (fire-and-forget acceptable).</summary>
    Task AddAsync(AuditLog entry, CancellationToken cancellationToken = default);

    /// <summary>Returns all audit entries for a specific entity record.</summary>
    Task<IEnumerable<AuditLog>> GetForEntityAsync(Guid tenantId, string entityType, Guid entityId, CancellationToken cancellationToken = default);

    /// <summary>Returns recent audit entries for a tenant (for the admin activity feed).</summary>
    Task<IEnumerable<AuditLog>> GetRecentByTenantAsync(Guid tenantId, int limit = 50, CancellationToken cancellationToken = default);

    /// <summary>Returns audit entries for a specific user across the tenant.</summary>
    Task<IEnumerable<AuditLog>> GetByUserAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Returns audit entries filtered by action type (e.g. all login failures).</summary>
    Task<IEnumerable<AuditLog>> GetByActionAsync(Guid tenantId, AuditAction action, DateTime? from = null, DateTime? to = null, CancellationToken cancellationToken = default);
}
