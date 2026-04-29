using CleanArch.Application.Common.Interfaces;
using CleanArch.Application.Common.Models;
using CleanArch.Application.Features.Invoices.DTOs;
using CleanArch.Domain.Entities;
using CleanArch.Domain.Enums;
using CleanArch.Domain.Exceptions;
using CleanArch.Domain.Interfaces;
using CleanArch.Domain.ValueObjects;
using MediatR;
using Microsoft.Extensions.Logging;

namespace CleanArch.Application.Features.Invoices.Commands.CreateInvoice;

// ── Command ───────────────────────────────────────────────────────────────────

/// <summary>
/// Command to create a new draft invoice for the current tenant.
///
/// Design note: the command carries the full creation intent including line items.
/// The invoice is always created in Draft status — issuing it (sending to the customer)
/// is a separate command. This two-step model lets users build and preview before committing.
/// </summary>
public sealed record CreateInvoiceCommand(
    Guid TenantId,
    string CustomerName,
    string CustomerEmail,
    string Currency,
    decimal TaxRatePercentage,
    decimal DiscountPercentage,
    List<CreateInvoiceLineItemCommand> LineItems,
    string? Notes,
    CreateInvoiceBillingAddressCommand? BillingAddress
) : IRequest<Result<InvoiceResponse>>;

public sealed record CreateInvoiceLineItemCommand(
    string Description,
    decimal UnitPrice,
    decimal Quantity,
    decimal DiscountPercentage,
    string? ProductReference
);

public sealed record CreateInvoiceBillingAddressCommand(
    string Line1,
    string? Line2,
    string City,
    string? State,
    string CountryCode,
    string PostalCode
);

// ── Handler ───────────────────────────────────────────────────────────────────

/// <summary>
/// Orchestrates the full Create Invoice flow:
///
///   1. Resolve the calling user and tenant context.
///   2. Verify the tenant is permitted to create invoices (not suspended/cancelled).
///   3. Generate the next sequential invoice number (atomic, via stored procedure).
///   4. Build the Invoice aggregate with all line items.
///   5. Persist everything in a single transaction (invoice + audit log).
///   6. Return the full InvoiceResponse DTO.
///
/// Transaction scope:
/// The invoice insert and audit log insert share the same DbContext transaction.
/// If the audit log write fails, the invoice creation rolls back — a financial
/// record without an audit trail is worse than no record at all.
/// </summary>
public sealed class CreateInvoiceCommandHandler : IRequestHandler<CreateInvoiceCommand, Result<InvoiceResponse>>
{
    private readonly IInvoiceRepository _invoiceRepository;
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly ITenantRepository _tenantRepository;
    private readonly ICurrentUserService _currentUser;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<CreateInvoiceCommandHandler> _logger;

    public CreateInvoiceCommandHandler(
        IInvoiceRepository invoiceRepository,
        IAuditLogRepository auditLogRepository,
        ITenantRepository tenantRepository,
        ICurrentUserService currentUser,
        IUnitOfWork unitOfWork,
        ILogger<CreateInvoiceCommandHandler> logger)
    {
        _invoiceRepository = invoiceRepository;
        _auditLogRepository = auditLogRepository;
        _tenantRepository = tenantRepository;
        _currentUser = currentUser;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result<InvoiceResponse>> Handle(
        CreateInvoiceCommand request,
        CancellationToken cancellationToken)
    {
        // ── Step 1: Resolve and validate tenant ───────────────────────────────

        var tenant = await _tenantRepository.GetByIdAsync(request.TenantId, cancellationToken);
        if (tenant is null)
            throw new NotFoundException(nameof(Tenant), request.TenantId);

        if (!tenant.CanCreateInvoices())
            throw new DomainException(
                $"Tenant '{tenant.Name}' cannot create invoices while in '{tenant.Status}' status. " +
                "Resolve any billing issues or reactivate the account first.");

        // ── Step 2: Resolve the current user ──────────────────────────────────

        var userId = _currentUser.UserId
            ?? throw new DomainException("An authenticated user is required to create an invoice.");

        _logger.LogInformation(
            "Creating invoice for tenant {TenantId} by user {UserId}",
            request.TenantId, userId);

        // ── Step 3: Generate unique invoice number ────────────────────────────
        // This calls the stored procedure usp_GetNextInvoiceNumber which uses a
        // serialised sequence table to guarantee uniqueness under concurrent load.
        // The number format is: INV-{YYYY}-{NNNNNN} (e.g. INV-2024-000042)

        var sequence = await _invoiceRepository.GetNextInvoiceSequenceAsync(
            request.TenantId, cancellationToken);

        var invoiceNumber = FormatInvoiceNumber(sequence);

        _logger.LogDebug(
            "Generated invoice number {InvoiceNumber} (sequence {Sequence}) for tenant {TenantId}",
            invoiceNumber, sequence, request.TenantId);

        // ── Step 4: Build the Invoice aggregate ───────────────────────────────

        var invoice = Invoice.Create(
            tenantId: request.TenantId,
            createdByUserId: userId,
            invoiceNumber: invoiceNumber,
            customerName: request.CustomerName,
            customerEmail: request.CustomerEmail,
            currency: request.Currency,
            taxRatePercentage: request.TaxRatePercentage,
            discountPercentage: request.DiscountPercentage,
            notes: request.Notes);

        // Map and add line items. AddLineItem validates currency consistency
        // and throws DomainException if anything is wrong — no silent failures.
        foreach (var line in request.LineItems)
        {
            var lineItem = new InvoiceLineItem(
                description: line.Description,
                unitPrice: Money.Of(line.UnitPrice, request.Currency),
                quantity: line.Quantity,
                discountPercentage: line.DiscountPercentage,
                productReference: line.ProductReference);

            invoice.AddLineItem(lineItem);
        }

        // ── Step 5: Persist within a transaction ──────────────────────────────
        // The unit of work wraps both inserts in one transaction.
        // If either fails, both roll back.

        await _unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            await _invoiceRepository.AddAsync(invoice, cancellationToken);

            var auditEntry = AuditLog.Create(
                tenantId: request.TenantId,
                action: AuditAction.Created,
                entityType: nameof(Invoice),
                entityId: invoice.Id,
                description: $"Invoice {invoiceNumber} created for customer '{request.CustomerEmail}'. " +
                             $"Total: {invoice.TotalAmount}.",
                userId: userId,
                userDisplayName: _currentUser.UserName,
                stateAfter: BuildAuditSnapshot(invoice));

            await _auditLogRepository.AddAsync(auditEntry, cancellationToken);

        }, cancellationToken);

