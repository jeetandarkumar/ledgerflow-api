namespace CleanArch.Application.Features.Invoices.DTOs;

/// <summary>Request body for POST /api/v1/invoices/{id}/issue</summary>
public sealed class IssueInvoiceRequest
{
    /// <summary>Payment due date. Must be in the future.</summary>
    public DateTime DueDate { get; init; }

    /// <summary>Optional billing address snapshot. Captured at issuance time.</summary>
    public IssueInvoiceBillingAddressRequest? BillingAddress { get; init; }
}

public sealed class IssueInvoiceBillingAddressRequest
{
    public string Line1 { get; init; } = string.Empty;
    public string? Line2 { get; init; }
    public string City { get; init; } = string.Empty;
    public string? State { get; init; }
    public string CountryCode { get; init; } = string.Empty;
    public string PostalCode { get; init; } = string.Empty;
}

/// <summary>Request body for POST /api/v1/invoices/{id}/void</summary>
public sealed class VoidInvoiceRequest
{
    /// <summary>
    /// Mandatory reason for voiding. Min 10 chars, max 500 chars.
    /// Required for audit trail compliance.
    /// </summary>
    public string Reason { get; init; } = string.Empty;
}

/// <summary>Request body for POST /api/v1/invoices/{id}/payments</summary>
public sealed class ProcessPaymentRequest
{
    /// <summary>Payment amount. Must be greater than zero.</summary>
    public decimal Amount { get; init; }

    /// <summary>ISO 4217 currency code. Must match the invoice currency.</summary>
    public string Currency { get; init; } = string.Empty;

    /// <summary>Payment method: "card", "bank_transfer", "cash", "cheque", etc.</summary>
    public string PaymentMethod { get; init; } = string.Empty;

    /// <summary>
    /// Payment processor transaction ID (e.g. Stripe charge_xxx).
    /// Used for idempotency — submitting the same ExternalReference twice
    /// returns the existing payment rather than double-processing.
    /// </summary>
    public string? ExternalReference { get; init; }

    /// <summary>"Standard" for normal payments, "Refund" for refunds.</summary>
    public string Type { get; init; } = "Standard";

    /// <summary>Required when Type = "Refund". The payment being refunded.</summary>
    public Guid? RefundedPaymentId { get; init; }

    /// <summary>Optional notes about this payment.</summary>
    public string? Notes { get; init; }
}
