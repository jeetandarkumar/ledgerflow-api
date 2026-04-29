namespace CleanArch.Application.Features.Invoices.DTOs;

/// <summary>
/// Full invoice representation returned by the API.
/// All computed financial figures are included so clients don't have
/// to re-implement the calculation logic on the frontend.
/// </summary>
public sealed class InvoiceResponse
{
    public Guid Id { get; init; }
    public string InvoiceNumber { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;

    // ── Customer ──────────────────────────────────────────────────────────
    public string CustomerName { get; init; } = string.Empty;
    public string CustomerEmail { get; init; } = string.Empty;
    public InvoiceBillingAddressResponse? BillingAddress { get; init; }

    // ── Dates ─────────────────────────────────────────────────────────────
    public DateTime CreatedAt { get; init; }
    public DateTime? IssuedAt { get; init; }
    public DateTime? DueDate { get; init; }
    public DateTime? PaidAt { get; init; }

    // ── Financials ────────────────────────────────────────────────────────
    public string Currency { get; init; } = string.Empty;
    public decimal TaxRatePercentage { get; init; }
    public decimal DiscountPercentage { get; init; }

    /// <summary>Sum of all line net amounts before invoice-level discount and tax.</summary>
    public decimal Subtotal { get; init; }

    /// <summary>Monetary value of the invoice-level discount.</summary>
    public decimal InvoiceDiscountAmount { get; init; }

    /// <summary>Subtotal after invoice-level discount, before tax.</summary>
    public decimal DiscountedSubtotal { get; init; }

    /// <summary>Tax amount calculated on the discounted subtotal.</summary>
    public decimal TaxAmount { get; init; }

    /// <summary>Grand total: what the customer owes.</summary>
    public decimal TotalAmount { get; init; }

    /// <summary>Amount paid so far.</summary>
    public decimal PaidAmount { get; init; }

    /// <summary>Remaining balance (TotalAmount − PaidAmount).</summary>
    public decimal OutstandingAmount { get; init; }

    // ── Lines ─────────────────────────────────────────────────────────────
    public List<InvoiceLineItemResponse> LineItems { get; init; } = [];

    // ── Metadata ──────────────────────────────────────────────────────────
    public string? Notes { get; init; }
    public Guid CreatedByUserId { get; init; }
}

public sealed class InvoiceLineItemResponse
{
    public string Description { get; init; } = string.Empty;
    public decimal UnitPrice { get; init; }
    public decimal Quantity { get; init; }
    public decimal DiscountPercentage { get; init; }
    public string? ProductReference { get; init; }

    /// <summary>UnitPrice × Quantity before any discount.</summary>
    public decimal GrossAmount { get; init; }

    /// <summary>Monetary value of the line discount.</summary>
    public decimal DiscountAmount { get; init; }

    /// <summary>Net amount the customer pays for this line.</summary>
    public decimal NetAmount { get; init; }
}

public sealed class InvoiceBillingAddressResponse
{
    public string Line1 { get; init; } = string.Empty;
    public string? Line2 { get; init; }
    public string City { get; init; } = string.Empty;
    public string? State { get; init; }
    public string CountryCode { get; init; } = string.Empty;
    public string PostalCode { get; init; } = string.Empty;
}
