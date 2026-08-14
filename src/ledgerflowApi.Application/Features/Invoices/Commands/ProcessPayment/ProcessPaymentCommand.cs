using ledgerflowApi.Application.Common.Interfaces;
using ledgerflowApi.Application.Common.Models;
using ledgerflowApi.Domain.Entities;
using ledgerflowApi.Domain.Enums;
using ledgerflowApi.Domain.Exceptions;
using ledgerflowApi.Domain.Interfaces;
using ledgerflowApi.Domain.ValueObjects;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ledgerflowApi.Application.Features.Invoices.Commands.ProcessPayment;

// ── DTOs returned ─────────────────────────────────────────────────────────────

/// <summary>
/// Response returned after a payment is processed.
/// Includes both the payment record and the updated invoice state
/// so the client can refresh the UI in one round-trip.
/// </summary>
public sealed class PaymentResponse
{
    public Guid PaymentId { get; init; }
    public string Status { get; init; } = string.Empty;
    public string Type { get; init; } = string.Empty;
    public decimal Amount { get; init; }
    public string Currency { get; init; } = string.Empty;
    public string PaymentMethod { get; init; } = string.Empty;
    public string? ExternalReference { get; init; }
    public DateTime? CompletedAt { get; init; }

    // Updated invoice snapshot — avoids a second GET call after payment
    public UpdatedInvoiceSnapshot Invoice { get; init; } = null!;
}

public sealed class UpdatedInvoiceSnapshot
{
    public Guid Id { get; init; }
    public string InvoiceNumber { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public decimal TotalAmount { get; init; }
    public decimal PaidAmount { get; init; }
    public decimal OutstandingAmount { get; init; }
    public string Currency { get; init; } = string.Empty;
    public DateTime? PaidAt { get; init; }
}

// ── Command ───────────────────────────────────────────────────────────────────

/// <summary>
/// Records a completed payment or refund against an invoice.
///
/// Design note: this command assumes the payment has ALREADY been confirmed by the
/// payment processor (Stripe, PayPal, etc.). The typical flow is:
///   1. Client calls the processor's SDK to capture the payment.
///   2. Processor webhook fires → application calls ProcessPaymentCommand.
///   3. This command records the payment and updates the invoice in one transaction.
///
/// For refunds: set Type = "Refund" and provide RefundedPaymentId.
/// The original payment record is never mutated — a new Payment row is created.
/// </summary>
public sealed record ProcessPaymentCommand(
    Guid TenantId,
    Guid InvoiceId,
    decimal Amount,
    string Currency,
    string PaymentMethod,
    string PaymentType,              // "Standard" | "Refund"
    string? ExternalReference,
    Guid? RefundedPaymentId,
    Guid? InitiatedByUserId,
    string? InitiatedByUserName,
    string? Notes
) : IRequest<Result<PaymentResponse>>;

// ── Validator ─────────────────────────────────────────────────────────────────

public sealed class ProcessPaymentCommandValidator : AbstractValidator<ProcessPaymentCommand>
{
    private static readonly HashSet<string> SupportedCurrencies = new(StringComparer.OrdinalIgnoreCase)
    {
        "USD", "EUR", "GBP", "CAD", "AUD", "JPY", "CHF", "SGD", "HKD", "NOK",
        "SEK", "DKK", "NZD", "ZAR", "INR", "BRL", "MXN", "AED", "SAR", "PLN"
    };

