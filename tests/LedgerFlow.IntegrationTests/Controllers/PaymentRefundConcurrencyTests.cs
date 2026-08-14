using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using LedgerFlow.IntegrationTests.Infrastructure;
using ledgerflowApi.Application.Features.Invoices.Commands.ProcessPayment;
using ledgerflowApi.Application.Features.Invoices.DTOs;
using ledgerflowApi.Domain.Entities;
using ledgerflowApi.Domain.Enums;
using ledgerflowApi.Domain.ValueObjects;
using ledgerflowApi.Infrastructure.Identity;
using Xunit;

namespace LedgerFlow.IntegrationTests.Controllers;

/// <summary>
/// Integration tests for the payment/refund concurrency fix.
///
/// Covers, end to end through real HTTP requests against ProcessPaymentCommand:
///  - A single refund can never exceed the original payment's amount.
///  - Two *sequential* refunds against the same payment are correctly tracked
///    against each other (the bug found during the audit — nothing previously
///    stopped this).
///  - Two *concurrent* refunds against the same payment: exactly one succeeds.
///  - Two *concurrent* standard payments against the same invoice: exactly one
///    succeeds, and the invoice's final PaidAmount/status reflect only the winner
///    (this protection already existed via Invoice.UpdatedAt as a concurrency
///    token — these tests are what actually proves it works end to end).
///  - Duplicate payment requests with the same ExternalReference remain idempotent
///    even when fired concurrently.
/// </summary>
[Collection("Integration")]
public class PaymentRefundConcurrencyTests : IntegrationTestBase
{
    private async Task<(Tenant tenant, User user, string token)> SeedTenantAndUserAsync()
    {
        var hasher = new PasswordHasher();
        const string password = "TestPass123!";
        Tenant? tenant = null;
        User? user = null;

        await SeedAsync(async db =>
        {
            var slug = $"corp-{Guid.NewGuid():N}";
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

    private async Task<Invoice> SeedIssuedInvoiceAsync(Tenant tenant, User user, decimal total)
    {
        Invoice invoice = null!;
        await SeedAsync(async db =>
        {
            invoice = Invoice.Create(tenant.Id, user.Id, $"INV-{Guid.NewGuid():N}"[..14], "Cust", "c@c.com", "USD");
            invoice.AddLineItem(new InvoiceLineItem("Item", Money.Of(total, "USD"), 1m, 0m, null));
            invoice.Issue(DateTime.UtcNow.AddDays(30));
            await db.Invoices.AddAsync(invoice);
        });
        return invoice;
    }

    // ── A single refund can never exceed the original payment ───────────────

    [Fact]
    public async Task Refund_ExceedingOriginalPaymentAmount_Returns400()
    {
        var (tenant, user, token) = await SeedTenantAndUserAsync();
        var invoice = await SeedIssuedInvoiceAsync(tenant, user, total: 100m);
        var client = CreateClientWithToken(token);

        var paymentResponse = await client.PostAsJsonAsync($"/api/v1/invoices/{invoice.Id}/payments", new
        {
            amount = 100m,
            currency = "USD",
            paymentMethod = "card",
            type = "Standard",
            externalReference = $"pi_{Guid.NewGuid():N}"
        });
        var payment = await paymentResponse.Content.ReadFromJsonAsync<PaymentResponse>();

        // Try to refund more than was ever paid.
        var refundResponse = await client.PostAsJsonAsync($"/api/v1/invoices/{invoice.Id}/payments", new
        {
            amount = 150m,
            currency = "USD",
            paymentMethod = "card",
            type = "Refund",
            refundedPaymentId = payment!.PaymentId
        });

        refundResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── Two sequential refunds together exceeding the original amount ───────

    [Fact]
    public async Task TwoSequentialRefunds_TogetherExceedingOriginalAmount_SecondIsRejected()
    {
        // This is the bug found during the audit: prior to the fix, nothing tracked how
        // much of a specific payment had already been refunded, so two $60 refunds against
        // a single $100 payment could both succeed (refunding $120 of a $100 payment).
        var (tenant, user, token) = await SeedTenantAndUserAsync();
        var invoice = await SeedIssuedInvoiceAsync(tenant, user, total: 100m);
        var client = CreateClientWithToken(token);

        var paymentResponse = await client.PostAsJsonAsync($"/api/v1/invoices/{invoice.Id}/payments", new
        {
            amount = 100m,
            currency = "USD",
            paymentMethod = "card",
            type = "Standard",
            externalReference = $"pi_{Guid.NewGuid():N}"
        });
        var payment = await paymentResponse.Content.ReadFromJsonAsync<PaymentResponse>();

        var firstRefund = await client.PostAsJsonAsync($"/api/v1/invoices/{invoice.Id}/payments", new
        {
            amount = 60m,
            currency = "USD",
            paymentMethod = "card",
            type = "Refund",
            refundedPaymentId = payment!.PaymentId
        });

        var secondRefund = await client.PostAsJsonAsync($"/api/v1/invoices/{invoice.Id}/payments", new
        {
            amount = 60m,
            currency = "USD",
            paymentMethod = "card",
            type = "Refund",
            refundedPaymentId = payment.PaymentId
        });

        firstRefund.StatusCode.Should().Be(HttpStatusCode.OK);
        secondRefund.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task TwoSequentialRefunds_TogetherExactlyEqualToOriginalAmount_BothSucceed()
    {
        var (tenant, user, token) = await SeedTenantAndUserAsync();
        var invoice = await SeedIssuedInvoiceAsync(tenant, user, total: 100m);
        var client = CreateClientWithToken(token);

        var paymentResponse = await client.PostAsJsonAsync($"/api/v1/invoices/{invoice.Id}/payments", new
        {
            amount = 100m,
            currency = "USD",
            paymentMethod = "card",
            type = "Standard",
            externalReference = $"pi_{Guid.NewGuid():N}"
        });
        var payment = await paymentResponse.Content.ReadFromJsonAsync<PaymentResponse>();

        var firstRefund = await client.PostAsJsonAsync($"/api/v1/invoices/{invoice.Id}/payments", new
        {
            amount = 40m,
            currency = "USD",
            paymentMethod = "card",
            type = "Refund",
            refundedPaymentId = payment!.PaymentId
        });

        var secondRefund = await client.PostAsJsonAsync($"/api/v1/invoices/{invoice.Id}/payments", new
        {
            amount = 60m,
            currency = "USD",
            paymentMethod = "card",
            type = "Refund",
            refundedPaymentId = payment.PaymentId
        });

        firstRefund.StatusCode.Should().Be(HttpStatusCode.OK);
        secondRefund.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ── Two concurrent refunds against the same original payment ────────────

    [Fact]
    public async Task TwoConcurrentRefunds_AgainstSamePayment_ExactlyOneSucceeds()
    {
        var (tenant, user, token) = await SeedTenantAndUserAsync();
        var invoice = await SeedIssuedInvoiceAsync(tenant, user, total: 100m);
        var client = CreateClientWithToken(token);

        var paymentResponse = await client.PostAsJsonAsync($"/api/v1/invoices/{invoice.Id}/payments", new
        {
            amount = 100m,
            currency = "USD",
            paymentMethod = "card",
            type = "Standard",
            externalReference = $"pi_{Guid.NewGuid():N}"
        });
        var payment = await paymentResponse.Content.ReadFromJsonAsync<PaymentResponse>();

        // Two refunds of 70 each, fired at the same time, against a 100 payment.
        // Together they would refund 140 — only one can be allowed to win.
        var refundTask1 = client.PostAsJsonAsync($"/api/v1/invoices/{invoice.Id}/payments", new
        {
            amount = 70m,
            currency = "USD",
            paymentMethod = "card",
            type = "Refund",
            refundedPaymentId = payment!.PaymentId
        });
        var refundTask2 = client.PostAsJsonAsync($"/api/v1/invoices/{invoice.Id}/payments", new
        {
            amount = 70m,
            currency = "USD",
            paymentMethod = "card",
            type = "Refund",
            refundedPaymentId = payment.PaymentId
        });

        var results = await Task.WhenAll(refundTask1, refundTask2);

        var successCount = results.Count(r => r.StatusCode == HttpStatusCode.OK);
        var failureCount = results.Count(r => r.StatusCode == HttpStatusCode.BadRequest);

        // Exactly one of the two concurrent refunds should have won. If the fix weren't in
        // place, both could succeed and 140 would be refunded against a 100 payment.
        successCount.Should().Be(1);
        failureCount.Should().Be(1);

        // Confirm the invoice ended up in a consistent state: PaidAmount reflects exactly
        // one 70 refund (100 - 70 = 30 outstanding... expressed here via the invoice's
        // paidAmount, which should be 30 after exactly one refund of 70 from a paid 100).
        var invoiceResponse = await client.GetAsync($"/api/v1/invoices/{invoice.Id}");
        var invoiceBody = await invoiceResponse.Content.ReadFromJsonAsync<InvoiceResponse>();
        invoiceBody!.PaidAmount.Should().Be(30m);
    }

    // ── Two concurrent standard payments against the same invoice ───────────
    // (Protected by the pre-existing Invoice.UpdatedAt concurrency token — this test
    // is what actually proves that protection works end to end, and that the conflict
    // is now surfaced as a clean 400 instead of an unhandled 500.)

    [Fact]
    public async Task TwoConcurrentPayments_AgainstSameInvoice_ExactlyOneSucceeds()
    {
        var (tenant, user, token) = await SeedTenantAndUserAsync();
        var invoice = await SeedIssuedInvoiceAsync(tenant, user, total: 100m);
        var client = CreateClientWithToken(token);

        var paymentTask1 = client.PostAsJsonAsync($"/api/v1/invoices/{invoice.Id}/payments", new
        {
            amount = 60m,
            currency = "USD",
            paymentMethod = "card",
            type = "Standard",
            externalReference = $"pi_{Guid.NewGuid():N}"
        });
        var paymentTask2 = client.PostAsJsonAsync($"/api/v1/invoices/{invoice.Id}/payments", new
        {
            amount = 60m,
            currency = "USD",
            paymentMethod = "card",
            type = "Standard",
            externalReference = $"pi_{Guid.NewGuid():N}"
        });

        var results = await Task.WhenAll(paymentTask1, paymentTask2);

        // The two payments (60 + 60 = 120) together exceed the 100 invoice total, so
        // allowing both to succeed would mean the invoice ends up overpaid — that must
        // never happen. Depending on timing, the second request is rejected either by
        // the domain's own overpayment guard (if it reads the invoice after the first
        // commit) or by the DbUpdateConcurrencyException catch (if both read the invoice
        // concurrently before either commits) — either way, exactly one must win.
        results.Should().OnlyContain(r =>
            r.StatusCode == HttpStatusCode.OK || r.StatusCode == HttpStatusCode.BadRequest);

        var successCount = results.Count(r => r.StatusCode == HttpStatusCode.OK);
        successCount.Should().Be(1, "60 + 60 exceeds the 100 invoice total, so exactly one payment must win — never both, never neither");

        var invoiceResponse = await client.GetAsync($"/api/v1/invoices/{invoice.Id}");
        var invoiceBody = await invoiceResponse.Content.ReadFromJsonAsync<InvoiceResponse>();
        invoiceBody!.PaidAmount.Should().Be(60m);
        invoiceBody.PaidAmount.Should().BeLessThanOrEqualTo(invoiceBody.TotalAmount);
    }

    // ── Duplicate ExternalReference fired concurrently stays idempotent ─────

    [Fact]
    public async Task DuplicatePaymentRequests_SameExternalReference_FiredConcurrently_OnlyOnePaymentIsCreated()
    {
        var (tenant, user, token) = await SeedTenantAndUserAsync();
        var invoice = await SeedIssuedInvoiceAsync(tenant, user, total: 100m);
        var client = CreateClientWithToken(token);
        var externalRef = $"pi_{Guid.NewGuid():N}";

        var task1 = client.PostAsJsonAsync($"/api/v1/invoices/{invoice.Id}/payments", new
        {
            amount = 50m,
            currency = "USD",
            paymentMethod = "card",
            type = "Standard",
            externalReference = externalRef
        });
        var task2 = client.PostAsJsonAsync($"/api/v1/invoices/{invoice.Id}/payments", new
        {
            amount = 50m,
            currency = "USD",
            paymentMethod = "card",
            type = "Standard",
            externalReference = externalRef
        });

        var results = await Task.WhenAll(task1, task2);

        // Both requests should come back successfully (idempotent replay returns the
        // existing payment) — never a duplicate charge, never a 500.
        results.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.OK);

        var invoiceResponse = await client.GetAsync($"/api/v1/invoices/{invoice.Id}");
        var invoiceBody = await invoiceResponse.Content.ReadFromJsonAsync<InvoiceResponse>();

        // Only ONE 50 payment should ever have been applied, not 100.
        invoiceBody!.PaidAmount.Should().Be(50m);
    }
}
