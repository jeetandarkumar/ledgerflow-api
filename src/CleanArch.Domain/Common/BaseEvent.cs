using MediatR;

namespace CleanArch.Domain.Common;

/// <summary>
/// Base for all domain events. Published after the transaction commits
/// so side-effects (emails, audit logs, webhooks) don't run inside the DB transaction.
/// </summary>
public abstract class BaseEvent : INotification
{
    public DateTime OccurredOn { get; } = DateTime.UtcNow;
}
