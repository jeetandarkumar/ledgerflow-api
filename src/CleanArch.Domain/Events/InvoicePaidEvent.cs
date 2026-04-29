using CleanArch.Domain.Common;

namespace CleanArch.Domain.Events;

/// <summary>
/// Fired when an invoice is fully paid.
/// Consumers: send receipt email, update revenue reports, write audit log.
/// </summary>
public sealed class InvoicePaidEvent : BaseEvent
{
    public Guid InvoiceId { get; }
    public Guid TenantId { get; }
    public string InvoiceNumber { get; }

    public InvoicePaidEvent(Guid invoiceId, Guid tenantId, string invoiceNumber)
    {
        InvoiceId = invoiceId;
        TenantId = tenantId;
        InvoiceNumber = invoiceNumber;
    }
}
