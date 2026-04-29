namespace CleanArch.Domain.Common;

/// <summary>
/// Base for any entity that is scoped to a specific tenant.
/// Every financial record (invoices, payments, etc.) extends this
/// so the tenant boundary is enforced at the type level, not just by convention.
/// </summary>
public abstract class TenantEntity : BaseEntity
{
    /// <summary>
    /// The tenant this record belongs to.
    /// Never null — a financial record without a tenant owner is invalid.
    /// </summary>
    public Guid TenantId { get; protected set; }
}
