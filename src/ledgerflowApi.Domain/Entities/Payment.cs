using ledgerflowApi.Domain.Common;
using ledgerflowApi.Domain.Events;
using ledgerflowApi.Domain.Exceptions;
using ledgerflowApi.Domain.ValueObjects;

namespace ledgerflowApi.Domain.Entities;

/// <summary>
/// Represents a single payment attempt against an invoice.
///
/// Aggregate rules:
/// - Payment is its own aggregate root, not a child of Invoice.
///   This keeps the Invoice aggregate from becoming a "God object" and
///   allows payments to be processed independently (async payment gateways).
/// - The Invoice aggregate is notified via RecordPayment() in the application layer
///   after a Payment reaches Completed status — the coordination happens in the
///   application layer, not inside either aggregate.
///
/// Financial rules:
/// - Amount must be positive — zero-value payments are not meaningful.
/// - A refund is modelled as a new Payment record linked to the original via
///   RefundedPaymentId, rather than mutating the original. This preserves
///   the immutable audit trail: every payment record is a "fact" that happened.
/// - Currency must match the invoice currency (no FX).
/// - Only Completed payments can trigger invoice state changes.
///
/// Payment gateway integration:
/// - ExternalReference stores the payment processor's transaction ID
///   (Stripe charge ID, PayPal transaction ID, etc.) for reconciliation.
/// - PaymentMethod records what was used (card, bank transfer, etc.) for reporting.
/// </summary>
public class Payment : TenantEntity
{
    // ── Relationships ─────────────────────────────────────────────────────────

    /// <summary>
    /// The invoice this payment is being applied to.
    /// All payments must be linked to a specific invoice — unallocated
    /// payments are not supported in this model.
    /// </summary>
    public Guid InvoiceId { get; private set; }

    public Invoice Invoice { get; private set; } = null!;

    /// <summary>
    /// For refund records only: the ID of the original completed payment
    /// this refund is reversing. Null for non-refund payments.
    /// </summary>
    public Guid? RefundedPaymentId { get; private set; }

    // ── Financials ────────────────────────────────────────────────────────────

    /// <summary>
    /// The amount of this payment. Always positive — refunds are also positive
    /// amounts but stored as a separate record with PaymentType = Refund.
    /// </summary>
    public Money Amount { get; private set; } = null!;

    /// <summary>
    /// For Standard payments only: the running total of all refunds applied against this
    /// payment so far. Starts at zero and is incremented by <see cref="ApplyRefund"/> — never
    /// decremented, since a refund, once completed, is a permanent fact.
    ///
    /// This is the source of truth for "how much of this payment is still refundable",
    /// rather than deriving it by summing child refund rows on every request. Tracking it
    /// here, on the original payment itself, is also what makes the row-version concurrency
    /// check below effective: two refunds racing against the same original payment both
    /// have to update this same row, so the database — not application code — decides which
    /// one commits first.
    /// </summary>
    public Money RefundedAmount { get; private set; } = null!;

    /// <summary>
    /// EF Core concurrency token (SQL Server ROWVERSION). Automatically changed by the
    /// database on every UPDATE to this row.
    ///
    /// This is what makes <see cref="ApplyRefund"/> safe under concurrency: if two refund
    /// requests are processed against the same original payment at the same time, both read
    /// the same starting RowVersion, but only the first one to commit its UPDATE succeeds —
    /// the second one's UPDATE targets a RowVersion that no longer matches, EF Core raises
    /// DbUpdateConcurrencyException, and the transaction is rolled back. The caller (see
    /// ProcessPaymentCommandHandler) turns that into a "try again" response rather than
    /// silently applying both refunds.
    /// </summary>
    public byte[] RowVersion { get; private set; } = [];

    // ── Metadata ──────────────────────────────────────────────────────────────

    public PaymentStatus Status { get; private set; } = PaymentStatus.Pending;

    /// <summary>
    /// Type of payment, used for reporting and reconciliation.
    /// </summary>
    public PaymentType Type { get; private set; } = PaymentType.Standard;

    /// <summary>
    /// The payment method used: "card", "bank_transfer", "cash", etc.
    /// Stored as a free-form string to accommodate different gateway naming conventions.
    /// </summary>
    public string PaymentMethod { get; private set; } = string.Empty;

    /// <summary>
    /// The payment processor's transaction/charge ID (e.g. Stripe's "ch_xxx").
    /// Used for reconciliation with bank statements and processor dashboards.
    /// Null until the processor confirms the payment.
    /// </summary>
    public string? ExternalReference { get; private set; }

