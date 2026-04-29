using CleanArch.Domain.Common;

namespace CleanArch.Domain.Events;

/// <summary>
/// Fired when a tenant transitions from Trial → Active.
/// Consumers: disable trial limitations, trigger onboarding sequence.
/// </summary>
public sealed class TenantActivatedEvent : BaseEvent
{
    public Guid TenantId { get; }
    public TenantActivatedEvent(Guid tenantId) => TenantId = tenantId;
}
