namespace ledgerflowApi.Domain.ValueObjects;

/// <summary>
/// Represents a monetary amount with its currency.
///
/// Design decisions:
/// - Amount is always stored as decimal to avoid floating-point rounding bugs,
///   which are catastrophic in financial systems (e.g. 0.1 + 0.2 != 0.3 in float).
/// - Currency is an ISO 4217 code (USD, EUR, GBP, etc.).
/// - Money is immutable — arithmetic returns new instances, never mutates in place.
/// - Cross-currency arithmetic throws rather than silently converting, because
///   exchange rates are a business concern, not a domain concern.
/// </summary>
public sealed class Money : IEquatable<Money>
{
    public decimal Amount { get; }

    /// <summary>ISO 4217 currency code, always stored uppercase (e.g. "USD").</summary>
    public string Currency { get; }

    public static readonly Money Zero = new(0m, "USD");

    private Money() { Amount = 0; Currency = string.Empty; } // EF Core

    public Money(decimal amount, string currency)
    {
        if (string.IsNullOrWhiteSpace(currency))
            throw new ArgumentException("Currency code is required.", nameof(currency));

        if (currency.Length != 3)
            throw new ArgumentException("Currency must be a 3-character ISO 4217 code.", nameof(currency));

        // Amounts CAN be zero (e.g. a fully discounted invoice line) but never negative.
        // Negative money is represented as a credit note, not a negative invoice.
        if (amount < 0)
            throw new ArgumentException("Monetary amount cannot be negative.", nameof(amount));

        // Round to 2 decimal places (standard for most currencies).
        // For currencies like JPY (0 decimals) or KWD (3 decimals), this would be
        // parameterised, but 2dp covers the majority of SaaS billing scenarios.
        Amount = Math.Round(amount, 2, MidpointRounding.AwayFromZero);
        Currency = currency.ToUpperInvariant();
    }

    /// <summary>Creates a zero-value Money in the given currency.</summary>
    public static Money Of(decimal amount, string currency) => new(amount, currency);

    public Money Add(Money other)
    {
        GuardSameCurrency(other);
        return new Money(Amount + other.Amount, Currency);
    }

    public Money Subtract(Money other)
    {
        GuardSameCurrency(other);
        var result = Amount - other.Amount;
        if (result < 0)
            throw new InvalidOperationException(
                $"Subtraction would produce a negative amount ({Amount} - {other.Amount} {Currency}).");
        return new Money(result, Currency);
    }

    public Money Multiply(decimal factor)
    {
        if (factor < 0)
            throw new ArgumentException("Multiplication factor cannot be negative.", nameof(factor));
        return new Money(Amount * factor, Currency);
    }

    /// <summary>
    /// Applies a percentage discount (0–100).
    /// e.g. 100 USD.ApplyDiscount(10) = 90 USD
    /// </summary>
    public Money ApplyDiscount(decimal discountPercentage)
    {
        if (discountPercentage < 0 || discountPercentage > 100)
            throw new ArgumentException("Discount must be between 0 and 100.", nameof(discountPercentage));

        var discountAmount = Amount * (discountPercentage / 100m);
        return new Money(Amount - discountAmount, Currency);
    }

    public bool IsZero => Amount == 0m;
    public bool IsGreaterThan(Money other) { GuardSameCurrency(other); return Amount > other.Amount; }
    public bool IsGreaterThanOrEqual(Money other) { GuardSameCurrency(other); return Amount >= other.Amount; }

    private void GuardSameCurrency(Money other)
    {
        if (Currency != other.Currency)
            throw new InvalidOperationException(
                $"Cannot operate on different currencies: {Currency} and {other.Currency}. " +
                "Use an exchange rate service to convert first.");
    }

    public override string ToString() => $"{Amount:F2} {Currency}";

    // Value object equality — two Money instances are equal if amount AND currency match.
    public bool Equals(Money? other) =>
        other is not null && Amount == other.Amount && Currency == other.Currency;

    public override bool Equals(object? obj) => Equals(obj as Money);
    public override int GetHashCode() => HashCode.Combine(Amount, Currency);

    public static bool operator ==(Money? left, Money? right) =>
        left?.Equals(right) ?? right is null;
    public static bool operator !=(Money? left, Money? right) => !(left == right);
}
