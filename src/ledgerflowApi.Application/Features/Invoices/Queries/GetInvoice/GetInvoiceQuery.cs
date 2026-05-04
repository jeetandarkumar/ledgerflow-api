using ledgerflowApi.Application.Common.Models;
using ledgerflowApi.Application.Features.Invoices.DTOs;
using ledgerflowApi.Domain.Entities;
using ledgerflowApi.Domain.Exceptions;
using ledgerflowApi.Domain.Interfaces;
using MediatR;

namespace ledgerflowApi.Application.Features.Invoices.Queries.GetInvoice;

// ── Query ─────────────────────────────────────────────────────────────────────

/// <summary>
/// Returns a single invoice by ID, scoped to the caller's tenant.
/// Returns the same 404 if the invoice doesn't exist OR belongs to another tenant —
/// never leak cross-tenant existence.
/// </summary>
public sealed record GetInvoiceQuery(
    Guid InvoiceId,
    Guid TenantId
) : IRequest<Result<InvoiceResponse>>;

// ── Handler ───────────────────────────────────────────────────────────────────

public sealed class GetInvoiceQueryHandler : IRequestHandler<GetInvoiceQuery, Result<InvoiceResponse>>
{
    private readonly IInvoiceRepository _invoiceRepository;

    public GetInvoiceQueryHandler(IInvoiceRepository invoiceRepository)
    {
        _invoiceRepository = invoiceRepository;
    }

    public async Task<Result<InvoiceResponse>> Handle(
        GetInvoiceQuery request,
        CancellationToken cancellationToken)
    {
        var invoice = await _invoiceRepository.GetByIdAsync(request.InvoiceId, cancellationToken);

        // Return 404 for both "not found" and "wrong tenant" — never distinguish them
        if (invoice is null || invoice.TenantId != request.TenantId)
            return Result<InvoiceResponse>.Failure(
                $"Invoice '{request.InvoiceId}' was not found.");

        return Result<InvoiceResponse>.Success(InvoiceMapper.ToResponse(invoice));
    }
}
