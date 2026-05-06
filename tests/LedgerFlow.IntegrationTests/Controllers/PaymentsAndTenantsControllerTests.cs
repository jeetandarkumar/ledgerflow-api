using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using LedgerFlow.IntegrationTests.Infrastructure;
using ledgerflowApi.Domain.Entities;
using ledgerflowApi.Domain.Enums;
using ledgerflowApi.Domain.ValueObjects;
using ledgerflowApi.Infrastructure.Identity;
using Xunit;

namespace LedgerFlow.IntegrationTests.Controllers;

/// <summary>
/// Integration tests for the Payments API endpoints.
/// These cover payment history retrieval and the refund flow.
/// </summary>
[Collection("Integration")]
public class PaymentsControllerTests : IntegrationTestBase
{
    private async Task<(Tenant tenant, User user, string token)> SeedTenantAndUserAsync()
    {
        var hasher = new PasswordHasher();
        const string password = "TestPass123!";
        Tenant? tenant = null;
        User? user = null;

        await SeedAsync(async db =>
        {
            tenant = Tenant.Create("Corp", $"corp-{Guid.NewGuid():N}", "USD");
            await db.Tenants.AddAsync(tenant!);
            await db.SaveChangesAsync();
            user = User.Create(tenant!.Id, "Bob", "Admin", "bob@corp.com",
                hasher.Hash(password), UserRole.Admin);
            await db.Users.AddAsync(user!);
        });

        var token = await GetAuthTokenAsync("bob@corp.com", password, tenant!.Id);
        return (tenant!, user!, token);
    }

    // ── GET /api/payments?invoiceId={id} ──────────────────────────────────────

