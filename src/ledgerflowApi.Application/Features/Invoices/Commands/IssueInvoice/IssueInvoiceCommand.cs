using ledgerflowApi.Application.Common.Interfaces;
using ledgerflowApi.Application.Common.Models;
using ledgerflowApi.Application.Features.Invoices.DTOs;
using ledgerflowApi.Domain.Entities;
using ledgerflowApi.Domain.Enums;
using ledgerflowApi.Domain.Exceptions;
using ledgerflowApi.Domain.Interfaces;
using ledgerflowApi.Domain.ValueObjects;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;

namespace ledgerflowApi.Application.Features.Invoices.Commands.IssueInvoice;

// ── Command ───────────────────────────────────────────────────────────────────

/// <summary>
/// Transitions a Draft invoice to Issued status and delivers it to the customer.
///
/// "Issuing" means the invoice is now a formal financial commitment:
///   - The due date is set and the clock starts ticking.
///   - Line items are frozen — no further edits allowed.
///   - An InvoiceIssuedEvent is raised so email delivery / notifications can happen
///     asynchronously without coupling this command to any email service.
/// </summary>
public sealed record IssueInvoiceCommand(
    Guid InvoiceId,
    Guid TenantId,
    Guid IssuedByUserId,
    string IssuedByUserName,
    DateTime DueDate,
    IssueInvoiceBillingAddressCommand? BillingAddress = null
) : IRequest<Result<InvoiceResponse>>;

public sealed record IssueInvoiceBillingAddressCommand(
    string Line1,
    string? Line2,
    string City,
    string? State,
    string CountryCode,
    string PostalCode
);

// ── Validator ─────────────────────────────────────────────────────────────────

public sealed class IssueInvoiceCommandValidator : AbstractValidator<IssueInvoiceCommand>
{
    public IssueInvoiceCommandValidator()
    {
        RuleFor(x => x.InvoiceId)
            .NotEmpty().WithMessage("InvoiceId is required.");

        RuleFor(x => x.TenantId)
            .NotEmpty().WithMessage("TenantId is required.");

        RuleFor(x => x.DueDate)
            .GreaterThan(DateTime.UtcNow)
            .WithMessage("Due date must be in the future.");

        When(x => x.BillingAddress is not null, () =>
        {
            RuleFor(x => x.BillingAddress!.Line1)
                .NotEmpty().WithMessage("Billing address Line1 is required.")
                .MaximumLength(200);

            RuleFor(x => x.BillingAddress!.City)
                .NotEmpty().WithMessage("Billing address City is required.")
                .MaximumLength(100);

            RuleFor(x => x.BillingAddress!.CountryCode)
                .NotEmpty().WithMessage("Billing address CountryCode is required.")
                .Length(2).WithMessage("CountryCode must be 2 characters (ISO 3166-1).")
                .Matches("^[A-Z]{2}$").WithMessage("CountryCode must be uppercase letters.");

            RuleFor(x => x.BillingAddress!.PostalCode)
                .NotEmpty().WithMessage("Billing address PostalCode is required.")
                .MaximumLength(20);
        });
    }
}

// ── Handler ───────────────────────────────────────────────────────────────────

/// <summary>
/// Orchestrates the Issue Invoice flow:
///   1. Load the invoice and verify it belongs to the caller's tenant.
///   2. Call invoice.Issue() on the domain aggregate — this enforces business rules
///      (must be draft, must have lines, due date must be future) and raises the event.
///   3. Persist the state change + audit log in one transaction.
///   4. Return the updated InvoiceResponse.
///
/// Domain event InvoiceIssuedEvent is raised inside invoice.Issue() and is dispatched
/// after the transaction commits via the ApplicationDbContext SaveChangesAsync override.
/// </summary>
public sealed class IssueInvoiceCommandHandler : IRequestHandler<IssueInvoiceCommand, Result<InvoiceResponse>>
{
    private readonly IInvoiceRepository _invoiceRepository;
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<IssueInvoiceCommandHandler> _logger;

    public IssueInvoiceCommandHandler(
        IInvoiceRepository invoiceRepository,
        IAuditLogRepository auditLogRepository,
        IUnitOfWork unitOfWork,
        ILogger<IssueInvoiceCommandHandler> logger)
    {
        _invoiceRepository = invoiceRepository;
        _auditLogRepository = auditLogRepository;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result<InvoiceResponse>> Handle(
        IssueInvoiceCommand request,
        CancellationToken cancellationToken)
    {
        // ── Load invoice ──────────────────────────────────────────────────────
        var invoice = await _invoiceRepository.GetByIdAsync(request.InvoiceId, cancellationToken);

        if (invoice is null || invoice.TenantId != request.TenantId)
            throw new NotFoundException(nameof(Invoice), request.InvoiceId);

        var statusBefore = invoice.Status.Value;

        // ── Build optional billing address value object ───────────────────────
        Address? billingAddress = request.BillingAddress is { } addr
            ? new Address(addr.Line1, addr.Line2, addr.City, addr.State, addr.CountryCode, addr.PostalCode)
            : null;

        // ── Call domain method (enforces all business rules) ──────────────────
        // invoice.Issue() throws DomainException / InvalidStatusTransitionException on violations.
        // GlobalExceptionHandlingMiddleware maps those to 400/422 appropriately.
        invoice.Issue(request.DueDate, billingAddress);

        // ── Persist + audit in one transaction ────────────────────────────────
        await _unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            await _invoiceRepository.UpdateAsync(invoice, cancellationToken);

            var audit = AuditLog.ForStatusChange(
                tenantId: request.TenantId,
                entityType: nameof(Invoice),
                entityId: invoice.Id,
                fromStatus: statusBefore,
                toStatus: invoice.Status.Value,
                description: $"Invoice {invoice.InvoiceNumber} issued to '{invoice.CustomerEmail}'. " +
                             $"Due date: {request.DueDate:yyyy-MM-dd}.",
                userId: request.IssuedByUserId,
                userDisplayName: request.IssuedByUserName);

            await _auditLogRepository.AddAsync(audit, cancellationToken);
        }, cancellationToken);

        _logger.LogInformation(
            "Invoice {InvoiceNumber} (ID: {InvoiceId}) issued by user {UserId} — due {DueDate:yyyy-MM-dd}",
            invoice.InvoiceNumber, invoice.Id, request.IssuedByUserId, request.DueDate);

        return Result<InvoiceResponse>.Success(InvoiceMapper.ToResponse(invoice));
    }
}
