using ledgerflowApi.Domain.Common;

namespace ledgerflowApi.Domain.Events;

/// <summary>
/// Fired when a payment is successfully completed.
/// Consumers: apply payment to invoice, update accounting records, send receipt.
/// </summary>
public sealed class PaymentCompletedEvent : BaseEvent
{
    public Guid PaymentId { get; }
    public Guid InvoiceId { get; }
    public Guid TenantId { get; }

    public PaymentCompletedEvent(Guid paymentId, Guid invoiceId, Guid tenantId)
    {
        PaymentId = paymentId;
        InvoiceId = invoiceId;
        TenantId = tenantId;
    }
}
