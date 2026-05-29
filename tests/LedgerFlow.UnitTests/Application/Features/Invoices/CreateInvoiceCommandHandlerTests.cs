using FluentAssertions;
using ledgerflowApi.Application.Common.Interfaces;
using ledgerflowApi.Application.Features.Invoices.Commands.CreateInvoice;
using ledgerflowApi.Domain.Entities;
using ledgerflowApi.Domain.Enums;
using ledgerflowApi.Domain.Interfaces;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace LedgerFlow.UnitTests.Application.Features.Invoices;

/// <summary>
/// Unit tests for CreateInvoiceCommandHandler.
/// The handler orchestrates tenant validation, invoice number generation,
/// aggregate construction, and transactional persistence.
/// </summary>
public class CreateInvoiceCommandHandlerTests
{
    private readonly Mock<IInvoiceRepository> _invoiceRepo = new();
    private readonly Mock<IAuditLogRepository> _auditRepo = new();
    private readonly Mock<ITenantRepository> _tenantRepo = new();
    private readonly Mock<ICurrentUserService> _currentUser = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Mock<ILogger<CreateInvoiceCommandHandler>> _logger = new();

    private CreateInvoiceCommandHandler CreateHandler()
        => new(_invoiceRepo.Object, _auditRepo.Object, _tenantRepo.Object,
               _currentUser.Object, _unitOfWork.Object, _logger.Object);

    private static Tenant CreateActiveTenant(Guid? id = null)
    {
        var tenant = (Tenant)Activator.CreateInstance(typeof(Tenant), nonPublic: true)!;
        typeof(Tenant).GetProperty("Id")!.SetValue(tenant, id ?? Guid.NewGuid());
        typeof(Tenant).GetProperty("Name")!.SetValue(tenant, "Acme Corp");
        typeof(Tenant).GetProperty("Status")!.SetValue(tenant, TenantStatus.Active);
        return tenant;
    }

    private static CreateInvoiceCommand MakeCommand(Guid tenantId, int lineItemCount = 1)
        => new(
            TenantId: tenantId,
            CustomerName: "Test Customer",
            CustomerEmail: "customer@test.com",
            Currency: "USD",
            TaxRatePercentage: 10m,
            DiscountPercentage: 0m,
            LineItems: Enumerable.Range(1, lineItemCount)
                .Select(i => new CreateInvoiceLineItemCommand(
                    Description: $"Item {i}",
                    UnitPrice: 100m,
                    Quantity: 1m,
                    DiscountPercentage: 0m,
                    ProductReference: null))
                .ToList(),
            Notes: null,
            BillingAddress: null);

