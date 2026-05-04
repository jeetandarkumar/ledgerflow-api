namespace ledgerflowApi.Domain.Enums;

/// <summary>
/// Lifecycle status of a tenant account.
/// </summary>
public enum TenantStatus
{
    /// <summary>Tenant has just signed up and is in the free trial window.</summary>
    Trial = 0,

    /// <summary>Active paying customer.</summary>
    Active = 1,

    /// <summary>
    /// Subscription has lapsed (failed payment, expired card).
    /// Tenant can log in and read data but cannot create new invoices or payments.
    /// </summary>
    Suspended = 2,

    /// <summary>
    /// Tenant has cancelled their account. Data is retained for 90 days
    /// then scheduled for deletion per GDPR/retention policy.
    /// </summary>
    Cancelled = 3
}
