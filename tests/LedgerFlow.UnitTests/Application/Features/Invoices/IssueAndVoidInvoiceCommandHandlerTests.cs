using FluentAssertions;
using ledgerflowApi.Application.Common.Interfaces;
using ledgerflowApi.Application.Features.Invoices.Commands.IssueInvoice;
using ledgerflowApi.Application.Features.Invoices.Commands.VoidInvoice;
using ledgerflowApi.Domain.Entities;
using ledgerflowApi.Domain.Exceptions;
using ledgerflowApi.Domain.Interfaces;
using ledgerflowApi.Domain.ValueObjects;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace LedgerFlow.UnitTests.Application.Features.Invoices;

/// <summary>
/// Unit tests for IssueInvoiceCommandHandler and VoidInvoiceCommandHandler.
/// Both handlers follow the same pattern: fetch invoice → validate → call domain
/// method → persist → return result.
/// </summary>
public class IssueInvoiceCommandHandlerTests
{
    private readonly Mock<IInvoiceRepository> _invoiceRepo = new();
    private readonly Mock<IAuditLogRepository> _auditRepo = new();
    private readonly Mock<ICurrentUserService> _currentUser = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Mock<ILogger<IssueInvoiceCommandHandler>> _logger = new();

    private IssueInvoiceCommandHandler CreateHandler()
        => new(_invoiceRepo.Object, _auditRepo.Object,
               _unitOfWork.Object, _logger.Object);

    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    private Invoice CreateDraftWithLineItem()
    {
        var inv = Invoice.Create(TenantId, UserId, "INV-001", "Cust", "c@c.com", "USD");
        inv.AddLineItem(new InvoiceLineItem("Item", Money.Of(100m, "USD"), 1m, 0m, null));
        return inv;
    }

