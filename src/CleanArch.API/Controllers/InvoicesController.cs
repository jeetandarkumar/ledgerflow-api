using CleanArch.Application.Common.Interfaces;
using CleanArch.Application.Features.Invoices.Commands.CreateInvoice;
using CleanArch.Application.Features.Invoices.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CleanArch.API.Controllers;

/// <summary>
/// Invoice management endpoints.
/// All routes require authentication — the JWT must carry a valid TenantId claim.
/// </summary>
[Authorize]
public class InvoicesController : BaseApiController
{
    private readonly ICurrentUserService _currentUser;

    public InvoicesController(ICurrentUserService currentUser)
    {
        _currentUser = currentUser;
    }

    /// <summary>
    /// Creates a new draft invoice for the authenticated tenant.
    /// </summary>
    /// <remarks>
    /// The invoice is created in **Draft** status. It is not sent to the customer yet.
    /// To send it, call `POST /api/v1/invoices/{id}/issue` after creation.
    ///
    /// **Invoice number** is generated automatically in the format `INV-{YYYY}-{NNNNNN}`
    /// (e.g. `INV-2024-000042`). Numbers are sequential per tenant, per year.
    ///
    /// **Currency** defaults to the tenant's configured default currency if omitted.
    ///
    /// **Validation failures** return HTTP 422 with a structured error body showing
    /// exactly which fields failed and why.
    /// </remarks>
    /// <param name="request">Invoice creation payload.</param>
    /// <param name="cancellationToken">Request cancellation token.</param>
    /// <returns>The fully resolved invoice including all computed financial figures.</returns>
    /// <response code="201">Invoice created. Location header points to the new resource.</response>
    /// <response code="400">Business rule violation (e.g. tenant is suspended).</response>
    /// <response code="401">Missing or invalid JWT.</response>
    /// <response code="422">Validation failed. Body contains field-level error details.</response>
    [HttpPost]
    [ProducesResponseType(typeof(InvoiceResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> CreateInvoice(
        [FromBody] CreateInvoiceRequest request,
        CancellationToken cancellationToken)
    {
        // Resolve tenant from the JWT. Every authenticated request carries a TenantId claim
        // set during login. We don't accept TenantId from the request body — clients cannot
        // self-select their own tenant context.
        var tenantId = _currentUser.TenantId;
        if (tenantId is null)
        {
            return Unauthorized(new ProblemDetails
            {
                Title = "Missing tenant context",
                Detail = "The authentication token does not contain a valid tenant identifier.",
                Status = StatusCodes.Status401Unauthorized
            });
        }

        // Map the HTTP request DTO to the MediatR command.
        // This separation means the API contract (CreateInvoiceRequest) can evolve
        // independently of the application command — useful for API versioning.
        var command = new CreateInvoiceCommand(
            TenantId: tenantId.Value,
            CustomerName: request.CustomerName,
            CustomerEmail: request.CustomerEmail,
            Currency: ResolveCurrency(request),
            TaxRatePercentage: request.TaxRatePercentage,
            DiscountPercentage: request.DiscountPercentage,
            LineItems: request.LineItems.Select(li => new CreateInvoiceLineItemCommand(
                Description: li.Description,
                UnitPrice: li.UnitPrice,
                Quantity: li.Quantity,
                DiscountPercentage: li.DiscountPercentage,
                ProductReference: li.ProductReference
            )).ToList(),
            Notes: request.Notes,
            BillingAddress: request.BillingAddress is { } addr
                ? new CreateInvoiceBillingAddressCommand(
                    Line1: addr.Line1,
                    Line2: addr.Line2,
                    City: addr.City,
                    State: addr.State,
                    CountryCode: addr.CountryCode,
                    PostalCode: addr.PostalCode)
                : null
        );

        var result = await Mediator.Send(command, cancellationToken);

        if (!result.Succeeded)
        {
            // Handler returned a business-level failure (not an exception).
            // These are soft errors the caller can act on.
            return BadRequest(new ProblemDetails
            {
                Title = "Invoice creation failed",
                Detail = string.Join("; ", result.Errors),
                Status = StatusCodes.Status400BadRequest
            });
        }

        // 201 Created with a Location header pointing to the new resource.
        // Clients should follow the Location URL to retrieve the full invoice.
        return CreatedAtAction(
            actionName: nameof(GetInvoice),
            routeValues: new { id = result.Data!.Id },
            value: result.Data);
    }

    /// <summary>
    /// Gets a specific invoice by ID.
    /// </summary>
    /// <param name="id">Invoice GUID.</param>
    /// <param name="cancellationToken">Request cancellation token.</param>
    /// <response code="200">Invoice found and returned.</response>
    /// <response code="404">Invoice not found or does not belong to the current tenant.</response>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(InvoiceResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetInvoice(Guid id, CancellationToken cancellationToken)
    {
        // Placeholder — implement GetInvoiceQuery in the next slice.
        // Stubbed here so CreatedAtAction in CreateInvoice has a valid action to reference.
        return await Task.FromResult<IActionResult>(NotFound(new ProblemDetails
        {
            Title = "Not implemented",
            Detail = "GetInvoice query is not yet implemented.",
            Status = StatusCodes.Status404NotFound
        }));
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Resolves the invoice currency.
    /// If the request omits Currency, we fall back to the tenant's default currency
    /// (pulled from the JWT claim set at login time).
    /// If neither is available, we default to USD — the handler will validate this
    /// against the tenant's actual settings and throw if it's wrong.
    /// </summary>
    private string ResolveCurrency(CreateInvoiceRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.Currency))
            return request.Currency;

        if (!string.IsNullOrWhiteSpace(_currentUser.DefaultCurrency))
            return _currentUser.DefaultCurrency;

        return "USD";
    }
}
