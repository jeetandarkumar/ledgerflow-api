using CleanArch.Domain.Common;

namespace CleanArch.Domain.Events;

/// <summary>
/// Fired when a completed payment is refunded.
/// Consumers: reverse the invoice payment application, update accounting records.
/// Note: A refund creates a NEW Payment record linked to the original —
/// we never mutate historical payment records.
/// </summary>
public sealed class PaymentRefundedEvent : BaseEvent
{
    public Guid RefundPaymentId { get; }
    public Guid OriginalPaymentId { get; }
    public Guid InvoiceId { get; }
    public Guid TenantId { get; }

    public PaymentRefundedEvent(Guid refundPaymentId, Guid originalPaymentId, Guid invoiceId, Guid tenantId)
    {
        RefundPaymentId = refundPaymentId;
        OriginalPaymentId = originalPaymentId;
        InvoiceId = invoiceId;
        TenantId = tenantId;
    }
}
