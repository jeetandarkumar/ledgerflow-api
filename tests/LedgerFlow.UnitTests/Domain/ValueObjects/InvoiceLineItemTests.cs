using FluentAssertions;
using ledgerflowApi.Domain.ValueObjects;
using Xunit;

namespace LedgerFlow.UnitTests.Domain.ValueObjects;

/// <summary>
/// Unit tests for InvoiceLineItem, focused on the financial calculations —
/// GrossAmount, DiscountAmount, and NetAmount — and specifically on the
/// rounding behaviour fixed as part of the financial-correctness audit.
///
/// The key invariant under test: GrossAmount == DiscountAmount + NetAmount,
/// exactly, for every input. Before the fix, NetAmount was derived by
/// applying the discount to an already-rounded GrossAmount, and
/// DiscountAmount was computed independently — two separate roundings of
/// related quantities that could disagree by a cent.
/// </summary>
public class InvoiceLineItemTests
{
    private static InvoiceLineItem MakeLineItem(
        decimal unitPrice, decimal qty, decimal discount = 0m, string currency = "USD")
        => new(
            description: "Consulting Services",
            unitPrice: Money.Of(unitPrice, currency),
            quantity: qty,
            discountPercentage: discount,
            productReference: null);

    // ── Construction / validation ────────────────────────────────────────────

    [Fact]
    public void Constructor_WithZeroQuantity_ThrowsArgumentException()
    {
        var act = () => MakeLineItem(unitPrice: 100m, qty: 0m);
        act.Should().Throw<ArgumentException>().WithMessage("*Quantity*");
    }

    [Fact]
    public void Constructor_WithNegativeQuantity_ThrowsArgumentException()
    {
        var act = () => MakeLineItem(unitPrice: 100m, qty: -1m);
        act.Should().Throw<ArgumentException>().WithMessage("*Quantity*");
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void Constructor_WithOutOfRangeDiscount_ThrowsArgumentException(decimal discount)
    {
        var act = () => MakeLineItem(unitPrice: 100m, qty: 1m, discount: discount);
        act.Should().Throw<ArgumentException>().WithMessage("*Discount*");
    }

    // ── GrossAmount ───────────────────────────────────────────────────────────

    [Fact]
    public void GrossAmount_IsUnitPriceTimesQuantity()
    {
        var line = MakeLineItem(unitPrice: 49.99m, qty: 3m);
        line.GrossAmount.Amount.Should().Be(149.97m);
    }

    // ── NetAmount / DiscountAmount — no discount ─────────────────────────────

    [Fact]
    public void NetAmount_NoDiscount_EqualsGrossAmount()
    {
        var line = MakeLineItem(unitPrice: 33.33m, qty: 3m); // gross = 99.99
        line.NetAmount.Should().Be(line.GrossAmount);
        line.DiscountAmount.IsZero.Should().BeTrue();
    }

    // ── NetAmount / DiscountAmount — whole-number cases (sanity) ─────────────

    [Theory]
    [InlineData(100, 1, 10, 90, 10)]     // 100 x1, 10% off -> net 90, discount 10
    [InlineData(200, 1, 25, 150, 50)]    // 200 x1, 25% off -> net 150, discount 50
    [InlineData(100, 1, 100, 0, 100)]    // fully discounted line
    public void NetAmountAndDiscountAmount_WholeNumberCases_AreExact(
        decimal unitPrice, decimal qty, decimal discountPct, decimal expectedNet, decimal expectedDiscount)
    {
        var line = MakeLineItem(unitPrice, qty, discountPct);

        line.NetAmount.Amount.Should().Be(expectedNet);
        line.DiscountAmount.Amount.Should().Be(expectedDiscount);
    }

    // ── Reconciliation invariant: Gross == Discount + Net, exactly ──────────

    [Theory]
    [InlineData(19.99, 3, 15)]     // 3 x $19.99 with 15% off — the kind of case that used to drift a cent
    [InlineData(9.995, 7, 33.33)]  // odd unit price, fractional-feeling discount
    [InlineData(0.03, 1000, 7)]    // tiny unit price, large quantity
    [InlineData(1234.56, 2.5, 12.5)] // fractional quantity (e.g. hours billed)
    [InlineData(1.005, 1, 0.001)]  // right on a rounding boundary
    public void GrossAmount_AlwaysEquals_DiscountAmountPlusNetAmount(
        decimal unitPrice, decimal qty, decimal discountPct)
    {
        var line = MakeLineItem(unitPrice, qty, discountPct);

        line.DiscountAmount.Add(line.NetAmount).Should().Be(line.GrossAmount);
    }

    // ── Regression: discount applied to the rounded Gross used to drift ─────

    [Fact]
    public void NetAmount_ComputedFromFullPrecision_NotFromRoundedGross()
    {
        // UnitPrice 1.00, Qty 1.005 (e.g. a fractional usage-based quantity), 50% discount.
        //
        // Full-precision gross = 1.00 * 1.005 = 1.005, which rounds (away from zero, at the
        // halfway point) to GrossAmount = 1.01.
        //
        // The OLD (buggy) behaviour applied the discount to that already-rounded gross:
        //   1.01 * 0.50 = 0.505 -> rounds away from zero -> 0.51
        //
        // The NEW (correct) behaviour computes NetAmount directly from full precision:
        //   1.00 * 1.005 * 0.50 = 0.5025 -> rounds to 0.50
        //
        // The two approaches disagree by a cent. This test locks in the full-precision path
        // and confirms Gross still reconciles exactly with Discount + Net either way.
        var line = MakeLineItem(unitPrice: 1.00m, qty: 1.005m, discount: 50m);

        line.GrossAmount.Amount.Should().Be(1.01m);
        line.NetAmount.Amount.Should().Be(0.50m);
        line.DiscountAmount.Amount.Should().Be(0.51m);

        line.DiscountAmount.Add(line.NetAmount).Should().Be(line.GrossAmount);
    }

    // ── Fractional quantity ───────────────────────────────────────────────────

    [Fact]
    public void NetAmount_WithFractionalQuantity_CalculatesCorrectly()
    {
        // 1.5 hours at $80/hr, no discount
        var line = MakeLineItem(unitPrice: 80m, qty: 1.5m);
        line.GrossAmount.Amount.Should().Be(120m);
        line.NetAmount.Amount.Should().Be(120m);
    }
}
