using ledgerflowApi.Domain.Common;

namespace ledgerflowApi.Domain.Events;

/// <summary>
/// Fired after a new user is created within a tenant.
/// Consumers: send verification email, write audit log entry.
/// </summary>
public sealed class UserCreatedEvent : BaseEvent
{
    public Guid UserId { get; }
    public Guid TenantId { get; }
    public string Email { get; }

    public UserCreatedEvent(Guid userId, Guid tenantId, string email)
    {
        UserId = userId;
        TenantId = tenantId;
        Email = email;
    }
}
