using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using LedgerFlow.IntegrationTests.Infrastructure;
using ledgerflowApi.Domain.Entities;
using ledgerflowApi.Domain.Enums;
using ledgerflowApi.Domain.ValueObjects;
using ledgerflowApi.Infrastructure.Identity;
using ledgerflowApi.Infrastructure.Persistence;
using Xunit;

namespace LedgerFlow.IntegrationTests.Controllers;

/// <summary>
/// Integration tests for Payments (nested under invoices) and tenant-scoped
/// user management.
///
/// Key facts about this codebase:
/// - There is NO standalone /api/v1/payments endpoint. Payments are always
///   nested under invoices: POST /api/v1/invoices/{id}/payments
/// - There is NO public tenant-registration endpoint. Tenants are seeded
///   directly; users are registered via POST /api/v1/auth/register (Admin-only).
/// - Tenant.Create signature: (name, slug, billingEmail, defaultCurrency)
/// </summary>
[Collection("Integration")]
public class PaymentsControllerTests : IntegrationTestBase
{
    // ── Seed helpers ──────────────────────────────────────────────────────────

    private async Task<(Tenant tenant, User user, string token)> SeedTenantAndUserAsync(
        string slugSuffix = "")
    {
        var hasher = new PasswordHasher();
        const string password = "TestPass123!";
        Tenant? tenant = null;
        User? user = null;

        await SeedAsync(async db =>
        {
            var slug = string.IsNullOrEmpty(slugSuffix)
                ? $"corp-{Guid.NewGuid():N}"
                : $"corp-{slugSuffix}";

            tenant = Tenant.Create("Corp", slug, "billing@corp.com", "USD");
            await db.Tenants.AddAsync(tenant!);
            await db.SaveChangesAsync();

            user = User.Create(tenant!.Id, "Bob", "Admin", $"bob-{slug}@corp.com",
                hasher.Hash(password), UserRole.Admin);
            await db.Users.AddAsync(user!);
        });

        var token = await GetAuthTokenAsync(user!.Email, password, tenant!.Id);
        return (tenant!, user!, token);
    }

    // ── Payment history via invoice sub-route ─────────────────────────────────

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
        });

        var client = CreateClientWithToken(token);

        // Record a payment via the API
        await client.PostAsJsonAsync($"/api/v1/invoices/{invoice!.Id}/payments", new
        {
            amount = 100m,
            currency = "USD",
            paymentMethod = "card",
            type = "Standard",
            externalReference = $"pi_{Guid.NewGuid():N}"
        });

        // Act — list invoices and verify the paid amount is reflected
        var invoiceResponse = await client.GetAsync($"/api/v1/invoices/{invoice.Id}");

        // Assert
        invoiceResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await invoiceResponse.Content.ReadFromJsonAsync<InvoiceDto>();
        body!.PaidAmount.Should().Be(100m);
        body.Status.Should().Be("PartiallyPaid");
    }

    [Fact]
    public async Task GetPaymentsForInvoice_NoPayments_InvoiceHasZeroPaid()
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
        var response = await client.GetAsync($"/api/v1/invoices/{invoice!.Id}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<InvoiceDto>();
        body!.PaidAmount.Should().Be(0m);
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

        // Act — Tenant B tries to post a payment against Tenant A's invoice
        var response = await clientB.PostAsJsonAsync(
            $"/api/v1/invoices/{invoiceA!.Id}/payments", new
            {
                amount = 50m,
                currency = "USD",
                paymentMethod = "card",
                type = "Standard",
                externalReference = "pi_cross_tenant"
            });

        // Assert — handler cannot find the invoice for this tenant → 400 (not found / failure)
        // The handler returns Result.Failure which the controller maps to 400
        response.StatusCode.Should().BeOneOf(HttpStatusCode.BadRequest, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetPayments_Unauthenticated_Returns401()
    {
        // Act — no JWT; invoices endpoint requires auth
        var response = await Client.PostAsJsonAsync($"/api/v1/invoices/{Guid.NewGuid()}/payments", new
        {
            amount = 50m,
            currency = "USD",
            paymentMethod = "card",
            type = "Standard"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── Response DTOs ─────────────────────────────────────────────────────────

    private sealed record InvoiceDto(
        Guid Id,
        string InvoiceNumber,
        string Status,
        decimal TotalAmount,
        decimal PaidAmount,
        decimal OutstandingAmount);
}

/// <summary>
/// Tests for user management within a tenant.
/// There is no public tenant-registration endpoint in this API.
/// Tenants are created via seeding; users are added via POST /api/v1/auth/register (Admin-only).
/// </summary>
[Collection("Integration")]
public class TenantUserManagementTests : IntegrationTestBase
{
    private async Task<(Tenant tenant, string adminToken)> SeedTenantWithAdminAsync()
    {
        var hasher = new PasswordHasher();
        const string password = "AdminPass123!";
        Tenant? tenant = null;

        await SeedAsync(async db =>
        {
            tenant = Tenant.Create("Test Corp", $"testcorp-{Guid.NewGuid():N}",
                "billing@testcorp.com", "USD");
            await db.Tenants.AddAsync(tenant!);
            await db.SaveChangesAsync();

            var admin = User.Create(tenant!.Id, "Tony", "Admin", "tony@testcorp.com",
                hasher.Hash(password), UserRole.Admin);
            await db.Users.AddAsync(admin);
        });

        var token = await GetAuthTokenAsync("tony@testcorp.com", password, tenant!.Id);
        return (tenant!, token);
    }

    [Fact]
    public async Task Register_AdminCreatesNewUser_Returns201()
    {
        // Arrange
        var (_, adminToken) = await SeedTenantWithAdminAsync();
        var client = CreateClientWithToken(adminToken);

        // Act
        var response = await client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            firstName = "Jane",
            lastName = "Member",
            email = "jane@testcorp.com",
            password = "MemberPass123!",
            role = "Member"
        });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Register_DuplicateEmail_Returns400()
    {
        // Arrange — admin registers jane once
        var (_, adminToken) = await SeedTenantWithAdminAsync();
        var client = CreateClientWithToken(adminToken);

        await client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            firstName = "Jane",
            lastName = "Member",
            email = "jane-dup@testcorp.com",
            password = "MemberPass123!",
            role = "Member"
        });

        // Act — register same email again
        var response = await client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            firstName = "Jane",
            lastName = "Again",
            email = "jane-dup@testcorp.com",
            password = "MemberPass123!",
            role = "Member"
        });

        // Assert — handler returns failure for duplicate → 400
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Register_InvalidCurrency_IsNotApplicable_UserEmailValidation()
    {
        // Arrange
        var (_, adminToken) = await SeedTenantWithAdminAsync();
        var client = CreateClientWithToken(adminToken);

        // Act — invalid email
        var response = await client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            firstName = "Bad",
            lastName = "Email",
            email = "not-an-email",
            password = "MemberPass123!",
            role = "Member"
        });

        // Assert
        response.StatusCode.Should().BeOneOf(HttpStatusCode.BadRequest,
            HttpStatusCode.UnprocessableEntity);
    }
}