        _logger.LogInformation(
            "Invoice {InvoiceNumber} (ID: {InvoiceId}) created successfully for tenant {TenantId}",
            invoiceNumber, invoice.Id, request.TenantId);

        // ── Step 6: Map to response DTO ───────────────────────────────────────

        return Result<InvoiceResponse>.Success(MapToResponse(invoice));
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Produces a human-readable, predictable invoice number.
    /// Format: INV-{YYYY}-{NNNNNN} — year-scoped so the number resets each year,
    /// and sequences don't balloon to 7+ digits for typical SaaS invoice volumes.
    /// </summary>
    private static string FormatInvoiceNumber(int sequence)
        => $"INV-{DateTime.UtcNow.Year}-{sequence:D6}";

    /// <summary>
    /// Builds a compact JSON snapshot of the invoice for the audit log StateAfter field.
    /// Avoids full serialisation of the aggregate — captures only the fields relevant
    /// to recreating what the invoice looked like when it was first created.
    /// </summary>
    private static string BuildAuditSnapshot(Invoice invoice)
        => System.Text.Json.JsonSerializer.Serialize(new
        {
            invoiceNumber = invoice.InvoiceNumber,
            customerEmail = invoice.CustomerEmail,
            currency = invoice.Currency,
            status = invoice.Status.Value,
            lineItemCount = invoice.LineItems.Count,
            subtotal = invoice.Subtotal.Amount,
            taxAmount = invoice.TaxAmount.Amount,
            totalAmount = invoice.TotalAmount.Amount,
            createdAt = invoice.CreatedAt
        });

    /// <summary>
    /// Maps the Invoice aggregate to the API response DTO.
    /// Kept as an explicit mapping method rather than AutoMapper because
    /// the mapping includes computed properties and we want the compiler
    /// to catch any shape changes — invisible AutoMapper profile bugs are
    /// particularly painful for financial data.
    /// </summary>
    private static InvoiceResponse MapToResponse(Invoice invoice) => new()
    {
        Id = invoice.Id,
        InvoiceNumber = invoice.InvoiceNumber,
        Status = invoice.Status.Value,
        CustomerName = invoice.CustomerName,
        CustomerEmail = invoice.CustomerEmail,
        BillingAddress = invoice.BillingAddress is { } addr ? new InvoiceBillingAddressResponse
        {
            Line1 = addr.Line1,
            Line2 = addr.Line2,
            City = addr.City,
            State = addr.State,
            CountryCode = addr.CountryCode,
            PostalCode = addr.PostalCode
        } : null,
        CreatedAt = invoice.CreatedAt,
        IssuedAt = invoice.IssuedAt,
        DueDate = invoice.DueDate,
        PaidAt = invoice.PaidAt,
        Currency = invoice.Currency,
        TaxRatePercentage = invoice.TaxRatePercentage,
        DiscountPercentage = invoice.DiscountPercentage,
        Subtotal = invoice.Subtotal.Amount,
        InvoiceDiscountAmount = invoice.InvoiceDiscountAmount.Amount,
        DiscountedSubtotal = invoice.DiscountedSubtotal.Amount,
        TaxAmount = invoice.TaxAmount.Amount,
        TotalAmount = invoice.TotalAmount.Amount,
        PaidAmount = invoice.PaidAmount.Amount,
        OutstandingAmount = invoice.OutstandingAmount.Amount,
        Notes = invoice.Notes,
        CreatedByUserId = invoice.CreatedByUserId,
        LineItems = invoice.LineItems.Select(li => new InvoiceLineItemResponse
        {
            Description = li.Description,
            UnitPrice = li.UnitPrice.Amount,
            Quantity = li.Quantity,
            DiscountPercentage = li.DiscountPercentage,
            ProductReference = li.ProductReference,
            GrossAmount = li.GrossAmount.Amount,
            DiscountAmount = li.DiscountAmount.Amount,
            NetAmount = li.NetAmount.Amount
        }).ToList()
    };
}
