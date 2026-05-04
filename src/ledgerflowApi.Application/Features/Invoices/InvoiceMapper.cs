using ledgerflowApi.Application.Features.Invoices.DTOs;
using ledgerflowApi.Domain.Entities;

namespace ledgerflowApi.Application.Features.Invoices;

/// <summary>
/// Shared explicit mapper from Invoice aggregate to InvoiceResponse DTO.
///
/// Why static explicit mapping instead of AutoMapper?
/// - Invoice has computed properties (Subtotal, TaxAmount, TotalAmount) that
///   AutoMapper can't discover without explicit profile configuration.
/// - Financial mapping bugs are silent with AutoMapper — a missing property
///   mapping returns 0 rather than throwing. Explicit code fails loudly.
/// - All handlers (Create, Issue, Void, GetInvoice) need the same mapping —
///   putting it here avoids duplication without the ceremony of a profile class.
/// </summary>
public static class InvoiceMapper
{
    public static InvoiceResponse ToResponse(Invoice invoice) => new()
    {
        Id = invoice.Id,
        InvoiceNumber = invoice.InvoiceNumber,
        Status = invoice.Status.Value,

        CustomerName = invoice.CustomerName,
        CustomerEmail = invoice.CustomerEmail,
        BillingAddress = invoice.BillingAddress is { } addr
            ? new InvoiceBillingAddressResponse
            {
                Line1 = addr.Line1,
                Line2 = addr.Line2,
                City = addr.City,
                State = addr.State,
                CountryCode = addr.CountryCode,
                PostalCode = addr.PostalCode
            }
            : null,

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
