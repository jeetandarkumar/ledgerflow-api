using CleanArch.Domain.Common;

namespace CleanArch.Domain.Events;

/// <summary>
/// Fired when an invoice is marked as overdue (past its due date without full payment).
/// Consumers: send overdue reminder email, trigger dunning sequence.
/// </summary>
public sealed class InvoiceOverdueEvent : BaseEvent
{
    public Guid InvoiceId { get; }
    public Guid TenantId { get; }
    public string InvoiceNumber { get; }
    public DateTime DueDate { get; }

    public InvoiceOverdueEvent(Guid invoiceId, Guid tenantId, string invoiceNumber, DateTime dueDate)
    {
        InvoiceId = invoiceId;
        TenantId = tenantId;
        InvoiceNumber = invoiceNumber;
        DueDate = dueDate;
    }
}
