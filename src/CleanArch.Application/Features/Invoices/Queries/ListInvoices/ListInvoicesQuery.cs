using CleanArch.Application.Common.Models;
using CleanArch.Application.Features.Invoices.DTOs;
using CleanArch.Domain.Interfaces;
using CleanArch.Domain.ValueObjects;
using FluentValidation;
using MediatR;

namespace CleanArch.Application.Features.Invoices.Queries.ListInvoices;

// ── Query ─────────────────────────────────────────────────────────────────────

/// <summary>
/// Returns a paginated list of invoices for the caller's tenant,
/// with optional filtering by status and date range.
/// </summary>
public sealed record ListInvoicesQuery(
    Guid TenantId,
    string? Status = null,
    int PageNumber = 1,
    int PageSize = 20
) : IRequest<Result<ListInvoicesResponse>>;

// ── Response shape ────────────────────────────────────────────────────────────

public sealed class ListInvoicesResponse
{
    public List<InvoiceSummary> Items { get; init; } = [];
    public int TotalCount { get; init; }
    public int PageNumber { get; init; }
    public int PageSize { get; init; }
    public int TotalPages { get; init; }
    public bool HasPreviousPage { get; init; }
    public bool HasNextPage { get; init; }

    // Aggregate totals for the current filter (not just the current page)
    public decimal TotalOutstanding { get; init; }
    public decimal TotalOverdue { get; init; }
    public string Currency { get; init; } = string.Empty;
}

public sealed class InvoiceSummary
{
    public Guid Id { get; init; }
    public string InvoiceNumber { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string CustomerName { get; init; } = string.Empty;
    public string CustomerEmail { get; init; } = string.Empty;
    public string Currency { get; init; } = string.Empty;
    public decimal TotalAmount { get; init; }
    public decimal PaidAmount { get; init; }
    public decimal OutstandingAmount { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? IssuedAt { get; init; }
    public DateTime? DueDate { get; init; }
    public DateTime? PaidAt { get; init; }
    public int LineItemCount { get; init; }
}

// ── Validator ─────────────────────────────────────────────────────────────────

public sealed class ListInvoicesQueryValidator : AbstractValidator<ListInvoicesQuery>
{
    private static readonly HashSet<string> ValidStatuses = new(StringComparer.OrdinalIgnoreCase)
        { "Draft", "Issued", "PartiallyPaid", "Paid", "Overdue", "Voided" };

    public ListInvoicesQueryValidator()
    {
        RuleFor(x => x.TenantId)
            .NotEmpty().WithMessage("TenantId is required.");

        RuleFor(x => x.PageNumber)
            .GreaterThanOrEqualTo(1).WithMessage("PageNumber must be at least 1.");

        RuleFor(x => x.PageSize)
            .InclusiveBetween(1, 100).WithMessage("PageSize must be between 1 and 100.");

        RuleFor(x => x.Status)
            .Must(s => s is null || ValidStatuses.Contains(s))
            .WithMessage($"Status must be one of: {string.Join(", ", ValidStatuses.OrderBy(s => s))}.")
            .When(x => x.Status is not null);
    }
}

// ── Handler ───────────────────────────────────────────────────────────────────

public sealed class ListInvoicesQueryHandler : IRequestHandler<ListInvoicesQuery, Result<ListInvoicesResponse>>
{
    private readonly IInvoiceRepository _invoiceRepository;

    public ListInvoicesQueryHandler(IInvoiceRepository invoiceRepository)
    {
        _invoiceRepository = invoiceRepository;
    }

    public async Task<Result<ListInvoicesResponse>> Handle(
        ListInvoicesQuery request,
        CancellationToken cancellationToken)
    {
        // Parse optional status filter
        InvoiceStatus? statusFilter = request.Status is not null
            ? InvoiceStatus.From(request.Status)
            : null;

        // Load all invoices matching the filter (tenant-scoped in repo)
        var all = (await _invoiceRepository.GetByTenantAsync(
            request.TenantId,
            statusFilter,
            cancellationToken)).ToList();

        var totalCount = all.Count;

        // Compute aggregate totals across the full filtered set (not just the page)
        var totalOutstanding = all
            .Where(i => !i.Status.IsPaid && !i.Status.IsVoided)
            .Sum(i => i.OutstandingAmount.Amount);

        var totalOverdue = all
            .Where(i => i.Status == InvoiceStatus.Overdue)
            .Sum(i => i.OutstandingAmount.Amount);

        // The currency for the totals — use the first invoice's currency.
        // In a production system with multi-currency tenants this would need
        // separate aggregation per currency; for now tenants have one default.
        var currency = all.FirstOrDefault()?.Currency ?? "USD";

        // Apply pagination
        var paged = all
            .Skip((request.PageNumber - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToList();

        var totalPages = (int)Math.Ceiling(totalCount / (double)request.PageSize);

        var summaries = paged.Select(i => new InvoiceSummary
        {
            Id = i.Id,
            InvoiceNumber = i.InvoiceNumber,
            Status = i.Status.Value,
            CustomerName = i.CustomerName,
            CustomerEmail = i.CustomerEmail,
            Currency = i.Currency,
            TotalAmount = i.TotalAmount.Amount,
            PaidAmount = i.PaidAmount.Amount,
            OutstandingAmount = i.OutstandingAmount.Amount,
            CreatedAt = i.CreatedAt,
            IssuedAt = i.IssuedAt,
            DueDate = i.DueDate,
            PaidAt = i.PaidAt,
            LineItemCount = i.LineItems.Count
        }).ToList();

        return Result<ListInvoicesResponse>.Success(new ListInvoicesResponse
        {
            Items = summaries,
            TotalCount = totalCount,
            PageNumber = request.PageNumber,
            PageSize = request.PageSize,
            TotalPages = totalPages,
            HasPreviousPage = request.PageNumber > 1,
            HasNextPage = request.PageNumber < totalPages,
            TotalOutstanding = totalOutstanding,
            TotalOverdue = totalOverdue,
            Currency = currency
        });
    }
}
