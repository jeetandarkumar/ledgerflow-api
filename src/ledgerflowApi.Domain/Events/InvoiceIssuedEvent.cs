using ledgerflowApi.Domain.Common;

namespace ledgerflowApi.Domain.Events;

/// <summary>
/// Fired when a draft invoice is issued (sent to the customer).
/// Consumers: send invoice email to customer, write audit log.
/// </summary>
public sealed class InvoiceIssuedEvent : BaseEvent
{
    public Guid InvoiceId { get; }
    public Guid TenantId { get; }
    public string InvoiceNumber { get; }
    public string CustomerEmail { get; }

    public InvoiceIssuedEvent(Guid invoiceId, Guid tenantId, string invoiceNumber, string customerEmail)
    {
        InvoiceId = invoiceId;
        TenantId = tenantId;
        InvoiceNumber = invoiceNumber;
        CustomerEmail = customerEmail;
    }
}
