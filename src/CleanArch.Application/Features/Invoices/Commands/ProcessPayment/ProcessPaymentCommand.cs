using CleanArch.Application.Common.Interfaces;
using CleanArch.Application.Common.Models;
using CleanArch.Domain.Entities;
using CleanArch.Domain.Enums;
using CleanArch.Domain.Exceptions;
using CleanArch.Domain.Interfaces;
using CleanArch.Domain.ValueObjects;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;

namespace CleanArch.Application.Features.Invoices.Commands.ProcessPayment;

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
            .GreaterThan(0).WithMessage("Payment amount must be greater than zero.")
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

            if (!originalPayment.Status.IsCompleted)
                return Result<PaymentResponse>.Failure(
                    $"Only Completed payments can be refunded. Original payment status: {originalPayment.Status.Value}.");
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

        // ── Step 7: Persist all three writes atomically ───────────────────────
        await _unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            await _paymentRepository.AddAsync(payment, cancellationToken);
            await _invoiceRepository.UpdateAsync(invoice, cancellationToken);

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