    public ProcessPaymentCommandValidator()
    {
        RuleFor(x => x.TenantId).NotEmpty();
        RuleFor(x => x.InvoiceId).NotEmpty();

        RuleFor(x => x.Amount)
            .GreaterThan(0m).WithMessage("Payment amount must be greater than zero.")
            .LessThanOrEqualTo(10_000_000).WithMessage("Payment amount cannot exceed 10,000,000.");

        RuleFor(x => x.Currency)
            .NotEmpty().WithMessage("Currency is required.")
            .Length(3).WithMessage("Currency must be a 3-character ISO 4217 code.")
            .Must(c => SupportedCurrencies.Contains(c))
            .WithMessage(x => $"'{x.Currency}' is not a supported currency.");

        RuleFor(x => x.PaymentMethod)
            .NotEmpty().WithMessage("Payment method is required.")
            .MaximumLength(50);

        RuleFor(x => x.PaymentType)
            .Must(t => t is "Standard" or "Refund")
            .WithMessage("PaymentType must be 'Standard' or 'Refund'.");

        // Refund-specific rules
        When(x => x.PaymentType == "Refund", () =>
        {
            RuleFor(x => x.RefundedPaymentId)
                .NotEmpty()
                .WithMessage("RefundedPaymentId is required for refund payments.");
        });

        RuleFor(x => x.ExternalReference)
            .MaximumLength(200)
            .When(x => x.ExternalReference is not null);

        RuleFor(x => x.Notes)
            .MaximumLength(1000)
            .When(x => x.Notes is not null);
    }
}

// ── Handler ───────────────────────────────────────────────────────────────────

/// <summary>
/// Full payment processing flow:
///   1. Load the invoice and validate it can accept payment.
///   2. For refunds: load and validate the original payment.
///   3. Create the Payment aggregate (Standard or Refund).
///   4. Mark the Payment as Completed immediately (gateway already confirmed it).
///   5. Apply to invoice: RecordPayment() or ReversePayment() on the aggregate.
///   6. Persist Payment + updated Invoice + AuditLog in one transaction.
///   7. Return PaymentResponse with the updated invoice snapshot.
///
/// Transaction boundary:
/// All three writes (Payment insert, Invoice update, AuditLog insert) share one
/// database transaction. If any fails, all three roll back — no partial state.
/// </summary>
public sealed class ProcessPaymentCommandHandler
    : IRequestHandler<ProcessPaymentCommand, Result<PaymentResponse>>
{
    private readonly IInvoiceRepository _invoiceRepository;
    private readonly IPaymentRepository _paymentRepository;
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<ProcessPaymentCommandHandler> _logger;

    public ProcessPaymentCommandHandler(
        IInvoiceRepository invoiceRepository,
        IPaymentRepository paymentRepository,
        IAuditLogRepository auditLogRepository,
        IUnitOfWork unitOfWork,
        ILogger<ProcessPaymentCommandHandler> logger)
    {
        _invoiceRepository = invoiceRepository;
        _paymentRepository = paymentRepository;
        _auditLogRepository = auditLogRepository;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result<PaymentResponse>> Handle(
        ProcessPaymentCommand request,
        CancellationToken cancellationToken)
    {
        // ── Step 1: Idempotency check ─────────────────────────────────────────
        // If this ExternalReference was already processed (e.g. webhook replay),
        // return success without double-processing. Never process a payment twice.
        if (!string.IsNullOrWhiteSpace(request.ExternalReference))
        {
            var existing = await _paymentRepository.GetByExternalReferenceAsync(
                request.ExternalReference, cancellationToken);

            if (existing is not null)
            {
                _logger.LogWarning(
                    "Duplicate payment processing attempt for ExternalReference {Ref}. Returning existing payment {PaymentId}.",
                    request.ExternalReference, existing.Id);

                var existingInvoice = await _invoiceRepository.GetByIdAsync(request.InvoiceId, cancellationToken);
                return Result<PaymentResponse>.Success(BuildResponse(existing, existingInvoice!));
            }
        }

        // ── Step 2: Load and validate the invoice ─────────────────────────────
        var invoice = await _invoiceRepository.GetByIdAsync(request.InvoiceId, cancellationToken);

        if (invoice is null || invoice.TenantId != request.TenantId)
            throw new NotFoundException(nameof(Invoice), request.InvoiceId);

        var paymentAmount = Money.Of(request.Amount, request.Currency);
        var invoiceStatusBefore = invoice.Status.Value;
        var paidAmountBefore = invoice.PaidAmount.Amount;

        // ── Step 2.5: Re-check idempotency immediately before mutating anything ─
        // Step 1's check and this point are separated by an awaited DB round trip
        // (loading the invoice above) — long enough for a concurrent request with
        // the same ExternalReference to have fully committed in between. Re-checking
        // here, before any domain mutation or write, closes that window: if another
        // request already won, we return its committed payment instead of creating
        // a duplicate. Checked here (not later) so `invoice` is still exactly what
        // was just read from the database — no in-memory mutation to worry about
        // when building the response from it.
        // This is what actually fixes the concurrent-duplicate-request case; the
        // DbUpdateConcurrencyException handling in Step 7 is the backstop for the
        // much narrower window that remains after this check.
        if (request.PaymentType != "Refund" && !string.IsNullOrWhiteSpace(request.ExternalReference))
        {
            var raceWinner = await _paymentRepository.GetByExternalReferenceAsync(
                request.ExternalReference, cancellationToken);

            if (raceWinner is not null)
            {
                _logger.LogWarning(
                    "Duplicate payment processing attempt for ExternalReference {Ref} detected just before " +
                    "any mutation (lost the race to another request). Returning existing payment {PaymentId}.",
                    request.ExternalReference, raceWinner.Id);

                return Result<PaymentResponse>.Success(BuildResponse(raceWinner, invoice));
            }
        }

        // ── Step 3: Refund-specific validation ────────────────────────────────
        Payment? originalPayment = null;
        if (request.PaymentType == "Refund")
        {
            if (request.RefundedPaymentId is null)
                return Result<PaymentResponse>.Failure("RefundedPaymentId is required for refunds.");

            originalPayment = await _paymentRepository.GetByIdAsync(
                request.RefundedPaymentId.Value, cancellationToken);

            if (originalPayment is null || originalPayment.TenantId != request.TenantId)
                throw new NotFoundException(nameof(Payment), request.RefundedPaymentId.Value);

            if (originalPayment.InvoiceId != request.InvoiceId)
                return Result<PaymentResponse>.Failure(
                    "The original payment does not belong to the specified invoice.");

            // Enforces "total refunds against a payment can never exceed what was originally
            // paid" and increments the running RefundedAmount on the original payment. This
            // is also what makes two concurrent refund requests against the same original
            // payment safe: both mutate this same in-memory instance's RowVersion-tracked
            // state, and only one of the two resulting UPDATE statements can win when the
            // transaction commits below (see the DbUpdateConcurrencyException handling in
            // Step 7, and Payment.ApplyRefund's doc comment for the full explanation).
            try
            {
                originalPayment.ApplyRefund(paymentAmount);
            }
            catch (DomainException ex)
            {
                return Result<PaymentResponse>.Failure(ex.Message);
            }
        }

        // ── Step 4: Create the Payment aggregate ──────────────────────────────
        var payment = request.PaymentType == "Refund"
            ? Payment.CreateRefund(
                tenantId: request.TenantId,
                invoiceId: request.InvoiceId,
                originalPaymentId: request.RefundedPaymentId!.Value,
                refundAmount: paymentAmount,
                paymentMethod: request.PaymentMethod,
                notes: request.Notes,
                initiatedByUserId: request.InitiatedByUserId)
            : Payment.Create(
                tenantId: request.TenantId,
                invoiceId: request.InvoiceId,
                amount: paymentAmount,
                paymentMethod: request.PaymentMethod,
                externalReference: request.ExternalReference,
                notes: request.Notes,
                initiatedByUserId: request.InitiatedByUserId);

        // ── Step 5: Mark payment as completed (gateway already confirmed it) ──
        payment.Complete(request.ExternalReference);

        // ── Step 6: Apply payment to invoice aggregate ────────────────────────
        if (request.PaymentType == "Refund")
            invoice.ReversePayment(paymentAmount, request.TenantId);
        else
            invoice.RecordPayment(paymentAmount, request.TenantId);

        // ── Step 7: Persist all writes atomically ──────────────────────────────
        try
        {
            await _unitOfWork.ExecuteInTransactionAsync(async () =>
            {
                await _paymentRepository.AddAsync(payment, cancellationToken);
                await _invoiceRepository.UpdateAsync(invoice, cancellationToken);

                // For refunds, the original payment's RefundedAmount (and RowVersion) were
                // mutated in Step 3 via ApplyRefund() — persist that in the same transaction
                // as the new refund row and the invoice update, so all three either commit
                // or roll back together.
                if (originalPayment is not null)
                    await _paymentRepository.UpdateAsync(originalPayment, cancellationToken);

                var auditDescription = request.PaymentType == "Refund"
                    ? $"Refund of {paymentAmount} applied to invoice {invoice.InvoiceNumber}. " +
                      $"Invoice status: {invoiceStatusBefore} → {invoice.Status.Value}."
                    : $"Payment of {paymentAmount} received for invoice {invoice.InvoiceNumber}. " +
                      $"Invoice status: {invoiceStatusBefore} → {invoice.Status.Value}.";

                var audit = AuditLog.ForPayment(
                    tenantId: request.TenantId,
                    invoiceId: invoice.Id,
                    paymentId: payment.Id,
                    isRefund: request.PaymentType == "Refund",
                    amountDisplay: paymentAmount.ToString(),
                    userId: request.InitiatedByUserId,
                    correlationId: null);

                await _auditLogRepository.AddAsync(audit, cancellationToken);
            }, cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            // Reachable in three cases, all guarded by an existing optimistic
            // concurrency token and handled cleanly instead of surfacing as a 500:
            //  - Two payments/refunds processed concurrently against the same invoice
            //    (Invoice.UpdatedAt is a concurrency token — see InvoiceConfiguration).
            //  - Two refunds processed concurrently against the same original payment
            //    (Payment.RowVersion — see Payment.ApplyRefund's doc comment).
            //  - Two Standard payments with the same ExternalReference, racing tightly
            //    enough that both passed the Step 2.5 idempotency check before either
            //    committed. The Step 2.5 check closes the common case (one request
            //    fully finishes before the other reaches that point); this is the
            //    backstop for the rarer case where both are neck-and-neck.
            // Either way, nothing from this request was persisted (the transaction
            // rolled back) — the question is just what to tell the caller.
            _logger.LogWarning(
                ex,
                "Payment processing for invoice {InvoiceId} conflicted with a concurrent update. Rolled back, no data corrupted.",
                request.InvoiceId);

            // For the ExternalReference case specifically, the conflict almost certainly
            // means the other side of the race just committed the payment we were about
            // to create — so resolve idempotently to it rather than asking the caller to
            // retry a request that would just find the same "duplicate" again.
            //
            // Note on the invoice snapshot returned here: `invoice` is already tracked by
            // this request's DbContext and was mutated in memory by Step 6's RecordPayment
            // call. Querying for it again would return that same tracked (and now stale)
            // instance rather than a fresh read — EF Core does not overwrite an already-
            // tracked, modified entity's in-memory values from a subsequent query. Using it
            // as-is is correct for the expected case (the winning request applied the same
            // amount, since this is a genuine duplicate submission), and this is only a
            // fallback for the narrow window Step 2.5 doesn't already close. A client that
            // needs a guaranteed-fresh view can always follow up with a plain GET.
            if (request.PaymentType != "Refund" && !string.IsNullOrWhiteSpace(request.ExternalReference))
            {
                var raceWinner = await _paymentRepository.GetByExternalReferenceAsync(
                    request.ExternalReference, cancellationToken);

                if (raceWinner is not null)
                    return Result<PaymentResponse>.Success(BuildResponse(raceWinner, invoice));
            }

            return Result<PaymentResponse>.Failure(
                "This invoice or payment was updated by another request at the same time. " +
                "Please refresh and try again.");
        }

        _logger.LogInformation(
            "{PaymentType} of {Amount} {Currency} processed for invoice {InvoiceNumber} (ID: {InvoiceId}). " +
            "Invoice status: {Before} → {After}",
            request.PaymentType, request.Amount, request.Currency,
            invoice.InvoiceNumber, invoice.Id, invoiceStatusBefore, invoice.Status.Value);

        return Result<PaymentResponse>.Success(BuildResponse(payment, invoice));
    }

    private static PaymentResponse BuildResponse(Payment payment, Invoice invoice) => new()
    {
        PaymentId = payment.Id,
        Status = payment.Status.Value,
        Type = payment.Type.ToString(),
        Amount = payment.Amount.Amount,
        Currency = payment.Amount.Currency,
        PaymentMethod = payment.PaymentMethod,
        ExternalReference = payment.ExternalReference,
        CompletedAt = payment.CompletedAt,
        Invoice = new UpdatedInvoiceSnapshot
        {
            Id = invoice.Id,
            InvoiceNumber = invoice.InvoiceNumber,
            Status = invoice.Status.Value,
            TotalAmount = invoice.TotalAmount.Amount,
            PaidAmount = invoice.PaidAmount.Amount,
            OutstandingAmount = invoice.OutstandingAmount.Amount,
            Currency = invoice.Currency,
            PaidAt = invoice.PaidAt
        }
    };
}
