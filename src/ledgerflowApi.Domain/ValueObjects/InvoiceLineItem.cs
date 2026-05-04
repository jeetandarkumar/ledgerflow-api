namespace ledgerflowApi.Domain.ValueObjects;

/// <summary>
/// A single line on an invoice: one unit of a product or service.
///
/// Design rules:
/// - LineItems are value objects within the Invoice aggregate — they have no
///   independent identity and cannot be shared across invoices.
/// - The total for a line is always (UnitPrice × Quantity) − discount,
///   computed deterministically from its own fields (no side effects).
/// - Quantity must be positive — zero-quantity lines are meaningless and
///   could indicate a data entry error.
/// - Description is intentionally free-form to support any billing model
///   (time-based, flat-fee, usage-based).
/// </summary>
public sealed class InvoiceLineItem : IEquatable<InvoiceLineItem>
{
    /// <summary>Human-readable description of the product or service billed.</summary>
    public string Description { get; }

    /// <summary>
    /// Price per single unit. Must be in the same currency as the invoice.
    /// </summary>
    public Money UnitPrice { get; }

    /// <summary>
    /// Number of units. Supports fractional quantities (e.g. 1.5 hours).
    /// Must be greater than zero.
    /// </summary>
    public decimal Quantity { get; }

    /// <summary>
    /// Optional discount percentage applied to this line (0–100).
    /// e.g. 10.0 = 10% off this line item.
    /// Line-level discounts stack on top of any invoice-level discount.
    /// </summary>
    public decimal DiscountPercentage { get; }

    /// <summary>Optional external reference (SKU, product ID, etc.).</summary>
    public string? ProductReference { get; }

    private InvoiceLineItem() // EF Core
    {
        Description = string.Empty;
        UnitPrice = Money.Zero;
    }

    public InvoiceLineItem(
        string description,
        Money unitPrice,
        decimal quantity,
        decimal discountPercentage = 0m,
        string? productReference = null)
    {
        if (string.IsNullOrWhiteSpace(description))
            throw new ArgumentException("Line item description is required.", nameof(description));
        if (description.Length > 500)
            throw new ArgumentException("Line item description cannot exceed 500 characters.", nameof(description));
        if (unitPrice is null)
            throw new ArgumentNullException(nameof(unitPrice));
        if (quantity <= 0)
            throw new ArgumentException("Quantity must be greater than zero.", nameof(quantity));
        if (discountPercentage < 0 || discountPercentage > 100)
            throw new ArgumentException("Discount must be between 0 and 100.", nameof(discountPercentage));

        Description = description.Trim();
        UnitPrice = unitPrice;
        Quantity = Math.Round(quantity, 4, MidpointRounding.AwayFromZero); // 4dp for hourly/usage billing
        DiscountPercentage = discountPercentage;
        ProductReference = productReference?.Trim();
    }

    /// <summary>
    /// Gross subtotal before discount: UnitPrice × Quantity.
    /// </summary>
    public Money GrossAmount => UnitPrice.Multiply(Quantity);

    /// <summary>
    /// The monetary value of the discount applied to this line.
    /// </summary>
    public Money DiscountAmount => GrossAmount.Multiply(DiscountPercentage / 100m);

    /// <summary>
    /// Net amount after the line-level discount: what the customer actually pays for this line.
    /// </summary>
    public Money NetAmount => GrossAmount.ApplyDiscount(DiscountPercentage);

    public override string ToString() =>
        $"{Description} × {Quantity} @ {UnitPrice}" +
        (DiscountPercentage > 0 ? $" (−{DiscountPercentage}%)" : "") +
        $" = {NetAmount}";

    public bool Equals(InvoiceLineItem? other) =>
        other is not null
        && Description == other.Description
        && UnitPrice == other.UnitPrice
        && Quantity == other.Quantity
        && DiscountPercentage == other.DiscountPercentage
        && ProductReference == other.ProductReference;

    public override bool Equals(object? obj) => Equals(obj as InvoiceLineItem);

    public override int GetHashCode() =>
        HashCode.Combine(Description, UnitPrice, Quantity, DiscountPercentage, ProductReference);

    public static bool operator ==(InvoiceLineItem? l, InvoiceLineItem? r) => l?.Equals(r) ?? r is null;
    public static bool operator !=(InvoiceLineItem? l, InvoiceLineItem? r) => !(l == r);
}
