using CleanArch.Domain.Common;

namespace CleanArch.Domain.Events;

/// <summary>
/// Fired when too many failed login attempts lock out a user.
/// Consumers: notify tenant admins, write security audit log.
/// </summary>
public sealed class UserLockedOutEvent : BaseEvent
{
    public Guid UserId { get; }
    public Guid TenantId { get; }

    public UserLockedOutEvent(Guid userId, Guid tenantId)
    {
        UserId = userId;
        TenantId = tenantId;
    }
}