    private void SetupHappyPath(Tenant tenant, Guid userId)
    {
        _tenantRepo.Setup(r => r.GetByIdAsync(tenant.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(tenant);
        _currentUser.Setup(u => u.UserId).Returns(userId);
        _currentUser.Setup(u => u.UserName).Returns("Alice Smith");
        _invoiceRepo.Setup(r => r.GetNextInvoiceSequenceAsync(tenant.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(42);
        _unitOfWork.Setup(u => u.ExecuteInTransactionAsync(It.IsAny<Func<Task>>(), It.IsAny<CancellationToken>()))
            .Callback<Func<Task>, CancellationToken>((fn, _) => fn().GetAwaiter().GetResult());
    }

    // ── Happy path ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_ValidCommand_CreatesInvoiceAndReturnsResponse()
    {
        // Arrange
        var tenant = CreateActiveTenant();
        var userId = Guid.NewGuid();
        var command = MakeCommand(tenant.Id);
        SetupHappyPath(tenant, userId);

        var handler = CreateHandler();

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.Succeeded.Should().BeTrue();
        result.Data.Should().NotBeNull();
        result.Data!.InvoiceNumber.Should().Contain("INV-");
        result.Data!.InvoiceNumber.Should().Contain("000042");
        result.Data!.Status.Should().Be("Draft");
        result.Data!.CustomerEmail.Should().Be("customer@test.com");
    }

    [Fact]
    public async Task Handle_ValidCommand_PersistsInvoiceAndAuditLog()
    {
        // Arrange
        var tenant = CreateActiveTenant();
        var userId = Guid.NewGuid();
        SetupHappyPath(tenant, userId);

        var handler = CreateHandler();

        // Act
        await handler.Handle(MakeCommand(tenant.Id), CancellationToken.None);

        // Assert — both invoice and audit are written
        _invoiceRepo.Verify(r => r.AddAsync(It.IsAny<Invoice>(), It.IsAny<CancellationToken>()), Times.Once);
        _auditRepo.Verify(r => r.AddAsync(It.IsAny<AuditLog>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_MultipleLineItems_AllAddedToInvoice()
    {
        // Arrange
        var tenant = CreateActiveTenant();
        SetupHappyPath(tenant, Guid.NewGuid());
        Invoice? capturedInvoice = null;
        _invoiceRepo.Setup(r => r.AddAsync(It.IsAny<Invoice>(), It.IsAny<CancellationToken>()))
            .Callback<Invoice, CancellationToken>((inv, _) => capturedInvoice = inv);

        var handler = CreateHandler();

        // Act
        await handler.Handle(MakeCommand(tenant.Id, lineItemCount: 3), CancellationToken.None);

        // Assert
        capturedInvoice.Should().NotBeNull();
        capturedInvoice!.LineItems.Should().HaveCount(3);
    }

    // ── Tenant validation ─────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_TenantNotFound_ThrowsNotFoundException()
    {
        // Arrange
        var command = MakeCommand(Guid.NewGuid());
        _tenantRepo.Setup(r => r.GetByIdAsync(command.TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Tenant?)null);

        var handler = CreateHandler();

        // Act
        var act = async () => await handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<ledgerflowApi.Domain.Exceptions.NotFoundException>();
    }

    [Theory]
    [InlineData(TenantStatus.Suspended)]
    [InlineData(TenantStatus.Cancelled)]
    public async Task Handle_InactiveTenant_ThrowsDomainException(TenantStatus status)
    {
        // Arrange
        var tenant = CreateActiveTenant();
        typeof(Tenant).GetProperty("Status")!.SetValue(tenant, status);
        _tenantRepo.Setup(r => r.GetByIdAsync(tenant.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(tenant);
        _currentUser.Setup(u => u.UserId).Returns(Guid.NewGuid());

        var handler = CreateHandler();

        // Act
        var act = async () => await handler.Handle(MakeCommand(tenant.Id), CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<ledgerflowApi.Domain.Exceptions.DomainException>()
            .WithMessage("*cannot create invoices*");
    }

    // ── User context validation ───────────────────────────────────────────────

    [Fact]
    public async Task Handle_NoAuthenticatedUser_ThrowsDomainException()
    {
        // Arrange
        var tenant = CreateActiveTenant();
        _tenantRepo.Setup(r => r.GetByIdAsync(tenant.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(tenant);
        _currentUser.Setup(u => u.UserId).Returns((Guid?)null);

        var handler = CreateHandler();

        // Act
        var act = async () => await handler.Handle(MakeCommand(tenant.Id), CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<ledgerflowApi.Domain.Exceptions.DomainException>()
            .WithMessage("*authenticated user*");
    }

    // ── Invoice number format ─────────────────────────────────────────────────

    [Theory]
    [InlineData(1, "INV-", "000001")]
    [InlineData(999999, "INV-", "999999")]
    public async Task Handle_GeneratesFormattedInvoiceNumber(int sequence, string prefix, string seqPart)
    {
        // Arrange
        var tenant = CreateActiveTenant();
        var userId = Guid.NewGuid();
        SetupHappyPath(tenant, userId);
        _invoiceRepo.Setup(r => r.GetNextInvoiceSequenceAsync(tenant.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(sequence);

        var handler = CreateHandler();

        // Act
        var result = await handler.Handle(MakeCommand(tenant.Id), CancellationToken.None);

        // Assert
        result.Data!.InvoiceNumber.Should().StartWith(prefix);
        result.Data!.InvoiceNumber.Should().Contain(seqPart);
    }
}
