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
/// Integration tests for the Invoices API endpoints.
///
/// Route prefix: /api/v1/invoices  (BaseApiController uses "api/v1/[controller]")
/// Payment sub-route: POST /api/v1/invoices/{id}/payments  (plural)
///
/// Tenant.Create signature: (name, slug, billingEmail, defaultCurrency)
/// </summary>
[Collection("Integration")]
public class InvoicesControllerTests : IntegrationTestBase
{
    // ── Seed helpers ──────────────────────────────────────────────────────────

    private async Task<(Tenant tenant, User user, string token)> SeedTenantAndUserAsync(
        UserRole role = UserRole.Admin)
    {
        var hasher = new PasswordHasher();
        const string plainPassword = "TestPassword123!";

        Tenant? tenant = null;
        User? user = null;

        await SeedAsync(async db =>
        {
            // Correct Tenant.Create: (name, slug, billingEmail, defaultCurrency)
            tenant = Tenant.Create("Acme Corp", $"acme-{Guid.NewGuid():N}",
                "billing@acme.com", "USD");
            await db.Tenants.AddAsync(tenant!);
            await db.SaveChangesAsync();

            user = User.Create(tenant!.Id, "Alice", "Smith", "alice@acme.com",
                hasher.Hash(plainPassword), role);
            await db.Users.AddAsync(user!);
        });

        var token = await GetAuthTokenAsync("alice@acme.com", plainPassword, tenant!.Id);
        return (tenant!, user!, token);
    }

    // ── POST /api/v1/invoices ─────────────────────────────────────────────────


