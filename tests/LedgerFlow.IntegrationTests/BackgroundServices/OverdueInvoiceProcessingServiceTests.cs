using FluentAssertions;
using LedgerFlow.IntegrationTests.Infrastructure;
using ledgerflowApi.API.BackgroundServices;
using ledgerflowApi.Domain.Entities;
using ledgerflowApi.Domain.Enums;
using ledgerflowApi.Domain.Interfaces;
using ledgerflowApi.Domain.ValueObjects;
using ledgerflowApi.Infrastructure.Identity;
using ledgerflowApi.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LedgerFlow.IntegrationTests.BackgroundServices;

/// <summary>
/// Integration tests for OverdueInvoiceProcessingService.
///
/// The hosted-service registration is skipped in the "Testing" environment (see Program.cs)
/// so these tests construct the service directly with dependencies resolved from the running
/// factory, and call ProcessOverdueInvoicesAsync() deterministically instead of waiting on
/// its internal timer loop.
/// </summary>
[Collection("Integration")]
public class OverdueInvoiceProcessingServiceTests : IntegrationTestBase
{
    private OverdueInvoiceProcessingService CreateService()
    {
        var scopeFactory = Factory.Services.GetRequiredService<IServiceScopeFactory>();
        var configuration = Factory.Services.GetRequiredService<IConfiguration>();

        return new OverdueInvoiceProcessingService(
            scopeFactory,
            NullLogger<OverdueInvoiceProcessingService>.Instance,
            configuration);
    }

    private async Task<(Tenant tenant, User user)> SeedTenantAndUserAsync()
    {
        var hasher = new PasswordHasher();
        Tenant? tenant = null;
        User? user = null;

        await SeedAsync(async db =>
        {
            tenant = Tenant.Create("Acme Corp", $"acme-{Guid.NewGuid():N}", "billing@acme.com", "USD");
            await db.Tenants.AddAsync(tenant!);
            await db.SaveChangesAsync();

            user = User.Create(tenant!.Id, "Alice", "Smith", "alice@acme.com",
                hasher.Hash("TestPassword123!"), UserRole.Admin);
            await db.Users.AddAsync(user!);
        });

        return (tenant!, user!);
    }

    /// <summary>Issues an invoice with a near-future due date, then forces it into the past
    /// via reflection — the same technique already used in InvoiceTests.MarkAsOverdue tests,
    /// since Invoice.Issue() itself rejects a due date that isn't in the future.</summary>
    private static void BackdateDueDate(Invoice invoice, TimeSpan pastBy)
    {
        typeof(Invoice).GetProperty(nameof(Invoice.DueDate))!
            .SetValue(invoice, DateTime.UtcNow.Subtract(pastBy));
    }

    [Fact]
    public async Task ProcessOverdueInvoicesAsync_IssuedInvoicePastDueDate_IsMarkedOverdue()
    {
        var (tenant, user) = await SeedTenantAndUserAsync();
        Invoice invoice = null!;

        await SeedAsync(async db =>
        {
            invoice = Invoice.Create(tenant.Id, user.Id, "INV-OD-001", "Cust", "c@c.com", "USD");
            invoice.AddLineItem(new InvoiceLineItem("Item", Money.Of(100m, "USD"), 1m, 0m, null));
            invoice.Issue(DateTime.UtcNow.AddSeconds(1));
            BackdateDueDate(invoice, TimeSpan.FromDays(5));
            await db.Invoices.AddAsync(invoice);
        });

        var service = CreateService();
        var markedCount = await service.ProcessOverdueInvoicesAsync(CancellationToken.None);

        markedCount.Should().Be(1);

        using var scope = Factory.Services.CreateScope();
        var invoiceRepository = scope.ServiceProvider.GetRequiredService<IInvoiceRepository>();
        var reloaded = await invoiceRepository.GetByIdAsync(invoice.Id);
        reloaded!.Status.Should().Be(InvoiceStatus.Overdue);
    }

