using CleanArch.Domain.Entities;
using CleanArch.Domain.Enums;
using CleanArch.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace CleanArch.Infrastructure.Persistence.Repositories;

/// <summary>
/// EF Core implementation of IAuditLogRepository.
///
/// Write path is intentionally separate from BaseRepository:
/// - No soft-delete filter (audit logs must always be visible)
/// - No Update/Delete operations (append-only contract)
/// - Insert does NOT call SaveChangesAsync immediately — the UnitOfWork
///   controls when the transaction commits so both the Invoice insert
///   and AuditLog insert flush together in one roundtrip.
/// </summary>
public class AuditLogRepository : IAuditLogRepository
{
    private readonly ApplicationDbContext _context;

    public AuditLogRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Does NOT call SaveChangesAsync. The UnitOfWork commits the transaction
    /// which triggers a single SaveChangesAsync covering all pending changes.
    /// Calling SaveChanges here would commit the audit log before the invoice
    /// is saved, leaving a partial state on failure.
    /// </remarks>
    public async Task AddAsync(AuditLog entry, CancellationToken cancellationToken = default)
    {
        await _context.AuditLogs.AddAsync(entry, cancellationToken);
        // SaveChanges intentionally omitted — deferred to UnitOfWork
    }

    /// <inheritdoc/>
    public async Task<IEnumerable<AuditLog>> GetForEntityAsync(
        Guid tenantId,
        string entityType,
        Guid entityId,
        CancellationToken cancellationToken = default)
    {
        return await _context.AuditLogs
            .Where(a =>
                a.TenantId == tenantId &&
                a.EntityType == entityType &&
                a.EntityId == entityId)
            .OrderBy(a => a.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<IEnumerable<AuditLog>> GetRecentByTenantAsync(
        Guid tenantId,
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        return await _context.AuditLogs
            .Where(a => a.TenantId == tenantId)
            .OrderByDescending(a => a.CreatedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<IEnumerable<AuditLog>> GetByUserAsync(
        Guid tenantId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        return await _context.AuditLogs
            .Where(a => a.TenantId == tenantId && a.UserId == userId)
            .OrderByDescending(a => a.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<IEnumerable<AuditLog>> GetByActionAsync(
        Guid tenantId,
        AuditAction action,
        DateTime? from = null,
        DateTime? to = null,
        CancellationToken cancellationToken = default)
    {
        var query = _context.AuditLogs
            .Where(a => a.TenantId == tenantId && a.Action == action);

        if (from.HasValue)
            query = query.Where(a => a.CreatedAt >= from.Value);

        if (to.HasValue)
            query = query.Where(a => a.CreatedAt <= to.Value);

        return await query
            .OrderByDescending(a => a.CreatedAt)
            .ToListAsync(cancellationToken);
    }
}
