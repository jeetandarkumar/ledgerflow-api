namespace ledgerflowApi.Application.Features.Invoices.DTOs;

/// <summary>
/// The JSON body the client POSTs to /api/v1/invoices.
///
/// Kept as a plain DTO rather than a MediatR command so the API layer
/// can own its own request contract (versioning, camelCase names, swagger
/// descriptions) without coupling it to the application layer's command shape.
/// The controller maps this to a CreateInvoiceCommand before dispatching.
/// </summary>
public sealed class CreateInvoiceRequest
{
    /// <summary>
    /// Full legal name of the customer as it should appear on the invoice.
    /// Required. Max 200 characters.
    /// </summary>
    public string CustomerName { get; init; } = string.Empty;

    /// <summary>
    /// Customer email address. The issued invoice will be delivered here.
    /// Required. Must be a valid email.
    /// </summary>
    public string CustomerEmail { get; init; } = string.Empty;

    /// <summary>
    /// Optional billing address for the customer.
    /// Required in jurisdictions that mandate a customer address on invoices (e.g. EU VAT).
    /// </summary>
    public CreateInvoiceBillingAddressRequest? BillingAddress { get; init; }

    /// <summary>
    /// ISO 4217 currency code (e.g. "USD", "EUR", "GBP").
    /// All line items must be in this currency.
    /// Defaults to the tenant's configured default currency if omitted.
    /// </summary>
    public string? Currency { get; init; }

    /// <summary>
    /// Tax rate percentage to apply to the discounted subtotal (0–100).
    /// e.g. 20.0 = 20% VAT / GST. Defaults to 0.
    /// </summary>
    public decimal TaxRatePercentage { get; init; } = 0m;

    /// <summary>
    /// Invoice-level discount percentage applied after all line discounts (0–100).
    /// e.g. 5.0 = 5% off the whole invoice. Defaults to 0.
    /// </summary>
    public decimal DiscountPercentage { get; init; } = 0m;

    /// <summary>
    /// Line items on this invoice. At least one line is required.
    /// The invoice will be saved as a Draft; use the Issue endpoint to send it.
    /// </summary>
    public List<CreateInvoiceLineItemRequest> LineItems { get; init; } = [];

    /// <summary>
    /// Optional free-text notes printed on the invoice
    /// (e.g. "Payment due within 30 days. Bank transfer preferred.").
    /// Max 2000 characters.
    /// </summary>
    public string? Notes { get; init; }
}

public sealed class CreateInvoiceLineItemRequest
{
    /// <summary>
    /// Description of the product or service. Required. Max 500 characters.
    /// </summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>
    /// Price per unit (non-negative). Required.
    /// </summary>
    public decimal UnitPrice { get; init; }

    /// <summary>
    /// Number of units (must be > 0). Fractional quantities are supported
    /// for hourly/usage-based billing (e.g. 1.5 hours).
    /// </summary>
    public decimal Quantity { get; init; } = 1m;

    /// <summary>
    /// Per-line discount percentage (0–100). Defaults to 0.
    /// Applied before the invoice-level discount.
    /// </summary>
    public decimal DiscountPercentage { get; init; } = 0m;

    /// <summary>
    /// Optional SKU, product ID, or external reference for this line.
    /// Useful for reconciliation with external systems.
    /// </summary>
    public string? ProductReference { get; init; }
}

public sealed class CreateInvoiceBillingAddressRequest
{
    public string Line1 { get; init; } = string.Empty;
    public string? Line2 { get; init; }
    public string City { get; init; } = string.Empty;
    public string? State { get; init; }

    /// <summary>ISO 3166-1 alpha-2 country code (e.g. "US", "GB").</summary>
    public string CountryCode { get; init; } = string.Empty;
    public string PostalCode { get; init; } = string.Empty;
}
