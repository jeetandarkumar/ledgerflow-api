using ledgerflowApi.Domain.Common;

namespace ledgerflowApi.Domain.Events;

/// <summary>
/// Fired when an invoice is voided.
/// Consumers: reverse any partial payments if needed, write audit log.
/// </summary>
public sealed class InvoiceVoidedEvent : BaseEvent
{
    public Guid InvoiceId { get; }
    public Guid TenantId { get; }
    public string InvoiceNumber { get; }
    public string Reason { get; }

    public InvoiceVoidedEvent(Guid invoiceId, Guid tenantId, string invoiceNumber, string reason)
    {
        InvoiceId = invoiceId;
        TenantId = tenantId;
        InvoiceNumber = invoiceNumber;
        Reason = reason;
    }
}
