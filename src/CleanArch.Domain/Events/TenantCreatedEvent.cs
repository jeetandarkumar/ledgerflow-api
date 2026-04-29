using CleanArch.Domain.Common;

namespace CleanArch.Domain.Events;

/// <summary>
/// Fired after a new tenant is successfully created.
/// Consumers: send welcome email, provision default settings, seed admin user.
/// </summary>
public sealed class TenantCreatedEvent : BaseEvent
{
    public Guid TenantId { get; }
    public string TenantName { get; }

    public TenantCreatedEvent(Guid tenantId, string tenantName)
    {
        TenantId = tenantId;
        TenantName = tenantName;
    }
}
