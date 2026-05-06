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
/// Tests the full HTTP→handler→domain→DB round trip using the in-memory database.
/// Each test seeds its own data and gets a fresh JWT, so tests are fully isolated.
/// </summary>
[Collection("Integration")]
public class InvoicesControllerTests : IntegrationTestBase
{
    // ── Seeding helpers ───────────────────────────────────────────────────────

    private async Task<(Tenant tenant, User user, string token)> SeedTenantAndUserAsync(
        UserRole role = UserRole.Admin)
    {
        var hasher = new PasswordHasher();
        const string plainPassword = "TestPassword123!";

        Tenant? tenant = null;
        User? user = null;

        await SeedAsync(async db =>
        {
            tenant = Tenant.Create("Acme Corp", $"acme-{Guid.NewGuid():N}", "USD");
            await db.Tenants.AddAsync(tenant!);
            await db.SaveChangesAsync();

            user = User.Create(tenant!.Id, "Alice", "Smith", "alice@acme.com",
                hasher.Hash(plainPassword), role);
            await db.Users.AddAsync(user!);
        });

        var token = await GetAuthTokenAsync("alice@acme.com", plainPassword, tenant!.Id);
        return (tenant!, user!, token);
    }

    // ── POST /api/invoices ────────────────────────────────────────────────────

    [Fact]
    public async Task CreateInvoice_ValidRequest_Returns201WithInvoiceData()
    {
        // Arrange
        var (tenant, _, token) = await SeedTenantAndUserAsync();
        var client = CreateClientWithToken(token);

        // Act
        var response = await client.PostAsJsonAsync("/api/invoices", new
        {
            tenantId = tenant.Id,
            customerName = "Bob Customer",
            customerEmail = "bob@customer.com",
            currency = "USD",
            taxRatePercentage = 20,
            discountPercentage = 0,
            lineItems = new[]
            {
                new { description = "Consulting", unitPrice = 500m, quantity = 2m, discountPercentage = 0m }
            }
        });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<InvoiceResponseDto>();
        body.Should().NotBeNull();
        body!.Status.Should().Be("Draft");
        body.InvoiceNumber.Should().StartWith("INV-");
        body.TotalAmount.Should().Be(1200m); // (500*2) * 1.20 tax
        body.CustomerEmail.Should().Be("bob@customer.com");
    }

    [Fact]
    public async Task CreateInvoice_Unauthenticated_Returns401()
    {
        // Act — no auth header
        var response = await Client.PostAsJsonAsync("/api/invoices", new
        {
            customerName = "Bob",
            customerEmail = "bob@customer.com",
            currency = "USD",
            lineItems = new[] { new { description = "Item", unitPrice = 100m, quantity = 1m } }
        });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CreateInvoice_ViewerRole_Returns403()
    {
        // Arrange — Viewer role cannot create invoices
        var (tenant, _, token) = await SeedTenantAndUserAsync(UserRole.Viewer);
        var client = CreateClientWithToken(token);

        // Act
        var response = await client.PostAsJsonAsync("/api/invoices", new
        {
            tenantId = tenant.Id,
            customerName = "Bob",
            customerEmail = "bob@customer.com",
            currency = "USD",
            lineItems = new[] { new { description = "Item", unitPrice = 100m, quantity = 1m } }
        });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task CreateInvoice_MissingRequiredFields_Returns400()
    {
        // Arrange
        var (_, _, token) = await SeedTenantAndUserAsync();
        var client = CreateClientWithToken(token);

        // Act — missing customerName, customerEmail, currency, lineItems
        var response = await client.PostAsJsonAsync("/api/invoices", new { });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── GET /api/invoices/{id} ────────────────────────────────────────────────

    [Fact]
    public async Task GetInvoice_ExistingInvoice_Returns200WithInvoiceData()
    {
        // Arrange — seed directly into DB
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
        var response = await client.GetAsync($"/api/invoices/{invoice!.Id}");

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
        var response = await client.GetAsync($"/api/invoices/{Guid.NewGuid()}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetInvoice_BelongingToOtherTenant_Returns404()
    {
        // Arrange — two separate tenants; tenant B's user cannot see tenant A's invoice
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

        // Act — Tenant B's user tries to fetch Tenant A's invoice
        var response = await clientB.GetAsync($"/api/invoices/{invoiceA!.Id}");

        // Assert — returns 404, not 403, so we don't leak the invoice exists
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── GET /api/invoices ─────────────────────────────────────────────────────

    [Fact]
    public async Task ListInvoices_ReturnsOnlyCurrentTenantInvoices()
    {
        // Arrange — two tenants, each with their own invoices
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
        var response = await clientA.GetAsync("/api/invoices");

        // Assert — Tenant A only sees their own invoices
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<PaginatedResult<InvoiceResponseDto>>();
        body!.Items.Should().HaveCount(1);
        body.Items.First().InvoiceNumber.Should().Be("INV-A-001");
    }

    // ── POST /api/invoices/{id}/issue ─────────────────────────────────────────

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
        var response = await client.PostAsJsonAsync($"/api/invoices/{invoice!.Id}/issue", new
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
    public async Task IssueInvoice_AlreadyIssued_Returns409()
    {
        // Arrange
        var (tenant, user, token) = await SeedTenantAndUserAsync();
        Invoice? invoice = null;

        await SeedAsync(async db =>
        {
            invoice = Invoice.Create(tenant.Id, user.Id, "INV-001",
                "Customer", "c@c.com", "USD");
            invoice.AddLineItem(new InvoiceLineItem("Item", Money.Of(100m, "USD"), 1m, 0m, null));
            invoice.Issue(DateTime.UtcNow.AddDays(30));
            await db.Invoices.AddAsync(invoice);
        });

        var client = CreateClientWithToken(token);

        // Act — try to issue an already-issued invoice
        var response = await client.PostAsJsonAsync($"/api/invoices/{invoice!.Id}/issue", new
        {
            dueDate = DateTime.UtcNow.AddDays(30)
        });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    // ── POST /api/invoices/{id}/void ──────────────────────────────────────────

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
        var response = await client.PostAsJsonAsync($"/api/invoices/{invoice!.Id}/void", new
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
            await db.Invoices.AddAsync(invoice);
        });

        var client = CreateClientWithToken(token);

        // Act — missing reason
        var response = await client.PostAsJsonAsync($"/api/invoices/{invoice!.Id}/void", new
        {
            reason = ""
        });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── POST /api/invoices/{id}/payment ───────────────────────────────────────

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
        var response = await client.PostAsJsonAsync($"/api/invoices/{invoice!.Id}/payment", new
        {
            tenantId = tenant.Id,
            invoiceId = invoice.Id,
            amount = 100m,
            currency = "USD",
            paymentMethod = "card",
            paymentType = "Standard",
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
        // This tests the idempotency guard — webhook replays must not double-charge
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
            tenantId = tenant.Id,
            invoiceId = invoice!.Id,
            amount = 100m,
            currency = "USD",
            paymentMethod = "card",
            paymentType = "Standard",
            externalReference = extRef
        };

        // Act — call twice with same external reference
        var response1 = await client.PostAsJsonAsync($"/api/invoices/{invoice.Id}/payment", payload);
        var response2 = await client.PostAsJsonAsync($"/api/invoices/{invoice.Id}/payment", payload);

        // Assert — both succeed and invoice is paid exactly once
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
