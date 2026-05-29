using FluentAssertions;
using ledgerflowApi.Domain.Entities;
using ledgerflowApi.Domain.Exceptions;
using ledgerflowApi.Domain.ValueObjects;
using Xunit;

namespace LedgerFlow.UnitTests.Domain.Entities;

/// <summary>
/// Unit tests for the Invoice aggregate root.
/// Covers the full lifecycle: creation, line item management, all status
/// transitions, payment recording, and financial calculations.
/// </summary>
public class InvoiceTests
{
    // ── Test data helpers ─────────────────────────────────────────────────────

    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    private static Invoice CreateDraftInvoice(
        string currency = "USD",
        decimal taxRate = 0m,
        decimal discount = 0m)
        => Invoice.Create(
            tenantId: TenantId,
            createdByUserId: UserId,
            invoiceNumber: "INV-2024-000001",
            customerName: "Acme Corp",
            customerEmail: "billing@acme.com",
            currency: currency,
            taxRatePercentage: taxRate,
            discountPercentage: discount);

    private static InvoiceLineItem MakeLineItem(decimal unitPrice = 100m, decimal qty = 1m,
        string currency = "USD", decimal discount = 0m)
        => new(
            description: "Consulting Services",
            unitPrice: Money.Of(unitPrice, currency),
            quantity: qty,
            discountPercentage: discount,
            productReference: null);

    // ── Create ────────────────────────────────────────────────────────────────

    [Fact]
    public void Create_WithValidInputs_ReturnsDraftInvoice()
    {
        var invoice = CreateDraftInvoice();

        invoice.Status.Should().Be(InvoiceStatus.Draft);
        invoice.InvoiceNumber.Should().Be("INV-2024-000001");
        invoice.CustomerName.Should().Be("Acme Corp");
        invoice.CustomerEmail.Should().Be("billing@acme.com");
        invoice.Currency.Should().Be("USD");
        invoice.LineItems.Should().BeEmpty();
        invoice.PaidAmount.Amount.Should().Be(0m);
    }

    [Fact]
    public void Create_NormalisesEmailToLowerCase()
    {
        var invoice = Invoice.Create(TenantId, UserId, "INV-001", "Bob", "UPPER@CASE.COM", "USD");
        invoice.CustomerEmail.Should().Be("upper@case.com");
    }

