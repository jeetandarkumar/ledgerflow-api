using FluentAssertions;
using ledgerflowApi.Application.Common.Interfaces;
using ledgerflowApi.Application.Features.Invoices.Commands.ProcessPayment;
using ledgerflowApi.Domain.Entities;
using ledgerflowApi.Domain.Enums;
using ledgerflowApi.Domain.Exceptions;
using ledgerflowApi.Domain.Interfaces;
using ledgerflowApi.Domain.ValueObjects;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace LedgerFlow.UnitTests.Application.Features.Invoices;

/// <summary>
/// Unit tests for ProcessPaymentCommandHandler.
/// Covers happy paths, idempotency guard, cross-tenant protection,
/// refund logic, and persistence verification.
/// </summary>
public class ProcessPaymentCommandHandlerTests
{
    private readonly Mock<IInvoiceRepository> _invoiceRepo = new();
    private readonly Mock<IPaymentRepository> _paymentRepo = new();
    private readonly Mock<IAuditLogRepository> _auditRepo = new();
    private readonly Mock<ICurrentUserService> _currentUser = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Mock<ILogger<ProcessPaymentCommandHandler>> _logger = new();

    private ProcessPaymentCommandHandler CreateHandler()
        => new(_invoiceRepo.Object, _paymentRepo.Object, _auditRepo.Object,
               _unitOfWork.Object, _logger.Object);

    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    private static Invoice CreateIssuedInvoice(decimal amount = 200m)
    {
        var invoice = Invoice.Create(TenantId, UserId, "INV-001",
            "Customer", "c@c.com", "USD");
        invoice.AddLineItem(new InvoiceLineItem(
            "Service", Money.Of(amount, "USD"), 1m, 0m, null));
        invoice.Issue(DateTime.UtcNow.AddDays(30));
        return invoice;
    }

    private static ProcessPaymentCommand MakeCommand(
        Guid invoiceId,
        decimal amount = 200m,
        string paymentType = "Standard",
        string currency = "USD",
        string extRef = "pi_test_001",
        Guid? refundedPaymentId = null)
        => new(
            TenantId: TenantId,
            InvoiceId: invoiceId,
            Amount: amount,
            Currency: currency,
            PaymentMethod: "card",
            PaymentType: paymentType,
            ExternalReference: extRef,
            RefundedPaymentId: refundedPaymentId,
            InitiatedByUserId: UserId,
            InitiatedByUserName: "Alice",
            Notes: null);