    private void SetupCommon(Invoice invoice)
    {
        _invoiceRepo.Setup(r => r.GetByIdAsync(invoice.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(invoice);
        _currentUser.Setup(u => u.UserId).Returns(UserId);
        _currentUser.Setup(u => u.TenantId).Returns(TenantId);
        _currentUser.Setup(u => u.UserName).Returns("Alice");
        _unitOfWork.Setup(u => u.ExecuteInTransactionAsync(
                It.IsAny<Func<Task>>(), It.IsAny<CancellationToken>()))
            .Callback<Func<Task>, CancellationToken>((fn, _) => fn().GetAwaiter().GetResult());
    }

    // ── IssueInvoice ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_ValidDraftInvoice_TransitionsToIssuedAndReturnsResponse()
    {
        // Arrange
        var invoice = CreateDraftWithLineItem();
        SetupCommon(invoice);
        var dueDate = DateTime.UtcNow.AddDays(30);
        var command = new IssueInvoiceCommand(invoice.Id, TenantId, invoice.CreatedByUserId, "", dueDate);
        var handler = CreateHandler();

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.Succeeded.Should().BeTrue();
        result.Data!.Status.Should().Be("Issued");
        result.Data.DueDate.Should().Be(dueDate);
        result.Data.IssuedAt.Should().NotBeNull();
    }

    //[Fact]
    //public async Task Handle_InvoiceNotFound_ThrowsNotFoundException()
    //{
    //    // Arrange
    //    _invoiceRepo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
    //        .ReturnsAsync((Invoice?)null);
    //    _currentUser.Setup(u => u.TenantId).Returns(TenantId);

    //    var handler = CreateHandler();
    //    var command = new IssueInvoiceCommand( TenantId, Guid.NewGuid(), DateTime.UtcNow.AddDays(30));

    //    // Act
    //    var act = async () => await handler.Handle(command, CancellationToken.None);

    //    // Assert
    //    await act.Should().ThrowAsync<NotFoundException>();
    //}

    [Fact]
    public async Task Handle_InvoiceWithNoLineItems_ThrowsDomainException()
    {
        // Arrange — draft with no line items
        var invoice = Invoice.Create(TenantId, UserId, "INV-EMPTY", "Cust", "c@c.com", "USD");
        SetupCommon(invoice);

        var handler = CreateHandler();
        var command = new IssueInvoiceCommand(invoice.Id, TenantId, UserId, "", DateTime.UtcNow.AddDays(30));

        // Act
        var act = async () => await handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<DomainException>().WithMessage("*no line items*");
    }

    [Fact]
    public async Task Handle_PastDueDate_ThrowsDomainException()
    {
        // Arrange
        var invoice = CreateDraftWithLineItem();
        SetupCommon(invoice);

        var handler = CreateHandler();
        var command = new IssueInvoiceCommand(invoice.Id, TenantId, UserId, "", DateTime.UtcNow.AddDays(-1));

        // Act
        var act = async () => await handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<DomainException>().WithMessage("*future*");
    }

    [Fact]
    public async Task Handle_AlreadyIssuedInvoice_ThrowsInvalidStatusTransitionException()
    {
        // Arrange
        var invoice = CreateDraftWithLineItem();
        invoice.Issue(DateTime.UtcNow.AddDays(30)); // already issued
        SetupCommon(invoice);

        var handler = CreateHandler();
        var command = new IssueInvoiceCommand(invoice.Id, TenantId, UserId, "", DateTime.UtcNow.AddDays(60));

        // Act
        var act = async () => await handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidStatusTransitionException>();
    }

    [Fact]
    public async Task Handle_Success_PersistsUpdatesAndAuditLog()
    {
        // Arrange
        var invoice = CreateDraftWithLineItem();
        SetupCommon(invoice);
        var handler = CreateHandler();
        var command = new IssueInvoiceCommand(invoice.Id, TenantId, UserId, "", DateTime.UtcNow.AddDays(30));

        // Act
        await handler.Handle(command, CancellationToken.None);

        // Assert
        _invoiceRepo.Verify(r => r.UpdateAsync(invoice, It.IsAny<CancellationToken>()), Times.Once);
        _auditRepo.Verify(r => r.AddAsync(It.IsAny<AuditLog>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}

public class VoidInvoiceCommandHandlerTests
{
    private readonly Mock<IInvoiceRepository> _invoiceRepo = new();
    private readonly Mock<IAuditLogRepository> _auditRepo = new();
    private readonly Mock<ICurrentUserService> _currentUser = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Mock<ILogger<VoidInvoiceCommandHandler>> _logger = new();

    private VoidInvoiceCommandHandler CreateHandler()
        => new(_invoiceRepo.Object, _auditRepo.Object,
               _unitOfWork.Object, _logger.Object);

    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    private void SetupCommon(Invoice invoice)
    {
        _invoiceRepo.Setup(r => r.GetByIdAsync(invoice.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(invoice);
        _currentUser.Setup(u => u.UserId).Returns(UserId);
        _currentUser.Setup(u => u.TenantId).Returns(TenantId);
        _currentUser.Setup(u => u.UserName).Returns("Alice");
        _unitOfWork.Setup(u => u.ExecuteInTransactionAsync(
                It.IsAny<Func<Task>>(), It.IsAny<CancellationToken>()))
            .Callback<Func<Task>, CancellationToken>((fn, _) => fn().GetAwaiter().GetResult());
    }

    // ── VoidInvoice ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_DraftInvoice_VoidsSuccessfully()
    {
        // Arrange
        var invoice = Invoice.Create(TenantId, UserId, "INV-001", "Cust", "c@c.com", "USD");
        SetupCommon(invoice);

        var handler = CreateHandler();
        var command = new VoidInvoiceCommand(invoice.Id, TenantId, UserId, "", "Entered in error");

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.Succeeded.Should().BeTrue();
        result.Data!.Status.Should().Be("Voided");
    }

    [Fact]
    public async Task Handle_IssuedInvoice_VoidsSuccessfully()
    {
        // Arrange
        var invoice = Invoice.Create(TenantId, UserId, "INV-001", "Cust", "c@c.com", "USD");
        invoice.AddLineItem(new InvoiceLineItem("Item", Money.Of(100m, "USD"), 1m, 0m, null));
        invoice.Issue(DateTime.UtcNow.AddDays(30));
        SetupCommon(invoice);

        var handler = CreateHandler();
        var command = new VoidInvoiceCommand(invoice.Id, TenantId, UserId, "", "Customer cancelled");

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.Succeeded.Should().BeTrue();
        result.Data!.Status.Should().Be("Voided");
    }

    [Fact]
    public async Task Handle_PaidInvoice_ThrowsDomainException()
    {
        // Arrange — fully paid invoice
        var invoice = Invoice.Create(TenantId, UserId, "INV-001", "Cust", "c@c.com", "USD");
        invoice.AddLineItem(new InvoiceLineItem("Item", Money.Of(100m, "USD"), 1m, 0m, null));
        invoice.Issue(DateTime.UtcNow.AddDays(30));
        invoice.RecordPayment(Money.Of(100m, "USD"), TenantId);
        SetupCommon(invoice);

        var handler = CreateHandler();
        var command = new VoidInvoiceCommand(invoice.Id, TenantId, UserId, "", "Trying to void paid");

        // Act
        var act = async () => await handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<DomainException>().WithMessage("*fully paid*");
    }

    [Fact]
    public async Task Handle_EmptyReason_ThrowsDomainException()
    {
        // Arrange
        var invoice = Invoice.Create(TenantId, UserId, "INV-001", "Cust", "c@c.com", "USD");
        SetupCommon(invoice);

        var handler = CreateHandler();
        var command = new VoidInvoiceCommand(invoice.Id, TenantId, UserId, "", ""); // empty reason

        // Act
        var act = async () => await handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<DomainException>().WithMessage("*reason*");
    }

    //[Fact]
    //public async Task Handle_NonExistentInvoice_ThrowsNotFoundException()
    //{
    //    // Arrange
    //    _invoiceRepo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
    //        .ReturnsAsync((Invoice?)null);
    //    _currentUser.Setup(u => u.TenantId).Returns(TenantId);

    //    var handler = CreateHandler();
    //    var command = new VoidInvoiceCommand(TenantId, Guid.NewGuid(), "Some reason");

    //    // Act
    //    var act = async () => await handler.Handle(command, CancellationToken.None);

    //    // Assert
    //    await act.Should().ThrowAsync<NotFoundException>();
    //}

    [Fact]
    public async Task Handle_Success_PersistsUpdateAndAuditLog()
    {
        // Arrange
        var invoice = Invoice.Create(TenantId, UserId, "INV-001", "Cust", "c@c.com", "USD");
        SetupCommon(invoice);
        var handler = CreateHandler();

        // Act
        await handler.Handle(new VoidInvoiceCommand(invoice.Id, TenantId, UserId, "", "Reason"), CancellationToken.None);

        // Assert
        _invoiceRepo.Verify(r => r.UpdateAsync(invoice, It.IsAny<CancellationToken>()), Times.Once);
        _auditRepo.Verify(r => r.AddAsync(It.IsAny<AuditLog>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