    /// <summary>
    /// Human-readable note about the payment (e.g. "Retry after card update").
    /// </summary>
    public string? Notes { get; private set; }

    /// <summary>If failed, the reason reported by the payment processor.</summary>
    public string? FailureReason { get; private set; }

    /// <summary>When the payment was confirmed as completed by the processor.</summary>
    public DateTime? CompletedAt { get; private set; }

    // ── Audit ─────────────────────────────────────────────────────────────────

    /// <summary>The user who initiated this payment (for manual/offline payments).</summary>
    public Guid? InitiatedByUserId { get; private set; }

    private Payment() { } // EF Core

    // ── Factories ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a new pending payment against an invoice.
    /// The payment is created in Pending state; the processor callback
    /// will move it to Completed or Failed.
    /// </summary>
    public static Payment Create(
        Guid tenantId,
        Guid invoiceId,
        Money amount,
        string paymentMethod,
        string? externalReference = null,
        string? notes = null,
        Guid? initiatedByUserId = null)
    {
        ValidateAmount(amount);
        ValidatePaymentMethod(paymentMethod);

        return new Payment
        {
            TenantId = tenantId,
            InvoiceId = invoiceId,
            Amount = amount,
            RefundedAmount = new Money(0m, amount.Currency),
            PaymentMethod = paymentMethod.Trim(),
            ExternalReference = externalReference?.Trim(),
            Notes = notes?.Trim(),
            InitiatedByUserId = initiatedByUserId,
            Status = PaymentStatus.Pending,
            Type = PaymentType.Standard
        };
    }

    /// <summary>
    /// Creates a refund record against a previously completed payment.
    ///
    /// Design note: refunds are stored as separate Payment records rather
    /// than modifying the original. This gives us a clean append-only ledger
    /// where every row represents a discrete financial event. The original
    /// payment record remains unchanged as proof that money was received.
    /// </summary>
    public static Payment CreateRefund(
        Guid tenantId,
        Guid invoiceId,
        Guid originalPaymentId,
        Money refundAmount,
        string paymentMethod,
        string? notes = null,
        Guid? initiatedByUserId = null)
    {
        ValidateAmount(refundAmount);

        return new Payment
        {
            TenantId = tenantId,
            InvoiceId = invoiceId,
            RefundedPaymentId = originalPaymentId,
            Amount = refundAmount,
            RefundedAmount = new Money(0m, refundAmount.Currency),
            PaymentMethod = paymentMethod.Trim(),
            Notes = notes?.Trim(),
            InitiatedByUserId = initiatedByUserId,
            Status = PaymentStatus.Pending,
            Type = PaymentType.Refund
        };
    }

    // ── Status Transitions ────────────────────────────────────────────────────

    /// <summary>
    /// Marks the payment as completed after the processor confirms it.
    ///
    /// Business rules:
    /// - externalReference should be provided if available (for reconciliation).
    /// - A completed standard payment triggers Invoice.RecordPayment() in the
    ///   application layer (not here — that would be a cross-aggregate violation).
    /// - A completed refund triggers Invoice.ReversePayment() in the application layer.
    /// </summary>
    public void Complete(string? externalReference = null)
    {
        GuardTransition(PaymentStatus.Completed);

        Status = PaymentStatus.Completed;
        CompletedAt = DateTime.UtcNow;
        if (!string.IsNullOrWhiteSpace(externalReference))
            ExternalReference = externalReference.Trim();

        FailureReason = null;
        SetUpdatedAt();

        var evt = Type == PaymentType.Refund
            ? (object)new PaymentRefundedEvent(Id, RefundedPaymentId!.Value, InvoiceId, TenantId)
            : new PaymentCompletedEvent(Id, InvoiceId, TenantId);

        if (evt is PaymentCompletedEvent completedEvt)
            AddDomainEvent(completedEvt);
        else if (evt is PaymentRefundedEvent refundedEvt)
            AddDomainEvent(refundedEvt);
    }

