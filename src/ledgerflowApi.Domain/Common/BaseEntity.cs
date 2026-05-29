namespace ledgerflowApi.Domain.Common;

/// <summary>
/// Root base for all domain entities.
/// Carries identity, audit timestamps, soft-delete, and domain events.
/// </summary>
public abstract class BaseEntity
{
    public Guid Id { get; protected set; } = Guid.NewGuid();
    public DateTime CreatedAt { get; protected set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; protected set; }

    /// <summary>
    /// Soft-delete flag. Repositories should filter these out by default.
    /// Hard-delete is never used so financial audit history is preserved.
    /// </summary>
    public bool IsDeleted { get; protected set; }
    public DateTime? DeletedAt { get; protected set; }

    private readonly List<BaseEvent> _domainEvents = [];
    public IReadOnlyCollection<BaseEvent> DomainEvents => _domainEvents.AsReadOnly();

    public void AddDomainEvent(BaseEvent domainEvent) => _domainEvents.Add(domainEvent);
    public void RemoveDomainEvent(BaseEvent domainEvent) => _domainEvents.Remove(domainEvent);
    public void ClearDomainEvents() => _domainEvents.Clear();

    protected void SetUpdatedAt() => UpdatedAt = DateTime.UtcNow;

    protected void SoftDelete()
    {
        IsDeleted = true;
        DeletedAt = DateTime.UtcNow;
        SetUpdatedAt();
    }
}
