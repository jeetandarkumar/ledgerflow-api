namespace ledgerflowApi.Domain.Enums;

/// <summary>
/// Controls what a user can do within their tenant.
/// SuperAdmin is reserved for platform-level operations (Anthropic staff equivalent),
/// not tenant users — a tenant Admin is the highest role a customer can hold.
/// </summary>
public enum UserRole
{
    /// <summary>Read-only access. Can view invoices and payments, cannot create or modify.</summary>
    Viewer = 0,

    /// <summary>Standard user. Can create and manage invoices they own.</summary>
    Member = 1,

    /// <summary>Tenant administrator. Full access within the tenant — can manage users, settings, all invoices.</summary>
    Admin = 2,

    /// <summary>Platform-level operator. Can access all tenants. Never assigned to customer accounts.</summary>
    SuperAdmin = 99
}