    [Fact]
    public async Task ProcessOverdueInvoicesAsync_PartiallyPaidInvoicePastDueDate_IsMarkedOverdue()
    {
        var (tenant, user) = await SeedTenantAndUserAsync();
        Invoice invoice = null!;

        await SeedAsync(async db =>
        {
            invoice = Invoice.Create(tenant.Id, user.Id, "INV-OD-002", "Cust", "c@c.com", "USD");
            invoice.AddLineItem(new InvoiceLineItem("Item", Money.Of(200m, "USD"), 1m, 0m, null));
            invoice.Issue(DateTime.UtcNow.AddSeconds(1));
            invoice.RecordPayment(Money.Of(50m, "USD"), tenant.Id);
            BackdateDueDate(invoice, TimeSpan.FromDays(1));
            await db.Invoices.AddAsync(invoice);
        });

        var service = CreateService();
        var markedCount = await service.ProcessOverdueInvoicesAsync(CancellationToken.None);

        markedCount.Should().Be(1);

        using var scope = Factory.Services.CreateScope();
        var invoiceRepository = scope.ServiceProvider.GetRequiredService<IInvoiceRepository>();
        var reloaded = await invoiceRepository.GetByIdAsync(invoice.Id);
        reloaded!.Status.Should().Be(InvoiceStatus.Overdue);
    }

    [Fact]
    public async Task ProcessOverdueInvoicesAsync_PaidInvoicePastDueDate_IsNotTouched()
    {
        // Paid invoices are excluded by the repository query itself (only Issued/PartiallyPaid
        // are candidates), and MarkAsOverdue() would reject Paid -> Overdue even if it weren't.
        var (tenant, user) = await SeedTenantAndUserAsync();
        Invoice invoice = null!;

        await SeedAsync(async db =>
        {
            invoice = Invoice.Create(tenant.Id, user.Id, "INV-OD-003", "Cust", "c@c.com", "USD");
            invoice.AddLineItem(new InvoiceLineItem("Item", Money.Of(100m, "USD"), 1m, 0m, null));
            invoice.Issue(DateTime.UtcNow.AddSeconds(1));
            invoice.RecordPayment(Money.Of(100m, "USD"), tenant.Id); // fully paid -> Paid
            BackdateDueDate(invoice, TimeSpan.FromDays(10));
            await db.Invoices.AddAsync(invoice);
        });

        var service = CreateService();
        var markedCount = await service.ProcessOverdueInvoicesAsync(CancellationToken.None);

        markedCount.Should().Be(0);

        using var scope = Factory.Services.CreateScope();
        var invoiceRepository = scope.ServiceProvider.GetRequiredService<IInvoiceRepository>();
        var reloaded = await invoiceRepository.GetByIdAsync(invoice.Id);
        reloaded!.Status.Should().Be(InvoiceStatus.Paid);
    }

    [Fact]
    public async Task ProcessOverdueInvoicesAsync_VoidedInvoicePastDueDate_IsNotTouched()
    {
        var (tenant, user) = await SeedTenantAndUserAsync();
        Invoice invoice = null!;

        await SeedAsync(async db =>
        {
            invoice = Invoice.Create(tenant.Id, user.Id, "INV-OD-004", "Cust", "c@c.com", "USD");
            invoice.AddLineItem(new InvoiceLineItem("Item", Money.Of(100m, "USD"), 1m, 0m, null));
            invoice.Issue(DateTime.UtcNow.AddSeconds(1));
            invoice.Void("Customer cancelled the order.");
            BackdateDueDate(invoice, TimeSpan.FromDays(10));
            await db.Invoices.AddAsync(invoice);
        });

        var service = CreateService();
        var markedCount = await service.ProcessOverdueInvoicesAsync(CancellationToken.None);

        markedCount.Should().Be(0);

        using var scope = Factory.Services.CreateScope();
        var invoiceRepository = scope.ServiceProvider.GetRequiredService<IInvoiceRepository>();
        var reloaded = await invoiceRepository.GetByIdAsync(invoice.Id);
        reloaded!.Status.Should().Be(InvoiceStatus.Voided);
    }

