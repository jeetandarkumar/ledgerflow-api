using ledgerflowApi.API.Authorization;
using ledgerflowApi.Application.Common.Interfaces;
using ledgerflowApi.Application.Features.Invoices.Commands.CreateInvoice;
using ledgerflowApi.Application.Features.Invoices.Commands.IssueInvoice;
using ledgerflowApi.Application.Features.Invoices.Commands.ProcessPayment;
using ledgerflowApi.Application.Features.Invoices.Commands.VoidInvoice;
using ledgerflowApi.Application.Features.Invoices.DTOs;
using ledgerflowApi.Application.Features.Invoices.Queries.GetInvoice;
using ledgerflowApi.Application.Features.Invoices.Queries.ListInvoices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ledgerflowApi.API.Controllers;

/// <summary>
/// Invoice lifecycle: create, issue, void, list, get, and record payments.
/// All routes are tenant-scoped via the JWT.
/// </summary>
[Authorize]
public class InvoicesController : BaseApiController
{
    private readonly ICurrentUserService _currentUser;

    public InvoicesController(ICurrentUserService currentUser)
    {
        _currentUser = currentUser;
    }

    /// <summary>Returns a paginated list of invoices for the authenticated tenant.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(ListInvoicesResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> ListInvoices(
        [FromQuery] string? status,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var tenantId = _currentUser.TenantId;
        if (tenantId is null) return Unauthorized();

        var result = await Mediator.Send(
            new ListInvoicesQuery(tenantId.Value, status, pageNumber, pageSize),
            cancellationToken);

