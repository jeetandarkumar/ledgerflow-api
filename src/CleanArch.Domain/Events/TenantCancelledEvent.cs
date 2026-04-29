using CleanArch.Domain.Common;

namespace CleanArch.Domain.Events;

/// <summary>
/// Fired when a tenant cancels their account.
/// Consumers: schedule data retention/deletion job, send goodbye email.
/// </summary>
public sealed class TenantCancelledEvent : BaseEvent
{
    public Guid TenantId { get; }
    public string Reason { get; }

    public TenantCancelledEvent(Guid tenantId, string reason)
    {
        TenantId = tenantId;
        Reason = reason;
    }
}