    [Fact]
    public async Task ProcessOverdueInvoicesAsync_InvoiceNotYetDue_IsNotTouched()
    {
        var (tenant, user) = await SeedTenantAndUserAsync();
        Invoice invoice = null!;

        await SeedAsync(async db =>
        {
            invoice = Invoice.Create(tenant.Id, user.Id, "INV-OD-005", "Cust", "c@c.com", "USD");
            invoice.AddLineItem(new InvoiceLineItem("Item", Money.Of(100m, "USD"), 1m, 0m, null));
            invoice.Issue(DateTime.UtcNow.AddDays(30)); // due in the future, no backdating
            await db.Invoices.AddAsync(invoice);
        });

        var service = CreateService();
        var markedCount = await service.ProcessOverdueInvoicesAsync(CancellationToken.None);

        markedCount.Should().Be(0);

        using var scope = Factory.Services.CreateScope();
        var invoiceRepository = scope.ServiceProvider.GetRequiredService<IInvoiceRepository>();
        var reloaded = await invoiceRepository.GetByIdAsync(invoice.Id);
        reloaded!.Status.Should().Be(InvoiceStatus.Issued);
    }

    [Fact]
    public async Task ProcessOverdueInvoicesAsync_RunTwiceInARow_IsIdempotent()
    {
        // Running the job again after invoices are already Overdue must not throw and must
        // not re-mark them (they're no longer Issued/PartiallyPaid, so the repository query
        // won't even return them a second time).
        var (tenant, user) = await SeedTenantAndUserAsync();
        Invoice invoice = null!;

        await SeedAsync(async db =>
        {
            invoice = Invoice.Create(tenant.Id, user.Id, "INV-OD-006", "Cust", "c@c.com", "USD");
            invoice.AddLineItem(new InvoiceLineItem("Item", Money.Of(100m, "USD"), 1m, 0m, null));
            invoice.Issue(DateTime.UtcNow.AddSeconds(1));
            BackdateDueDate(invoice, TimeSpan.FromDays(2));
            await db.Invoices.AddAsync(invoice);
        });

        var service = CreateService();

        var firstRun = await service.ProcessOverdueInvoicesAsync(CancellationToken.None);
        var secondRun = await service.ProcessOverdueInvoicesAsync(CancellationToken.None);

        firstRun.Should().Be(1);
        secondRun.Should().Be(0);

        using var scope = Factory.Services.CreateScope();
        var invoiceRepository = scope.ServiceProvider.GetRequiredService<IInvoiceRepository>();
        var reloaded = await invoiceRepository.GetByIdAsync(invoice.Id);
        reloaded!.Status.Should().Be(InvoiceStatus.Overdue);
    }

    [Fact]
    public async Task ProcessOverdueInvoicesAsync_WritesAuditLogEntry()
    {
        var (tenant, user) = await SeedTenantAndUserAsync();
        Invoice invoice = null!;

        await SeedAsync(async db =>
        {
            invoice = Invoice.Create(tenant.Id, user.Id, "INV-OD-007", "Cust", "c@c.com", "USD");
            invoice.AddLineItem(new InvoiceLineItem("Item", Money.Of(100m, "USD"), 1m, 0m, null));
            invoice.Issue(DateTime.UtcNow.AddSeconds(1));
            BackdateDueDate(invoice, TimeSpan.FromDays(3));
            await db.Invoices.AddAsync(invoice);
        });

        var service = CreateService();
        await service.ProcessOverdueInvoicesAsync(CancellationToken.None);

        using var scope = Factory.Services.CreateScope();
        var auditLogRepository = scope.ServiceProvider.GetRequiredService<IAuditLogRepository>();
        var entries = await auditLogRepository.GetForEntityAsync(tenant.Id, nameof(Invoice), invoice.Id);

        entries.Should().Contain(e =>
            e.Action == AuditAction.StatusChanged &&
            e.UserId == null &&
            e.UserDisplayName == "System (Overdue Processing Job)");
    }

    [Fact]
    public async Task ProcessOverdueInvoicesAsync_NoEligibleInvoices_ReturnsZeroAndDoesNotThrow()
    {
        var service = CreateService();

        var act = async () => await service.ProcessOverdueInvoicesAsync(CancellationToken.None);

        (await act.Should().NotThrowAsync()).Which.Should().Be(0);
    }
}