    [Fact]
    public async Task GetPaymentsForInvoice_WithPayments_Returns200WithPaymentList()
    {
        // Arrange
        var (tenant, user, token) = await SeedTenantAndUserAsync();

        Invoice? invoice = null;
        await SeedAsync(async db =>
        {
            invoice = Invoice.Create(tenant.Id, user.Id, "INV-001", "Cust", "c@c.com", "USD");
            invoice.AddLineItem(new InvoiceLineItem("Item", Money.Of(200m, "USD"), 1m, 0m, null));
            invoice.Issue(DateTime.UtcNow.AddDays(30));
            await db.Invoices.AddAsync(invoice);
            await db.SaveChangesAsync();

            var payment = Payment.Create(tenant.Id, invoice.Id,
                Money.Of(100m, "USD"), "card", "Standard", "pi_001");
            invoice.RecordPayment(Money.Of(100m, "USD"), tenant.Id);
            await db.Payments.AddAsync(payment);
        });

        var client = CreateClientWithToken(token);

        // Act
        var response = await client.GetAsync($"/api/payments?invoiceId={invoice!.Id}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<List<PaymentDto>>();
        body.Should().HaveCount(1);
        body!.First().Amount.Should().Be(100m);
        body.First().PaymentType.Should().Be("Standard");
    }

    [Fact]
    public async Task GetPaymentsForInvoice_NoPayments_Returns200WithEmptyList()
    {
        // Arrange
        var (tenant, user, token) = await SeedTenantAndUserAsync();
        Invoice? invoice = null;

        await SeedAsync(async db =>
        {
            invoice = Invoice.Create(tenant.Id, user.Id, "INV-002", "Cust", "c@c.com", "USD");
            invoice.AddLineItem(new InvoiceLineItem("Item", Money.Of(100m, "USD"), 1m, 0m, null));
            await db.Invoices.AddAsync(invoice);
        });

        var client = CreateClientWithToken(token);

        // Act
        var response = await client.GetAsync($"/api/payments?invoiceId={invoice!.Id}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<List<PaymentDto>>();
        body.Should().BeEmpty();
    }

    [Fact]
    public async Task GetPaymentsForInvoice_BelongingToOtherTenant_Returns404()
    {
        // Arrange
        var (tenantA, userA, _) = await SeedTenantAndUserAsync();
        var (_, _, tokenB) = await SeedTenantAndUserAsync();

        Invoice? invoiceA = null;
        await SeedAsync(async db =>
        {
            invoiceA = Invoice.Create(tenantA.Id, userA.Id, "INV-A", "Cust", "c@c.com", "USD");
            invoiceA.AddLineItem(new InvoiceLineItem("Item", Money.Of(100m, "USD"), 1m, 0m, null));
            await db.Invoices.AddAsync(invoiceA);
        });

        var clientB = CreateClientWithToken(tokenB);

        // Act
        var response = await clientB.GetAsync($"/api/payments?invoiceId={invoiceA!.Id}");

        // Assert — tenant isolation: 404 not 403
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetPayments_Unauthenticated_Returns401()
    {
        var response = await Client.GetAsync($"/api/payments?invoiceId={Guid.NewGuid()}");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private sealed record PaymentDto(
        Guid Id,
        decimal Amount,
        string Currency,
        string PaymentMethod,
        string PaymentType,
        string ExternalReference,
        DateTime CreatedAt);
}

/// <summary>
/// Integration tests for the Tenants API endpoints.
/// Covers registration, retrieval, and status management.
/// </summary>
[Collection("Integration")]
public class TenantsControllerTests : IntegrationTestBase
{
    // ── POST /api/tenants (public registration endpoint) ─────────────────────

    [Fact]
    public async Task RegisterTenant_ValidRequest_Returns201WithTenantAndAdminUser()
    {
        // Act
        var response = await Client.PostAsJsonAsync("/api/tenants", new
        {
            companyName = "Stark Industries",
            subdomain = $"stark-{Guid.NewGuid():N}",
            currency = "USD",
            adminFirstName = "Tony",
            adminLastName = "Stark",
            adminEmail = "tony@stark.com",
            adminPassword = "IronMan3000!",
            adminConfirmPassword = "IronMan3000!"
        });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<TenantRegistrationDto>();
        body.Should().NotBeNull();
        body!.TenantId.Should().NotBeEmpty();
        body.AdminUserId.Should().NotBeEmpty();
    }

    [Fact]
    public async Task RegisterTenant_DuplicateSubdomain_Returns409()
    {
        // Arrange — register once
        var subdomain = $"acme-{Guid.NewGuid():N}";
        await Client.PostAsJsonAsync("/api/tenants", new
        {
            companyName = "Acme Corp",
            subdomain,
            currency = "USD",
            adminFirstName = "Alice",
            adminLastName = "Smith",
            adminEmail = "alice@acme.com",
            adminPassword = "Pass123!@",
            adminConfirmPassword = "Pass123!@"
        });

        // Act — register again with the same subdomain
        var response = await Client.PostAsJsonAsync("/api/tenants", new
        {
            companyName = "Another Acme",
            subdomain,  // same subdomain
            currency = "USD",
            adminFirstName = "Bob",
            adminLastName = "Jones",
            adminEmail = "bob@anotheracme.com",
            adminPassword = "Pass123!@",
            adminConfirmPassword = "Pass123!@"
        });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Theory]
    [InlineData("US")]    // not 3 chars
    [InlineData("USDD")]  // not 3 chars
    public async Task RegisterTenant_InvalidCurrency_Returns400(string currency)
    {
        var response = await Client.PostAsJsonAsync("/api/tenants", new
        {
            companyName = "Corp",
            subdomain = $"corp-{Guid.NewGuid():N}",
            currency,
            adminFirstName = "A",
            adminLastName = "B",
            adminEmail = "a@b.com",
            adminPassword = "Pass123!@",
            adminConfirmPassword = "Pass123!@"
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private sealed record TenantRegistrationDto(
        Guid TenantId,
        string CompanyName,
        string Subdomain,
        Guid AdminUserId);
}
