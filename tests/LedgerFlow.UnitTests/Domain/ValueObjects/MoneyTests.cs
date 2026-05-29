using FluentAssertions;
using ledgerflowApi.Domain.ValueObjects;
using Xunit;

namespace LedgerFlow.UnitTests.Domain.ValueObjects;

/// <summary>
/// Unit tests for the Money value object.
/// Money is the foundation of every financial calculation in the system,
/// so we test it thoroughly — including edge cases around rounding and
/// cross-currency guards.
/// </summary>
public class MoneyTests
{
    // ── Construction ──────────────────────────────────────────────────────────

    [Fact]
    public void Constructor_WithValidAmountAndCurrency_CreatesInstance()
    {
        // Arrange & Act
        var money = new Money(100.50m, "USD");

        // Assert
        money.Amount.Should().Be(100.50m);
        money.Currency.Should().Be("USD");
    }

    [Theory]
    [InlineData("usd", "USD")]
    [InlineData("eur", "EUR")]
    [InlineData("GBP", "GBP")]
    public void Constructor_NormalisesCurrency_ToUpperCase(string input, string expected)
    {
        var money = new Money(50m, input);
        money.Currency.Should().Be(expected);
    }

    [Fact]
    public void Constructor_RoundsAmount_ToTwoDecimalPlaces()
    {
        // 100.555 rounds up (AwayFromZero midpoint rounding)
        var money = new Money(100.555m, "USD");
        money.Amount.Should().Be(100.56m);
    }

    [Fact]
    public void Constructor_WithZeroAmount_IsAllowed()
    {
        // Zero-value lines (fully discounted) are legitimate
        var money = new Money(0m, "USD");
        money.Amount.Should().Be(0m);
    }

    [Fact]
    public void Constructor_WithNegativeAmount_ThrowsArgumentException()
    {
        // Negative money = credit note, not an invoice line
        var act = () => new Money(-1m, "USD");
        act.Should().Throw<ArgumentException>().WithMessage("*negative*");
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData(null)]
    public void Constructor_WithEmptyCurrency_ThrowsArgumentException(string? currency)
    {
        var act = () => new Money(10m, currency!);
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("US")]
    [InlineData("USDD")]
    public void Constructor_WithInvalidCurrencyLength_ThrowsArgumentException(string currency)
    {
        var act = () => new Money(10m, currency);
        act.Should().Throw<ArgumentException>().WithMessage("*3-character*");
    }

    // ── Factory method ─────────────────────────────────────────────────────────

    [Fact]
    public void Of_CreatesEquivalentInstance()
    {
        var a = Money.Of(75m, "EUR");
        var b = new Money(75m, "EUR");
        a.Should().Be(b);
    }

    // ── Add ───────────────────────────────────────────────────────────────────

    [Fact]
    public void Add_SameCurrency_ReturnsSummedAmount()
    {
        var a = new Money(100m, "USD");
        var b = new Money(50m, "USD");

        var result = a.Add(b);

        result.Amount.Should().Be(150m);
        result.Currency.Should().Be("USD");
    }

    [Fact]
    public void Add_DifferentCurrencies_ThrowsInvalidOperationException()
    {
        var usd = new Money(100m, "USD");
        var eur = new Money(100m, "EUR");

        var act = () => usd.Add(eur);
        act.Should().Throw<InvalidOperationException>().WithMessage("*different currencies*");
    }

    [Fact]
    public void Add_IsImmutable_OriginalUnchanged()
    {
        var original = new Money(100m, "USD");
        var _ = original.Add(new Money(50m, "USD"));
        original.Amount.Should().Be(100m);
    }

    // ── Subtract ──────────────────────────────────────────────────────────────

    [Fact]
    public void Subtract_SameCurrency_ReturnsDifference()
    {
        var result = new Money(100m, "USD").Subtract(new Money(30m, "USD"));
        result.Amount.Should().Be(70m);
    }

