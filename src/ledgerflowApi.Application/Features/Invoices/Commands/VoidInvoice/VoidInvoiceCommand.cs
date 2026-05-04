using ledgerflowApi.Application.Common.Interfaces;
using ledgerflowApi.Application.Common.Models;
using ledgerflowApi.Application.Features.Invoices.DTOs;
using ledgerflowApi.Domain.Entities;
using ledgerflowApi.Domain.Enums;
using ledgerflowApi.Domain.Exceptions;
using ledgerflowApi.Domain.Interfaces;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;

namespace ledgerflowApi.Application.Features.Invoices.Commands.VoidInvoice;

// ── Command ───────────────────────────────────────────────────────────────────

/// <summary>
/// Voids an invoice, making it permanently invalid.
///
/// Voiding is a terminal, irreversible action. It is used when:
///   - The invoice was created in error.
///   - The customer cancelled before payment.
///   - A correction is needed — the workflow is void + create-new, not edit-in-place.
///
/// A Reason is required — financial regulations in most jurisdictions mandate
/// a documented reason when a tax document (invoice) is cancelled.
///
/// A fully Paid invoice cannot be voided — a refund must be processed first.
/// </summary>
public sealed record VoidInvoiceCommand(
    Guid InvoiceId,
    Guid TenantId,
    Guid VoidedByUserId,
    string VoidedByUserName,
    string Reason
) : IRequest<Result<InvoiceResponse>>;

// ── Validator ─────────────────────────────────────────────────────────────────

public sealed class VoidInvoiceCommandValidator : AbstractValidator<VoidInvoiceCommand>
{
    public VoidInvoiceCommandValidator()
    {
        RuleFor(x => x.InvoiceId)
            .NotEmpty().WithMessage("InvoiceId is required.");

        RuleFor(x => x.TenantId)
            .NotEmpty().WithMessage("TenantId is required.");

        RuleFor(x => x.Reason)
            .NotEmpty().WithMessage("A reason is required when voiding an invoice.")
            .MinimumLength(10).WithMessage("Reason must be at least 10 characters.")
            .MaximumLength(500).WithMessage("Reason cannot exceed 500 characters.");
    }
}

// ── Handler ───────────────────────────────────────────────────────────────────

public sealed class VoidInvoiceCommandHandler : IRequestHandler<VoidInvoiceCommand, Result<InvoiceResponse>>
{
    private readonly IInvoiceRepository _invoiceRepository;
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<VoidInvoiceCommandHandler> _logger;

    public VoidInvoiceCommandHandler(
        IInvoiceRepository invoiceRepository,
        IAuditLogRepository auditLogRepository,
        IUnitOfWork unitOfWork,
        ILogger<VoidInvoiceCommandHandler> logger)
    {
        _invoiceRepository = invoiceRepository;
        _auditLogRepository = auditLogRepository;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result<InvoiceResponse>> Handle(
        VoidInvoiceCommand request,
        CancellationToken cancellationToken)
    {
        // ── Load and tenant-scope the invoice ─────────────────────────────────
        var invoice = await _invoiceRepository.GetByIdAsync(request.InvoiceId, cancellationToken);

        if (invoice is null || invoice.TenantId != request.TenantId)
            throw new NotFoundException(nameof(Invoice), request.InvoiceId);

        var statusBefore = invoice.Status.Value;

        // ── Domain method enforces: can't void Paid invoices, reason required ─
        invoice.Void(request.Reason);

        // ── Persist + audit ───────────────────────────────────────────────────
        await _unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            await _invoiceRepository.UpdateAsync(invoice, cancellationToken);

            var audit = AuditLog.Create(
                tenantId: request.TenantId,
                action: AuditAction.StatusChanged,
                entityType: nameof(Invoice),
                entityId: invoice.Id,
                description: $"Invoice {invoice.InvoiceNumber} voided by '{request.VoidedByUserName}'. Reason: {request.Reason}",
                userId: request.VoidedByUserId,
                userDisplayName: request.VoidedByUserName,
                stateBefore: System.Text.Json.JsonSerializer.Serialize(new { status = statusBefore }),
                stateAfter: System.Text.Json.JsonSerializer.Serialize(new
                {
                    status = invoice.Status.Value,
                    voidReason = request.Reason
                }));

            await _auditLogRepository.AddAsync(audit, cancellationToken);
        }, cancellationToken);

        _logger.LogInformation(
            "Invoice {InvoiceNumber} (ID: {InvoiceId}) voided by user {UserId}. Reason: {Reason}",
            invoice.InvoiceNumber, invoice.Id, request.VoidedByUserId, request.Reason);

        return Result<InvoiceResponse>.Success(InvoiceMapper.ToResponse(invoice));
    }
}