        return result.Succeeded ? Ok(result.Data) : BadRequest(result.Errors);
    }

    /// <summary>Returns a single invoice by ID.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(InvoiceResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetInvoice(Guid id, CancellationToken cancellationToken)
    {
        var tenantId = _currentUser.TenantId;
        if (tenantId is null) return Unauthorized();

        var result = await Mediator.Send(new GetInvoiceQuery(id, tenantId.Value), cancellationToken);

        if (!result.Succeeded)
            return NotFound(new ProblemDetails
            {
                Title = "Invoice not found",
                Detail = result.Errors.FirstOrDefault(),
                Status = StatusCodes.Status404NotFound
            });

        return Ok(result.Data);
    }

    /// <summary>Creates a new invoice in Draft status.</summary>
    [HttpPost]
    [Authorize(Policy = AuthorizationPolicies.RequireMember)]
    [ProducesResponseType(typeof(InvoiceResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> CreateInvoice(
        [FromBody] CreateInvoiceRequest request,
        CancellationToken cancellationToken)
    {
        var tenantId = _currentUser.TenantId;
        if (tenantId is null) return Unauthorized();

        var command = new CreateInvoiceCommand(
            TenantId: tenantId.Value,
            CustomerName: request.CustomerName,
            CustomerEmail: request.CustomerEmail,
            Currency: ResolveCurrency(request.Currency),
            TaxRatePercentage: request.TaxRatePercentage,
            DiscountPercentage: request.DiscountPercentage,
            LineItems: request.LineItems.Select(li => new CreateInvoiceLineItemCommand(
                li.Description, li.UnitPrice, li.Quantity, li.DiscountPercentage, li.ProductReference
            )).ToList(),
            Notes: request.Notes,
            BillingAddress: request.BillingAddress is { } addr
                ? new CreateInvoiceBillingAddressCommand(addr.Line1, addr.Line2, addr.City,
                    addr.State, addr.CountryCode, addr.PostalCode)
                : null);

        var result = await Mediator.Send(command, cancellationToken);

        if (!result.Succeeded)
            return BadRequest(new ProblemDetails
            {
                Title = "Invoice creation failed",
                Detail = string.Join("; ", result.Errors),
                Status = StatusCodes.Status400BadRequest
            });

        return CreatedAtAction(nameof(GetInvoice), new { id = result.Data!.Id }, result.Data);
    }

    /// <summary>Issues a Draft invoice, making it payable by the customer.</summary>
    [HttpPost("{id:guid}/issue")]
    [Authorize(Policy = AuthorizationPolicies.RequireMember)]
    [ProducesResponseType(typeof(InvoiceResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> IssueInvoice(
        Guid id,
        [FromBody] IssueInvoiceRequest request,
        CancellationToken cancellationToken)
    {
        var tenantId = _currentUser.TenantId;
        var userId = _currentUser.UserId;
        if (tenantId is null || userId is null) return Unauthorized();

        var command = new IssueInvoiceCommand(
            InvoiceId: id,
            TenantId: tenantId.Value,
            IssuedByUserId: userId.Value,
            IssuedByUserName: _currentUser.UserName ?? "Unknown",
            DueDate: request.DueDate,
            BillingAddress: request.BillingAddress is { } addr
                ? new IssueInvoiceBillingAddressCommand(addr.Line1, addr.Line2, addr.City,
                    addr.State, addr.CountryCode, addr.PostalCode)
                : null);

        var result = await Mediator.Send(command, cancellationToken);

        return result.Succeeded
            ? Ok(result.Data)
            : BadRequest(new ProblemDetails
            {
                Title = "Failed to issue invoice",
                Detail = string.Join("; ", result.Errors),
                Status = StatusCodes.Status400BadRequest
            });
    }

    /// <summary>Voids an invoice permanently. Requires Admin role.</summary>
    [HttpPost("{id:guid}/void")]
    [Authorize(Policy = AuthorizationPolicies.RequireAdmin)]
    [ProducesResponseType(typeof(InvoiceResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> VoidInvoice(
        Guid id,
        [FromBody] VoidInvoiceRequest request,
        CancellationToken cancellationToken)
    {
        var tenantId = _currentUser.TenantId;
        var userId = _currentUser.UserId;
        if (tenantId is null || userId is null) return Unauthorized();

        var command = new VoidInvoiceCommand(
            InvoiceId: id,
            TenantId: tenantId.Value,
            VoidedByUserId: userId.Value,
            VoidedByUserName: _currentUser.UserName ?? "Unknown",
            Reason: request.Reason);

        var result = await Mediator.Send(command, cancellationToken);

        return result.Succeeded
            ? Ok(result.Data)
            : BadRequest(new ProblemDetails
            {
                Title = "Failed to void invoice",
                Detail = string.Join("; ", result.Errors),
                Status = StatusCodes.Status400BadRequest
            });
    }

    /// <summary>Records a confirmed payment or refund against an invoice.</summary>
    [HttpPost("{id:guid}/payments")]
    [Authorize(Policy = AuthorizationPolicies.RequireMember)]
    [ProducesResponseType(typeof(PaymentResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> ProcessPayment(
        Guid id,
        [FromBody] ProcessPaymentRequest request,
        CancellationToken cancellationToken)
    {
        var tenantId = _currentUser.TenantId;
        if (tenantId is null) return Unauthorized();

        var command = new ProcessPaymentCommand(
            TenantId: tenantId.Value,
            InvoiceId: id,
            Amount: request.Amount,
            Currency: request.Currency,
            PaymentMethod: request.PaymentMethod,
            PaymentType: request.Type,
            ExternalReference: request.ExternalReference,
            RefundedPaymentId: request.RefundedPaymentId,
            InitiatedByUserId: _currentUser.UserId,
            InitiatedByUserName: _currentUser.UserName,
            Notes: request.Notes);

        var result = await Mediator.Send(command, cancellationToken);

        return result.Succeeded
            ? Ok(result.Data)
            : BadRequest(new ProblemDetails
            {
                Title = "Payment processing failed",
                Detail = string.Join("; ", result.Errors),
                Status = StatusCodes.Status400BadRequest
            });
    }

    private string ResolveCurrency(string? requested)
    {
        if (!string.IsNullOrWhiteSpace(requested)) return requested;
        if (!string.IsNullOrWhiteSpace(_currentUser.DefaultCurrency)) return _currentUser.DefaultCurrency!;
        return "USD";
    }
}