    [Fact]
    public void Create_NormalisesCurrencyToUpperCase()
    {
        var invoice = Invoice.Create(TenantId, UserId, "INV-001", "Bob", "bob@test.com", "usd");
        invoice.Currency.Should().Be("USD");
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Create_WithEmptyInvoiceNumber_ThrowsDomainException(string number)
    {
        var act = () => Invoice.Create(TenantId, UserId, number, "Bob", "bob@test.com", "USD");
        act.Should().Throw<DomainException>().WithMessage("*Invoice number*");
    }

    [Fact]
    public void Create_WithInvoiceNumberOver50Chars_ThrowsDomainException()
    {
        var longNumber = new string('X', 51);
        var act = () => Invoice.Create(TenantId, UserId, longNumber, "Bob", "bob@test.com", "USD");
        act.Should().Throw<DomainException>().WithMessage("*50 characters*");
    }

    [Fact]
    public void Create_WithInvalidEmail_ThrowsDomainException()
    {
        var act = () => Invoice.Create(TenantId, UserId, "INV-001", "Bob", "not-an-email", "USD");
        act.Should().Throw<DomainException>().WithMessage("*valid email*");
    }

    [Theory]
    [InlineData("US")]
    [InlineData("USDD")]
    [InlineData("")]
    public void Create_WithInvalidCurrency_ThrowsDomainException(string currency)
    {
        var act = () => Invoice.Create(TenantId, UserId, "INV-001", "Bob", "bob@test.com", currency);
        act.Should().Throw<DomainException>().WithMessage("*ISO 4217*");
    }

    [Fact]
    public void Create_WithTaxRateAbove100_ThrowsDomainException()
    {
        var act = () => Invoice.Create(TenantId, UserId, "INV-001", "Bob", "b@t.com", "USD", taxRatePercentage: 101m);
        act.Should().Throw<DomainException>().WithMessage("*Tax rate*");
    }

    // ── AddLineItem ───────────────────────────────────────────────────────────

    [Fact]
    public void AddLineItem_OnDraftInvoice_AddsItemSuccessfully()
    {
        var invoice = CreateDraftInvoice();
        var item = MakeLineItem();

        invoice.AddLineItem(item);

        invoice.LineItems.Should().HaveCount(1);
        invoice.LineItems.Should().Contain(item);
    }

    [Fact]
    public void AddLineItem_OnIssuedInvoice_ThrowsDomainException()
    {
        var invoice = CreateDraftInvoice();
        invoice.AddLineItem(MakeLineItem());
        invoice.Issue(DateTime.UtcNow.AddDays(30));

        var act = () => invoice.AddLineItem(MakeLineItem());
        act.Should().Throw<DomainException>().WithMessage("*no longer a draft*");
    }

    [Fact]
    public void AddLineItem_WithDifferentCurrency_ThrowsDomainException()
    {
        var invoice = CreateDraftInvoice(currency: "USD");
        var eurItem = MakeLineItem(currency: "EUR");

        var act = () => invoice.AddLineItem(eurItem);
        act.Should().Throw<DomainException>().WithMessage("*currency*");
    }

    [Fact]
    public void AddLineItem_NullItem_ThrowsArgumentNullException()
    {
        var invoice = CreateDraftInvoice();
        var act = () => invoice.AddLineItem(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    // ── RemoveLineItem ────────────────────────────────────────────────────────

    [Fact]
    public void RemoveLineItem_ExistingItem_RemovesSuccessfully()
    {
        var invoice = CreateDraftInvoice();
        var item = MakeLineItem();
        invoice.AddLineItem(item);

        invoice.RemoveLineItem(item);

        invoice.LineItems.Should().BeEmpty();
    }

    [Fact]
    public void RemoveLineItem_NonExistingItem_ThrowsDomainException()
    {
        var invoice = CreateDraftInvoice();
        var act = () => invoice.RemoveLineItem(MakeLineItem());
        act.Should().Throw<DomainException>().WithMessage("*not found*");
    }

    [Fact]
    public void RemoveLineItem_OnIssuedInvoice_ThrowsDomainException()
    {
        var invoice = CreateDraftInvoice();
        var item = MakeLineItem();
        invoice.AddLineItem(item);
        invoice.Issue(DateTime.UtcNow.AddDays(30));

        var act = () => invoice.RemoveLineItem(item);
        act.Should().Throw<DomainException>();
    }

    // ── Issue ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Issue_WithLineItemsAndFutureDueDate_TransitionsToIssued()
    {
        var invoice = CreateDraftInvoice();
        invoice.AddLineItem(MakeLineItem());
        var dueDate = DateTime.UtcNow.AddDays(30);

        invoice.Issue(dueDate);

        invoice.Status.Should().Be(InvoiceStatus.Issued);
        invoice.IssuedAt.Should().NotBeNull();
        invoice.DueDate.Should().Be(dueDate);
    }

    [Fact]
    public void Issue_WithNoLineItems_ThrowsDomainException()
    {
        var invoice = CreateDraftInvoice();
        var act = () => invoice.Issue(DateTime.UtcNow.AddDays(30));
        act.Should().Throw<DomainException>().WithMessage("*no line items*");
    }

    [Fact]
    public void Issue_WithPastDueDate_ThrowsDomainException()
    {
        var invoice = CreateDraftInvoice();
        invoice.AddLineItem(MakeLineItem());

        var act = () => invoice.Issue(DateTime.UtcNow.AddDays(-1));
        act.Should().Throw<DomainException>().WithMessage("*future*");
    }

    [Fact]
    public void Issue_AlreadyIssuedInvoice_ThrowsInvalidStatusTransitionException()
    {
        var invoice = CreateDraftInvoice();
        invoice.AddLineItem(MakeLineItem());
        invoice.Issue(DateTime.UtcNow.AddDays(30));

        var act = () => invoice.Issue(DateTime.UtcNow.AddDays(60));
        act.Should().Throw<InvalidStatusTransitionException>();
    }

    [Fact]
    public void Issue_RaisesDomainEvent()
    {
        var invoice = CreateDraftInvoice();
        invoice.AddLineItem(MakeLineItem());

        invoice.Issue(DateTime.UtcNow.AddDays(30));

        invoice.DomainEvents.Should().HaveCount(1);
        invoice.DomainEvents.First().Should().BeOfType<ledgerflowApi.Domain.Events.InvoiceIssuedEvent>();
    }

    // ── MarkAsOverdue ─────────────────────────────────────────────────────────

    [Fact]
    public void MarkAsOverdue_FromIssuedWithPastDueDate_TransitionsToOverdue()
    {
        var invoice = CreateDraftInvoice();
        invoice.AddLineItem(MakeLineItem());
        // Use reflection to set the DueDate to the past — the only way without using the Issue
        // method (which enforces future dates). This simulates a nightly job running after due date.
        invoice.Issue(DateTime.UtcNow.AddSeconds(1));
        // Force the due date backwards via reflection to simulate it being in the past
        typeof(Invoice).GetProperty("DueDate")!
            .SetValue(invoice, DateTime.UtcNow.AddDays(-1));

        invoice.MarkAsOverdue();

        invoice.Status.Should().Be(InvoiceStatus.Overdue);
    }

    [Fact]
    public void MarkAsOverdue_WhenDueDateNotPassed_ThrowsDomainException()
    {
        var invoice = CreateDraftInvoice();
        invoice.AddLineItem(MakeLineItem());
        invoice.Issue(DateTime.UtcNow.AddDays(30));

        var act = () => invoice.MarkAsOverdue();
        act.Should().Throw<DomainException>().WithMessage("*due date has not passed*");
    }

    // ── Void ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Void_DraftInvoice_WithReason_TransitionsToVoided()
    {
        var invoice = CreateDraftInvoice();

        invoice.Void("Entered in error");

        invoice.Status.Should().Be(InvoiceStatus.Voided);
        invoice.Notes.Should().Contain("VOIDED");
        invoice.Notes.Should().Contain("Entered in error");
    }

    [Fact]
    public void Void_IssuedInvoice_TransitionsToVoided()
    {
        var invoice = CreateDraftInvoice();
        invoice.AddLineItem(MakeLineItem());
        invoice.Issue(DateTime.UtcNow.AddDays(30));

        invoice.Void("Customer cancelled");

        invoice.Status.Should().Be(InvoiceStatus.Voided);
    }

    [Fact]
    public void Void_PaidInvoice_ThrowsDomainException()
    {
        var invoice = CreateDraftInvoice();
        invoice.AddLineItem(MakeLineItem(unitPrice: 100m));
        invoice.Issue(DateTime.UtcNow.AddDays(30));
        invoice.RecordPayment(Money.Of(100m, "USD"), TenantId);

        var act = () => invoice.Void("Want to void");
        act.Should().Throw<DomainException>().WithMessage("*fully paid*");
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Void_WithEmptyReason_ThrowsDomainException(string reason)
    {
        var invoice = CreateDraftInvoice();
        var act = () => invoice.Void(reason);
        act.Should().Throw<DomainException>().WithMessage("*reason*");
    }

    [Fact]
    public void Void_PrependsToExistingNotes()
    {
        var invoice = Invoice.Create(TenantId, UserId, "INV-001", "Bob", "b@t.com", "USD",
            notes: "Original note");

        invoice.Void("Error");

        invoice.Notes.Should().Contain("Original note");
        invoice.Notes.Should().Contain("VOIDED: Error");
    }

    // ── RecordPayment ─────────────────────────────────────────────────────────

    [Fact]
    public void RecordPayment_PartialPayment_TransitionsToPartiallyPaid()
    {
        var invoice = CreateDraftInvoice();
        invoice.AddLineItem(MakeLineItem(unitPrice: 100m));
        invoice.Issue(DateTime.UtcNow.AddDays(30));

        invoice.RecordPayment(Money.Of(50m, "USD"), TenantId);

        invoice.Status.Should().Be(InvoiceStatus.PartiallyPaid);
        invoice.PaidAmount.Amount.Should().Be(50m);
        invoice.OutstandingAmount.Amount.Should().Be(50m);
    }

    [Fact]
    public void RecordPayment_FullPayment_TransitionsToPaid()
    {
        var invoice = CreateDraftInvoice();
        invoice.AddLineItem(MakeLineItem(unitPrice: 100m));
        invoice.Issue(DateTime.UtcNow.AddDays(30));

        invoice.RecordPayment(Money.Of(100m, "USD"), TenantId);

        invoice.Status.Should().Be(InvoiceStatus.Paid);
        invoice.PaidAt.Should().NotBeNull();
        invoice.OutstandingAmount.IsZero.Should().BeTrue();
    }

    [Fact]
    public void RecordPayment_TwoPartialsAddingToFull_TransitionsToPaid()
    {
        var invoice = CreateDraftInvoice();
        invoice.AddLineItem(MakeLineItem(unitPrice: 100m));
        invoice.Issue(DateTime.UtcNow.AddDays(30));

        invoice.RecordPayment(Money.Of(60m, "USD"), TenantId);
        invoice.RecordPayment(Money.Of(40m, "USD"), TenantId);

        invoice.Status.Should().Be(InvoiceStatus.Paid);
    }

    [Fact]
    public void RecordPayment_ExceedingOutstandingAmount_ThrowsDomainException()
    {
        var invoice = CreateDraftInvoice();
        invoice.AddLineItem(MakeLineItem(unitPrice: 100m));
        invoice.Issue(DateTime.UtcNow.AddDays(30));

        var act = () => invoice.RecordPayment(Money.Of(150m, "USD"), TenantId);
        act.Should().Throw<DomainException>().WithMessage("*exceeds the outstanding balance*");
    }

    [Fact]
    public void RecordPayment_WrongCurrency_ThrowsDomainException()
    {
        var invoice = CreateDraftInvoice(currency: "USD");
        invoice.AddLineItem(MakeLineItem(unitPrice: 100m));
        invoice.Issue(DateTime.UtcNow.AddDays(30));

        var act = () => invoice.RecordPayment(Money.Of(100m, "EUR"), TenantId);
        act.Should().Throw<DomainException>().WithMessage("*currency*");
    }

    [Fact]
    public void RecordPayment_CrossTenant_ThrowsTenantMismatchException()
    {
        var invoice = CreateDraftInvoice();
        invoice.AddLineItem(MakeLineItem());
        invoice.Issue(DateTime.UtcNow.AddDays(30));

        var differentTenant = Guid.NewGuid();
        var act = () => invoice.RecordPayment(Money.Of(50m, "USD"), differentTenant);
        act.Should().Throw<TenantMismatchException>();
    }

    [Fact]
    public void RecordPayment_OnDraftInvoice_ThrowsDomainException()
    {
        var invoice = CreateDraftInvoice();
        invoice.AddLineItem(MakeLineItem());

        var act = () => invoice.RecordPayment(Money.Of(50m, "USD"), TenantId);
        act.Should().Throw<DomainException>().WithMessage("*draft*");
    }

    [Fact]
    public void RecordPayment_OnVoidedInvoice_ThrowsDomainException()
    {
        var invoice = CreateDraftInvoice();
        invoice.Void("Error");

        var act = () => invoice.RecordPayment(Money.Of(50m, "USD"), TenantId);
        act.Should().Throw<DomainException>().WithMessage("*voided*");
    }

    [Fact]
    public void RecordPayment_RaisesPaidEventWhenFullyPaid()
    {
        var invoice = CreateDraftInvoice();
        invoice.AddLineItem(MakeLineItem(unitPrice: 100m));
        invoice.Issue(DateTime.UtcNow.AddDays(30));
        invoice.ClearDomainEvents(); // clear the IssuedEvent

        invoice.RecordPayment(Money.Of(100m, "USD"), TenantId);

        invoice.DomainEvents.Should().HaveCount(1);
        invoice.DomainEvents.First().Should().BeOfType<ledgerflowApi.Domain.Events.InvoicePaidEvent>();
    }

    // ── ReversePayment ────────────────────────────────────────────────────────

    [Fact]
    public void ReversePayment_AfterPartialPayment_RecalculatesStatus()
    {
        var invoice = CreateDraftInvoice();
        invoice.AddLineItem(MakeLineItem(unitPrice: 100m));
        invoice.Issue(DateTime.UtcNow.AddDays(30));
        invoice.RecordPayment(Money.Of(50m, "USD"), TenantId);

        invoice.ReversePayment(Money.Of(50m, "USD"), TenantId);

        invoice.PaidAmount.IsZero.Should().BeTrue();
        invoice.Status.Should().Be(InvoiceStatus.Issued);
    }

    [Fact]
    public void ReversePayment_MoreThanPaid_ThrowsDomainException()
    {
        var invoice = CreateDraftInvoice();
        invoice.AddLineItem(MakeLineItem(unitPrice: 100m));
        invoice.Issue(DateTime.UtcNow.AddDays(30));
        invoice.RecordPayment(Money.Of(30m, "USD"), TenantId);

        var act = () => invoice.ReversePayment(Money.Of(50m, "USD"), TenantId);
        act.Should().Throw<DomainException>().WithMessage("*Cannot reverse*");
    }

    // ── Financial calculations ────────────────────────────────────────────────

    [Fact]
    public void Subtotal_WithMultipleLineItems_SumsNetAmounts()
    {
        var invoice = CreateDraftInvoice();
        invoice.AddLineItem(MakeLineItem(unitPrice: 100m, qty: 2m));   // 200
        invoice.AddLineItem(MakeLineItem(unitPrice: 50m, qty: 1m));    // 50

        invoice.Subtotal.Amount.Should().Be(250m);
    }

    [Fact]
    public void TotalAmount_WithTaxAndDiscount_CalculatesCorrectly()
    {
        // 100 subtotal, 10% invoice discount → 90 discounted subtotal
        // 90 * 20% tax = 18 tax
        // Total = 90 + 18 = 108
        var invoice = CreateDraftInvoice(taxRate: 20m, discount: 10m);
        invoice.AddLineItem(MakeLineItem(unitPrice: 100m));

        invoice.Subtotal.Amount.Should().Be(100m);
        invoice.DiscountedSubtotal.Amount.Should().Be(90m);
        invoice.TaxAmount.Amount.Should().Be(18m);
        invoice.TotalAmount.Amount.Should().Be(108m);
    }

    [Fact]
    public void OutstandingAmount_NoPayments_EqualsTotalAmount()
    {
        var invoice = CreateDraftInvoice();
        invoice.AddLineItem(MakeLineItem(unitPrice: 100m));

        invoice.OutstandingAmount.Should().Be(invoice.TotalAmount);
    }
}