    [Fact]
    public async Task CreateInvoice_Unauthenticated_Returns401()
    {
        // Act — no auth header
        var response = await Client.PostAsJsonAsync("/api/v1/invoices", new
        {
            customerName = "Bob",
            customerEmail = "bob@customer.com",
            currency = "USD",
            lineItems = new[] { new { description = "Item", unitPrice = 100m, quantity = 1m } }
        });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CreateInvoice_ViewerRole_Returns403()
    {
        // Arrange — Viewer role is below the RequireMember policy
        var (_, _, token) = await SeedTenantAndUserAsync(UserRole.Viewer);
        var client = CreateClientWithToken(token);

        // Act
        var response = await client.PostAsJsonAsync("/api/v1/invoices", new
        {
            customerName = "Bob",
            customerEmail = "bob@customer.com",
            currency = "USD",
            lineItems = new[] { new { description = "Item", unitPrice = 100m, quantity = 1m } }
        });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task CreateInvoice_MissingRequiredFields_Returns400()
    {
        // Arrange
        var (_, _, token) = await SeedTenantAndUserAsync();
        var client = CreateClientWithToken(token);

        // Act — empty body; validator / model binding will reject
        var response = await client.PostAsJsonAsync("/api/v1/invoices", new { });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    // ── GET /api/v1/invoices/{id} ─────────────────────────────────────────────

    [Fact]
    public async Task GetInvoice_ExistingInvoice_Returns200WithInvoiceData()
    {
        // Arrange
        var (tenant, user, token) = await SeedTenantAndUserAsync();
        Invoice? invoice = null;

        await SeedAsync(async db =>
        {
            invoice = Invoice.Create(tenant.Id, user.Id, "INV-2024-000001",
                "Test Customer", "test@customer.com", "USD");
            invoice.AddLineItem(new InvoiceLineItem(
                "Service", Money.Of(100m, "USD"), 1m, 0m, null));
            await db.Invoices.AddAsync(invoice);
        });

        var client = CreateClientWithToken(token);

        // Act
        var response = await client.GetAsync($"/api/v1/invoices/{invoice!.Id}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<InvoiceResponseDto>();
        body!.InvoiceNumber.Should().Be("INV-2024-000001");
    }

    [Fact]
    public async Task GetInvoice_NotFound_Returns404()
    {
        // Arrange
        var (_, _, token) = await SeedTenantAndUserAsync();
        var client = CreateClientWithToken(token);

        // Act
        var response = await client.GetAsync($"/api/v1/invoices/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetInvoice_BelongingToOtherTenant_Returns404()
    {
        // Arrange — two tenants; Tenant B cannot see Tenant A's invoice
        var (tenantA, userA, _) = await SeedTenantAndUserAsync();
        var (_, _, tokenB) = await SeedTenantAndUserAsync();

        Invoice? invoiceA = null;
        await SeedAsync(async db =>
        {
            invoiceA = Invoice.Create(tenantA.Id, userA.Id, "INV-A-001",
                "Customer", "c@c.com", "USD");
            invoiceA.AddLineItem(new InvoiceLineItem("Item", Money.Of(100m, "USD"), 1m, 0m, null));
            await db.Invoices.AddAsync(invoiceA);
        });

        var clientB = CreateClientWithToken(tokenB);

        // Act — Tenant B tries to fetch Tenant A's invoice
        var response = await clientB.GetAsync($"/api/v1/invoices/{invoiceA!.Id}");

        // Assert — returns 404 (not 403) to avoid leaking invoice existence
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── GET /api/v1/invoices ──────────────────────────────────────────────────

    [Fact]
    public async Task ListInvoices_ReturnsOnlyCurrentTenantInvoices()
    {
        // Arrange — two tenants, each with their own invoice
        var (tenantA, userA, tokenA) = await SeedTenantAndUserAsync();
        var (tenantB, userB, _) = await SeedTenantAndUserAsync();

        await SeedAsync(async db =>
        {
            var invA = Invoice.Create(tenantA.Id, userA.Id, "INV-A-001", "CustA", "a@a.com", "USD");
            invA.AddLineItem(new InvoiceLineItem("Item", Money.Of(100m, "USD"), 1m, 0m, null));
            var invB = Invoice.Create(tenantB.Id, userB.Id, "INV-B-001", "CustB", "b@b.com", "USD");
            invB.AddLineItem(new InvoiceLineItem("Item", Money.Of(100m, "USD"), 1m, 0m, null));
            await db.Invoices.AddRangeAsync(invA, invB);
        });

        var clientA = CreateClientWithToken(tokenA);

        // Act
        var response = await clientA.GetAsync("/api/v1/invoices");

        // Assert — Tenant A only sees their own invoice
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<PaginatedResult<InvoiceResponseDto>>();
        body!.Items.Should().HaveCount(1);
        body.Items.First().InvoiceNumber.Should().Be("INV-A-001");
    }

    // ── POST /api/v1/invoices/{id}/issue ──────────────────────────────────────

    [Fact]
    public async Task IssueInvoice_ValidDraftInvoice_Returns200WithIssuedStatus()
    {
        // Arrange
        var (tenant, user, token) = await SeedTenantAndUserAsync();
        Invoice? invoice = null;

        await SeedAsync(async db =>
        {
            invoice = Invoice.Create(tenant.Id, user.Id, "INV-2024-000001",
                "Customer", "cust@test.com", "USD");
            invoice.AddLineItem(new InvoiceLineItem("Service", Money.Of(200m, "USD"), 1m, 0m, null));
            await db.Invoices.AddAsync(invoice);
        });

        var client = CreateClientWithToken(token);

        // Act
        var response = await client.PostAsJsonAsync($"/api/v1/invoices/{invoice!.Id}/issue", new
        {
            dueDate = DateTime.UtcNow.AddDays(30)
        });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<InvoiceResponseDto>();
        body!.Status.Should().Be("Issued");
        body.IssuedAt.Should().NotBeNull();
        body.DueDate.Should().NotBeNull();
    }

    [Fact]
    public async Task IssueInvoice_AlreadyIssued_Returns400()
    {
        // Arrange
        var (tenant, user, token) = await SeedTenantAndUserAsync();
        Invoice? invoice = null;

        await SeedAsync(async db =>
        {
            invoice = Invoice.Create(tenant.Id, user.Id, "INV-001", "Customer", "c@c.com", "USD");
            invoice.AddLineItem(new InvoiceLineItem("Item", Money.Of(100m, "USD"), 1m, 0m, null));
            invoice.Issue(DateTime.UtcNow.AddDays(30));
            await db.Invoices.AddAsync(invoice);
        });

        var client = CreateClientWithToken(token);

        // Act — try to issue an already-issued invoice
        var response = await client.PostAsJsonAsync($"/api/v1/invoices/{invoice!.Id}/issue", new
        {
            dueDate = DateTime.UtcNow.AddDays(30)
        });

        // Assert — handler returns Result.Failure → controller returns 400
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── POST /api/v1/invoices/{id}/void ───────────────────────────────────────

    [Fact]
    public async Task VoidInvoice_ValidDraftInvoice_Returns200WithVoidedStatus()
    {
        // Arrange
        var (tenant, user, token) = await SeedTenantAndUserAsync();
        Invoice? invoice = null;

        await SeedAsync(async db =>
        {
            invoice = Invoice.Create(tenant.Id, user.Id, "INV-001", "Cust", "c@c.com", "USD");
            invoice.AddLineItem(new InvoiceLineItem("Item", Money.Of(100m, "USD"), 1m, 0m, null));
            await db.Invoices.AddAsync(invoice);
        });

        var client = CreateClientWithToken(token);

        // Act
        var response = await client.PostAsJsonAsync($"/api/v1/invoices/{invoice!.Id}/void", new
        {
            reason = "Created in error"
        });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<InvoiceResponseDto>();
        body!.Status.Should().Be("Voided");
    }

    [Fact]
    public async Task VoidInvoice_WithoutReason_Returns400()
    {
        // Arrange
        var (tenant, user, token) = await SeedTenantAndUserAsync();
        Invoice? invoice = null;

        await SeedAsync(async db =>
        {
            invoice = Invoice.Create(tenant.Id, user.Id, "INV-001", "Cust", "c@c.com", "USD");
            invoice.AddLineItem(new InvoiceLineItem("Item", Money.Of(100m, "USD"), 1m, 0m, null));
            await db.Invoices.AddAsync(invoice);
        });

        var client = CreateClientWithToken(token);

        // Act — empty reason; domain throws, handler returns failure → 400
        var response = await client.PostAsJsonAsync($"/api/v1/invoices/{invoice!.Id}/void", new
        {
            reason = ""
        });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    // ── POST /api/v1/invoices/{id}/payments ───────────────────────────────────
    // Note: the controller route is "payments" (plural), not "payment"

    [Fact]
    public async Task ProcessPayment_FullPayment_Returns200WithPaidStatus()
    {
        // Arrange
        var (tenant, user, token) = await SeedTenantAndUserAsync();
        Invoice? invoice = null;

        await SeedAsync(async db =>
        {
            invoice = Invoice.Create(tenant.Id, user.Id, "INV-001", "Cust", "c@c.com", "USD");
            invoice.AddLineItem(new InvoiceLineItem("Item", Money.Of(100m, "USD"), 1m, 0m, null));
            invoice.Issue(DateTime.UtcNow.AddDays(30));
            await db.Invoices.AddAsync(invoice);
        });

        var client = CreateClientWithToken(token);

        // Act
        var response = await client.PostAsJsonAsync($"/api/v1/invoices/{invoice!.Id}/payments", new
        {
            amount = 100m,
            currency = "USD",
            paymentMethod = "card",
            type = "Standard",
            externalReference = $"pi_test_{Guid.NewGuid():N}"
        });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<PaymentResponseDto>();
        body!.Invoice.Status.Should().Be("Paid");
        body.Invoice.OutstandingAmount.Should().Be(0m);
    }

    [Fact]
    public async Task ProcessPayment_DuplicateExternalReference_Returns200Idempotently()
    {
        // Arrange
        var (tenant, user, token) = await SeedTenantAndUserAsync();
        Invoice? invoice = null;
        const string extRef = "pi_idempotent_test_001";

        await SeedAsync(async db =>
        {
            invoice = Invoice.Create(tenant.Id, user.Id, "INV-001", "Cust", "c@c.com", "USD");
            invoice.AddLineItem(new InvoiceLineItem("Item", Money.Of(100m, "USD"), 1m, 0m, null));
            invoice.Issue(DateTime.UtcNow.AddDays(30));
            await db.Invoices.AddAsync(invoice);
        });

        var client = CreateClientWithToken(token);
        var payload = new
        {
            amount = 100m,
            currency = "USD",
            paymentMethod = "card",
            type = "Standard",
            externalReference = extRef
        };

        // Act — call twice with same external reference
        var response1 = await client.PostAsJsonAsync($"/api/v1/invoices/{invoice!.Id}/payments", payload);
        var response2 = await client.PostAsJsonAsync($"/api/v1/invoices/{invoice.Id}/payments", payload);

        // Assert — both succeed; payment recorded only once
        response1.StatusCode.Should().Be(HttpStatusCode.OK);
        response2.StatusCode.Should().Be(HttpStatusCode.OK);

        var body2 = await response2.Content.ReadFromJsonAsync<PaymentResponseDto>();
        body2!.Invoice.PaidAmount.Should().Be(100m); // not 200
    }

    // ── GET /health ───────────────────────────────────────────────────────────

    [Fact]
    public async Task HealthEndpoint_ReturnsHealthy()
    {
        var response = await Client.GetAsync("/health");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ── Response DTOs ─────────────────────────────────────────────────────────

    private sealed record InvoiceResponseDto(
        Guid Id,
        string InvoiceNumber,
        string Status,
        string CustomerEmail,
        decimal TotalAmount,
        decimal PaidAmount,
        decimal OutstandingAmount,
        DateTime? IssuedAt,
        DateTime? DueDate,
        DateTime? PaidAt);

    private sealed record PaymentResponseDto(
        Guid PaymentId,
        string Status,
        InvoiceSnapshotDto Invoice);

    private sealed record InvoiceSnapshotDto(
        Guid Id,
        string InvoiceNumber,
        string Status,
        decimal TotalAmount,
        decimal PaidAmount,
        decimal OutstandingAmount);

    private sealed record PaginatedResult<T>(
        IList<T> Items,
        int TotalCount,
        int PageNumber,
        int PageSize);
}