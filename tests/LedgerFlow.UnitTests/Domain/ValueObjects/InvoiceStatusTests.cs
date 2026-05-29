using FluentAssertions;
using ledgerflowApi.Domain.ValueObjects;
using Xunit;

namespace LedgerFlow.UnitTests.Domain.ValueObjects;

/// <summary>
/// Tests for the InvoiceStatus value object and its transition rules.
/// Getting transitions wrong in a financial system has legal consequences
/// (e.g. voiding a paid invoice), so every path is explicitly tested.
/// </summary>
public class InvoiceStatusTests
{
    // ── Equality ──────────────────────────────────────────────────────────────

    [Fact]
    public void Equals_SameStatus_ReturnsTrue()
    {
        (InvoiceStatus.Draft == InvoiceStatus.Draft).Should().BeTrue();
    }

    [Fact]
    public void Equals_DifferentStatus_ReturnsFalse()
    {
        (InvoiceStatus.Draft == InvoiceStatus.Issued).Should().BeFalse();
    }

    // ── From factory ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Draft")]
    [InlineData("Issued")]
    [InlineData("PartiallyPaid")]
    [InlineData("Paid")]
    [InlineData("Overdue")]
    [InlineData("Voided")]
    public void From_ValidValue_ReturnsCorrectStatus(string value)
    {
        var status = InvoiceStatus.From(value);
        status.Value.Should().Be(value);
    }

    [Theory]
    [InlineData("draft")]   // case-insensitive
    [InlineData("ISSUED")]
    public void From_CaseInsensitive_ReturnsCorrectStatus(string value)
    {
        var act = () => InvoiceStatus.From(value);
        act.Should().NotThrow();
    }

    [Fact]
    public void From_UnknownValue_ThrowsArgumentException()
    {
        var act = () => InvoiceStatus.From("Pending");
        act.Should().Throw<ArgumentException>().WithMessage("*not a valid InvoiceStatus*");
    }

    // ── Helper properties ─────────────────────────────────────────────────────

    [Fact]
    public void IsDraft_OnDraftStatus_ReturnsTrue()
        => InvoiceStatus.Draft.IsDraft.Should().BeTrue();

    [Fact]
    public void IsDraft_OnNonDraftStatus_ReturnsFalse()
        => InvoiceStatus.Issued.IsDraft.Should().BeFalse();

    [Fact]
    public void IsTerminal_PaidAndVoided_ReturnTrue()
    {
        InvoiceStatus.Paid.IsTerminal.Should().BeTrue();
        InvoiceStatus.Voided.IsTerminal.Should().BeTrue();
    }

    [Fact]
    public void IsTerminal_NonTerminalStatuses_ReturnFalse()
    {
        InvoiceStatus.Draft.IsTerminal.Should().BeFalse();
        InvoiceStatus.Issued.IsTerminal.Should().BeFalse();
        InvoiceStatus.PartiallyPaid.IsTerminal.Should().BeFalse();
        InvoiceStatus.Overdue.IsTerminal.Should().BeFalse();
    }

    // ── Transition rules ──────────────────────────────────────────────────────

    [Theory]
    [InlineData("Draft", "Issued", true)]
    [InlineData("Draft", "Voided", true)]
    [InlineData("Draft", "Paid", false)]          // can't skip directly to paid
    [InlineData("Draft", "PartiallyPaid", false)]
    [InlineData("Draft", "Overdue", false)]
    [InlineData("Issued", "PartiallyPaid", true)]
    [InlineData("Issued", "Paid", true)]
    [InlineData("Issued", "Overdue", true)]
    [InlineData("Issued", "Voided", true)]
    [InlineData("Issued", "Draft", false)]         // can't go backwards
    [InlineData("PartiallyPaid", "Paid", true)]
    [InlineData("PartiallyPaid", "Overdue", true)]
    [InlineData("PartiallyPaid", "Voided", true)]
    [InlineData("PartiallyPaid", "Draft", false)]
    [InlineData("Paid", "Draft", false)]           // Paid is terminal
    [InlineData("Paid", "Voided", false)]          // can't void a paid invoice
    [InlineData("Paid", "Issued", false)]
    [InlineData("Overdue", "PartiallyPaid", true)]
    [InlineData("Overdue", "Paid", true)]
    [InlineData("Overdue", "Voided", true)]
    [InlineData("Overdue", "Draft", false)]
    [InlineData("Voided", "Draft", false)]         // Voided is terminal
    [InlineData("Voided", "Issued", false)]
    [InlineData("Voided", "Paid", false)]
    public void CanTransitionTo_AllTransitions_MatchExpectedRules(
        string from, string to, bool expectedAllowed)
    {
        var fromStatus = InvoiceStatus.From(from);
        var toStatus = InvoiceStatus.From(to);

        fromStatus.CanTransitionTo(toStatus).Should().Be(expectedAllowed,
            because: $"{from} → {to} should {(expectedAllowed ? "" : "not ")}be allowed");
    }

    // ── All statuses ──────────────────────────────────────────────────────────

    [Fact]
    public void All_ContainsSixStatuses()
    {
        InvoiceStatus.All.Should().HaveCount(6);
    }
}