    [Fact]
    public void Subtract_WouldGoNegative_ThrowsInvalidOperationException()
    {
        var act = () => new Money(30m, "USD").Subtract(new Money(50m, "USD"));
        act.Should().Throw<InvalidOperationException>().WithMessage("*negative*");
    }

    [Fact]
    public void Subtract_DifferentCurrencies_ThrowsInvalidOperationException()
    {
        var act = () => new Money(100m, "USD").Subtract(new Money(10m, "EUR"));
        act.Should().Throw<InvalidOperationException>();
    }

    // ── Multiply ──────────────────────────────────────────────────────────────

    [Fact]
    public void Multiply_ByPositiveFactor_ReturnsScaledAmount()
    {
        var result = new Money(50m, "USD").Multiply(3m);
        result.Amount.Should().Be(150m);
    }

    [Fact]
    public void Multiply_ByZero_ReturnsZeroAmount()
    {
        var result = new Money(50m, "USD").Multiply(0m);
        result.Amount.Should().Be(0m);
    }

    [Fact]
    public void Multiply_ByNegativeFactor_ThrowsArgumentException()
    {
        var act = () => new Money(50m, "USD").Multiply(-1m);
        act.Should().Throw<ArgumentException>().WithMessage("*negative*");
    }

    // ── ApplyDiscount ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData(100, 10, 90)]    // standard 10% discount
    [InlineData(200, 25, 150)]   // 25% discount
    [InlineData(100, 0, 100)]    // no discount
    [InlineData(100, 100, 0)]    // fully discounted
    public void ApplyDiscount_WithValidPercentage_ReturnsDiscountedAmount(
        decimal original, decimal discountPct, decimal expected)
    {
        var result = new Money(original, "USD").ApplyDiscount(discountPct);
        result.Amount.Should().Be(expected);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void ApplyDiscount_OutOfRange_ThrowsArgumentException(decimal discountPct)
    {
        var act = () => new Money(100m, "USD").ApplyDiscount(discountPct);
        act.Should().Throw<ArgumentException>();
    }

    // ── Comparison helpers ────────────────────────────────────────────────────

    [Fact]
    public void IsZero_WhenAmountIsZero_ReturnsTrue()
    {
        new Money(0m, "USD").IsZero.Should().BeTrue();
    }

    [Fact]
    public void IsZero_WhenAmountIsPositive_ReturnsFalse()
    {
        new Money(0.01m, "USD").IsZero.Should().BeFalse();
    }

    [Fact]
    public void IsGreaterThan_WhenLarger_ReturnsTrue()
    {
        new Money(100m, "USD").IsGreaterThan(new Money(50m, "USD")).Should().BeTrue();
    }

    [Fact]
    public void IsGreaterThan_WhenEqual_ReturnsFalse()
    {
        new Money(50m, "USD").IsGreaterThan(new Money(50m, "USD")).Should().BeFalse();
    }

    // ── Equality ──────────────────────────────────────────────────────────────

    [Fact]
    public void Equals_SameAmountAndCurrency_ReturnsTrue()
    {
        var a = new Money(100m, "USD");
        var b = new Money(100m, "USD");
        a.Should().Be(b);
        (a == b).Should().BeTrue();
    }

    [Fact]
    public void Equals_DifferentAmount_ReturnsFalse()
    {
        var a = new Money(100m, "USD");
        var b = new Money(200m, "USD");
        (a == b).Should().BeFalse();
    }

    [Fact]
    public void Equals_DifferentCurrency_ReturnsFalse()
    {
        var a = new Money(100m, "USD");
        var b = new Money(100m, "EUR");
        (a == b).Should().BeFalse();
    }

    [Fact]
    public void GetHashCode_EqualInstances_HaveSameHashCode()
    {
        var a = new Money(100m, "USD");
        var b = new Money(100m, "USD");
        a.GetHashCode().Should().Be(b.GetHashCode());
    }

    // ── ToString ──────────────────────────────────────────────────────────────

    [Fact]
    public void ToString_FormatsAmountAndCurrency()
    {
        new Money(99.99m, "USD").ToString().Should().Be("99.99 USD");
    }
}
