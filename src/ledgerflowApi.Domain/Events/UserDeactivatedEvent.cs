using ledgerflowApi.Domain.Common;

namespace ledgerflowApi.Domain.Events;

/// <summary>
/// Fired when a user account is deactivated.
/// Consumers: invalidate active sessions/refresh tokens, write audit log.
/// </summary>
public sealed class UserDeactivatedEvent : BaseEvent
{
    public Guid UserId { get; }
    public Guid TenantId { get; }

    public UserDeactivatedEvent(Guid userId, Guid tenantId)
    {
        UserId = userId;
        TenantId = tenantId;
    }
}
