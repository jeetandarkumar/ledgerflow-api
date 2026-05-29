using ledgerflowApi.Domain.Common;

namespace ledgerflowApi.Domain.Events;

/// <summary>
/// Fired when a payment attempt fails.
/// Consumers: notify tenant admin, log for retry handling.
/// </summary>
public sealed class PaymentFailedEvent : BaseEvent
{
    public Guid PaymentId { get; }
    public Guid InvoiceId { get; }
    public Guid TenantId { get; }
    public string FailureReason { get; }

    public PaymentFailedEvent(Guid paymentId, Guid invoiceId, Guid tenantId, string failureReason)
    {
        PaymentId = paymentId;
        InvoiceId = invoiceId;
        TenantId = tenantId;
        FailureReason = failureReason;
    }
}