    /// <summary>
    /// Applies a refund against this (Standard) payment, incrementing <see cref="RefundedAmount"/>.
    ///
    /// Business rule: total refunds against a single payment can never exceed the original
    /// payment amount. This is enforced here, inside the aggregate, rather than by summing
    /// sibling refund rows in application code each time — so the rule holds no matter which
    /// caller processes a refund, and holds under concurrency via the RowVersion token (see
    /// its doc comment): two refunds racing against the same payment both mutate this same
    /// row, so only one can win the database's optimistic-concurrency check.
    ///
    /// Only a Standard, Completed payment can be refunded — refunding a refund would make the
    /// ledger ambiguous, so Type is checked alongside Status.
    /// </summary>
    public void ApplyRefund(Money refundAmount)
    {
        if (Type != PaymentType.Standard)
            throw new DomainException(
                "Only a Standard payment can be refunded — a refund cannot itself be refunded.");

        if (!Status.IsCompleted)
            throw new DomainException(
                $"Only Completed payments can be refunded. Current status: {Status.Value}.");

        if (refundAmount.Currency != Amount.Currency)
            throw new DomainException(
                $"Refund currency ({refundAmount.Currency}) does not match the original " +
                $"payment currency ({Amount.Currency}).");

        var totalAfterRefund = RefundedAmount.Add(refundAmount);

        if (totalAfterRefund.IsGreaterThan(Amount))
            throw new DomainException(
                $"Cannot refund {refundAmount} — only {RemainingRefundableAmount} of this " +
                $"{Amount} payment remains refundable ({RefundedAmount} already refunded).");

        RefundedAmount = totalAfterRefund;
        SetUpdatedAt();
    }

    /// <summary>The portion of this payment that has not yet been refunded.</summary>
    public Money RemainingRefundableAmount => Amount.Subtract(RefundedAmount);

    /// <summary>
    /// Marks the payment as failed.
    ///
    /// Business rule: a reason is required for failed payments — it is used
    /// to notify the customer and diagnose gateway issues (insufficient funds,
    /// expired card, fraud block, etc.).
    /// </summary>
    public void Fail(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new DomainException("A failure reason is required when marking a payment as failed.");

        GuardTransition(PaymentStatus.Failed);

        Status = PaymentStatus.Failed;
        FailureReason = reason.Trim();
        SetUpdatedAt();

        AddDomainEvent(new PaymentFailedEvent(Id, InvoiceId, TenantId, reason));
    }

    /// <summary>
    /// Cancels a pending or failed payment.
    ///
    /// Business rule: completed payments cannot be cancelled — they must be refunded.
    /// Cancellation is for payments that never left our system (e.g. user abandoned checkout).
    /// </summary>
    public void Cancel(string? reason = null)
    {
        if (Status == PaymentStatus.Completed)
            throw new DomainException(
                "A completed payment cannot be cancelled. Process a refund instead.");

        GuardTransition(PaymentStatus.Cancelled);

        Status = PaymentStatus.Cancelled;
        if (!string.IsNullOrWhiteSpace(reason))
            Notes = string.IsNullOrWhiteSpace(Notes)
                ? $"Cancelled: {reason}"
                : $"{Notes} | Cancelled: {reason}";

        SetUpdatedAt();
    }

    /// <summary>
    /// Resets a failed payment back to Pending for retry.
    ///
    /// Business rule: only failed payments can be retried — completed and cancelled
    /// payments are terminal. A new ExternalReference can be provided if the
    /// retry uses a new processor transaction.
    /// </summary>
    public void RetryFromFailed(string? newExternalReference = null)
    {
        if (Status != PaymentStatus.Failed)
            throw new DomainException(
                $"Only failed payments can be retried. Current status: {Status}.");

        GuardTransition(PaymentStatus.Pending);

        Status = PaymentStatus.Pending;
        FailureReason = null;
        if (!string.IsNullOrWhiteSpace(newExternalReference))
            ExternalReference = newExternalReference.Trim();

        SetUpdatedAt();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    public bool IsRefund => Type == PaymentType.Refund;

    private void GuardTransition(PaymentStatus target)
    {
        if (!Status.CanTransitionTo(target))
            throw new InvalidStatusTransitionException(
                nameof(Payment), Status.Value, target.Value);
    }

    private static void ValidateAmount(Money amount)
    {
        if (amount is null)
            throw new ArgumentNullException(nameof(amount));
        if (amount.IsZero)
            throw new DomainException("Payment amount must be greater than zero.");
    }

    private static void ValidatePaymentMethod(string method)
    {
        if (string.IsNullOrWhiteSpace(method))
            throw new DomainException("Payment method is required.");
        if (method.Length > 50)
            throw new DomainException("Payment method cannot exceed 50 characters.");
    }
}

/// <summary>
/// Distinguishes a standard payment from a refund in the payments ledger.
/// Using a type flag rather than two separate tables keeps reporting queries simple
/// and preserves the append-only ledger model.
/// </summary>
public enum PaymentType
{
    Standard = 0,
    Refund   = 1
}