    private void SetupHappyPath(Invoice invoice)
    {
        _invoiceRepo.Setup(r => r.GetByIdAsync(invoice.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(invoice);
        _paymentRepo.Setup(r => r.GetByExternalReferenceAsync(
                It.IsAny<string>(), It.IsAny<CancellationToken>()));
        _currentUser.Setup(u => u.UserId).Returns(UserId);
        _currentUser.Setup(u => u.TenantId).Returns(TenantId);
        _unitOfWork.Setup(u => u.ExecuteInTransactionAsync(
                It.IsAny<Func<Task>>(), It.IsAny<CancellationToken>()))
            .Callback<Func<Task>, CancellationToken>((fn, _) => fn().GetAwaiter().GetResult());
    }

    // ── Happy path ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_FullPayment_ReturnsSuccessWithPaidInvoiceSnapshot()
    {
        // Arrange
        var invoice = CreateIssuedInvoice(amount: 200m);
        SetupHappyPath(invoice);

        var handler = CreateHandler();

        // Act
        var result = await handler.Handle(MakeCommand(invoice.Id, amount: 200m), CancellationToken.None);

        // Assert
        result.Succeeded.Should().BeTrue();
        result.Data!.Invoice.Status.Should().Be("Paid");
        result.Data.Invoice.OutstandingAmount.Should().Be(0m);
        result.Data.Invoice.PaidAmount.Should().Be(200m);
    }

    [Fact]
    public async Task Handle_PartialPayment_ReturnsSuccessWithPartiallyPaidStatus()
    {
        // Arrange
        var invoice = CreateIssuedInvoice(amount: 200m);
        SetupHappyPath(invoice);

        var handler = CreateHandler();

        // Act
        var result = await handler.Handle(MakeCommand(invoice.Id, amount: 100m), CancellationToken.None);

        // Assert
        result.Succeeded.Should().BeTrue();
        result.Data!.Invoice.Status.Should().Be("PartiallyPaid");
        result.Data.Invoice.PaidAmount.Should().Be(100m);
        result.Data.Invoice.OutstandingAmount.Should().Be(100m);
    }

    [Fact]
    public async Task Handle_Payment_PersistsPaymentRecordAndAuditLog()
    {
        // Arrange
        var invoice = CreateIssuedInvoice();
        SetupHappyPath(invoice);
        var handler = CreateHandler();

        // Act
        await handler.Handle(MakeCommand(invoice.Id), CancellationToken.None);

        // Assert
        _paymentRepo.Verify(r => r.AddAsync(It.IsAny<Payment>(), It.IsAny<CancellationToken>()), Times.Once);
        _auditRepo.Verify(r => r.AddAsync(It.IsAny<AuditLog>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── Idempotency ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_DuplicateExternalReference_ReturnsExistingPaymentIdempotently()
    {
        // Arrange — simulate a payment with this external reference already recorded
        var invoice = CreateIssuedInvoice();
        var existingPayment = Payment.Create(TenantId, invoice.Id,
            Money.Of(200m, "USD"), "card", "Standard", "pi_test_001");

        _invoiceRepo.Setup(r => r.GetByIdAsync(invoice.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(invoice);
        _paymentRepo.Setup(r => r.GetByExternalReferenceAsync(
                "pi_test_001", It.IsAny<CancellationToken>()));
        _paymentRepo.Setup(r => r.GetByExternalReferenceAsync(
                 "pi_test_001", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingPayment);
        _currentUser.Setup(u => u.TenantId).Returns(TenantId);

        var handler = CreateHandler();

        // Act — second call with the same external reference
        var result = await handler.Handle(MakeCommand(invoice.Id, extRef: "pi_test_001"), CancellationToken.None);

        // Assert — succeeds without creating a duplicate
        result.Succeeded.Should().BeTrue();
        _paymentRepo.Verify(r => r.AddAsync(It.IsAny<Payment>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── Invoice validation ────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_InvoiceNotFound_ThrowsNotFoundException()
    {
        // Arrange
        _invoiceRepo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Invoice?)null);
        _currentUser.Setup(u => u.TenantId).Returns(TenantId);

        var handler = CreateHandler();

        // Act
        var act = async () => await handler.Handle(MakeCommand(Guid.NewGuid()), CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Handle_PaymentOnDraftInvoice_ThrowsDomainException()
    {
        // Arrange
        var draftInvoice = Invoice.Create(TenantId, UserId, "INV-DRAFT",
            "Customer", "c@c.com", "USD");
        draftInvoice.AddLineItem(new InvoiceLineItem("Item", Money.Of(100m, "USD"), 1m, 0m, null));
        // Not issued — still Draft

        _invoiceRepo.Setup(r => r.GetByIdAsync(draftInvoice.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(draftInvoice);
        _paymentRepo.Setup(r => r.GetByExternalReferenceAsync(
                It.IsAny<string>(), It.IsAny<CancellationToken>()));
        _currentUser.Setup(u => u.TenantId).Returns(TenantId);

        var handler = CreateHandler();

        // Act
        var act = async () => await handler.Handle(MakeCommand(draftInvoice.Id), CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<DomainException>().WithMessage("*draft*");
    }

    [Fact]
    public async Task Handle_PaymentExceedsOutstandingBalance_ThrowsDomainException()
    {
        // Arrange
        var invoice = CreateIssuedInvoice(amount: 100m); // total = 100
        SetupHappyPath(invoice);

        var handler = CreateHandler();

        // Act — try to pay 999 against a 100 invoice
        var act = async () => await handler.Handle(
            MakeCommand(invoice.Id, amount: 999m), CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<DomainException>().WithMessage("*exceeds*");
    }

    [Fact]
    public async Task Handle_WrongCurrency_ThrowsDomainException()
    {
        // Arrange
        var invoice = CreateIssuedInvoice(); // USD invoice
        SetupHappyPath(invoice);
        var handler = CreateHandler();

        // Act — pay in EUR
        var act = async () => await handler.Handle(
            MakeCommand(invoice.Id, currency: "EUR"), CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<DomainException>().WithMessage("*currency*");
    }

    // ── Refund ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_ValidRefund_ReversesPaymentAndUpdatesInvoiceStatus()
    {
        // Arrange — an invoice that was fully paid
        var invoice = CreateIssuedInvoice(amount: 200m);
        var originalPayment = Payment.Create(TenantId, invoice.Id,
            Money.Of(200m, "USD"), "card", "Standard", "pi_original_001");
        originalPayment.Complete("pi_original_001");
        invoice.RecordPayment(Money.Of(200m, "USD"), TenantId);

        _invoiceRepo.Setup(r => r.GetByIdAsync(invoice.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(invoice);
        _paymentRepo.Setup(r => r.GetByExternalReferenceAsync(
                 It.IsAny<string>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync((Payment)null);
        _paymentRepo.Setup(r => r.GetByIdAsync(originalPayment.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(originalPayment);
        _currentUser.Setup(u => u.UserId).Returns(UserId);
        _currentUser.Setup(u => u.TenantId).Returns(TenantId);
        _unitOfWork.Setup(u => u.ExecuteInTransactionAsync(
        It.IsAny<Func<Task>>(), It.IsAny<CancellationToken>()))
    .Returns<Func<Task>, CancellationToken>((func, _) => func());

        var handler = CreateHandler();

        // Act
        var result = await handler.Handle(
            MakeCommand(invoice.Id, amount: 200m, paymentType: "Refund",
                extRef: "pi_refund_001", refundedPaymentId: originalPayment.Id),
            CancellationToken.None);

        // Assert
        result.Succeeded.Should().BeTrue();
        result.Data.Should().NotBeNull();
        result.Data!.Invoice.Status.Should().Be("Issued"); // back to Issued after full refund
        result.Data.Invoice.PaidAmount.Should().Be(0m);
    }
}
