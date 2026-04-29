using CleanArch.Domain.Common;

namespace CleanArch.Domain.Events;

/// <summary>
/// Fired when a tenant is suspended (usually failed payment).
/// Consumers: send payment-failure notification, restrict API access.
/// </summary>
public sealed class TenantSuspendedEvent : BaseEvent
{
    public Guid TenantId { get; }
    public string Reason { get; }

    public TenantSuspendedEvent(Guid tenantId, string reason)
    {
        TenantId = tenantId;
        Reason = reason;
    }
}
