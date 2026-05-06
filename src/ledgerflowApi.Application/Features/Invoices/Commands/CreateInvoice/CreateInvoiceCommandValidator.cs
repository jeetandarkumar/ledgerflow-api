using ledgerflowApi.Application.Features.Invoices.Commands.CreateInvoice;
using FluentValidation;

namespace ledgerflowApi.Application.Features.Invoices.Commands.CreateInvoice;

/// <summary>
/// Validates the CreateInvoiceCommand before it reaches the handler.
/// Runs as part of the MediatR ValidationBehavior pipeline, so any failures
/// throw Application.Common.Exceptions.ValidationException which the global
/// middleware maps to HTTP 422 Unprocessable Entity with a structured error body.
///
/// Validation philosophy:
/// - Required field checks and format checks live here (the "can we even attempt this?").
/// - Business rule checks live in the domain (the "is this legally valid?").
///   e.g. "CustomerEmail must look like an email" = validator responsibility.
///       "Tenant must not be suspended" = handler + domain responsibility.
/// </summary>
public sealed class CreateInvoiceCommandValidator : AbstractValidator<CreateInvoiceCommand>
{
    // ISO 4217 codes we explicitly support. Extendable — just add to the set.
    // Keeping an allowlist rather than free-form prevents "XXX" or "ABC" slipping through.
    private static readonly HashSet<string> SupportedCurrencies = new(StringComparer.OrdinalIgnoreCase)
    {
        "USD", "EUR", "GBP", "CAD", "AUD", "JPY", "CHF", "SGD", "HKD", "NOK",
        "SEK", "DKK", "NZD", "ZAR", "INR", "BRL", "MXN", "AED", "SAR", "PLN"
    };

    public CreateInvoiceCommandValidator()
    {
        // ── Tenant ────────────────────────────────────────────────────────────
        RuleFor(x => x.TenantId)
            .NotEmpty()
            .WithMessage("TenantId is required.");

        // ── Customer ──────────────────────────────────────────────────────────
        RuleFor(x => x.CustomerName)
            .NotEmpty()
            .WithMessage("Customer name is required.")
            .MaximumLength(200)
            .WithMessage("Customer name cannot exceed 200 characters.");

        RuleFor(x => x.CustomerEmail)
            .NotEmpty()
            .WithMessage("Customer email is required.")
            .EmailAddress()
            .WithMessage("Customer email must be a valid email address.")
            .MaximumLength(256)
            .WithMessage("Customer email cannot exceed 256 characters.");

        // ── Currency ──────────────────────────────────────────────────────────
        RuleFor(x => x.Currency)
            .NotEmpty()
            .WithMessage("Currency is required.")
            .Length(3)
            .WithMessage("Currency must be a 3-character ISO 4217 code (e.g. USD, EUR, GBP).")
            .Must(c => SupportedCurrencies.Contains(c))
            .WithMessage(x =>
                $"'{x.Currency}' is not a supported currency. " +
                $"Supported currencies: {string.Join(", ", SupportedCurrencies.Order())}.");

        // ── Rates & Discounts ─────────────────────────────────────────────────
        RuleFor(x => x.TaxRatePercentage)
            .InclusiveBetween(0m, 100m)
            .WithMessage("Tax rate must be between 0 and 100.");

        RuleFor(x => x.DiscountPercentage)
            .InclusiveBetween(0m, 100m)
            .WithMessage("Invoice discount must be between 0 and 100.");

        // ── Line Items ────────────────────────────────────────────────────────
        RuleFor(x => x.LineItems)
            .NotEmpty()
            .WithMessage("At least one line item is required.");

        RuleFor(x => x.LineItems)
            .Must(lines => lines.Count <= 200)
            .WithMessage("An invoice cannot have more than 200 line items.")
            .When(x => x.LineItems.Count > 0);

        RuleForEach(x => x.LineItems)
            .SetValidator(new CreateInvoiceLineItemCommandValidator());

        // ── Notes ─────────────────────────────────────────────────────────────
        RuleFor(x => x.Notes)
            .MaximumLength(2000)
            .WithMessage("Notes cannot exceed 2000 characters.")
            .When(x => x.Notes is not null);

        // ── Billing Address (when provided, all required fields must be present) ──
        When(x => x.BillingAddress is not null, () =>
        {
            RuleFor(x => x.BillingAddress!.Line1)
                .NotEmpty()
                .WithMessage("Billing address Line1 is required.")
                .MaximumLength(200)
                .WithMessage("Billing address Line1 cannot exceed 200 characters.");

            RuleFor(x => x.BillingAddress!.City)
                .NotEmpty()
                .WithMessage("Billing address City is required.")
                .MaximumLength(100)
                .WithMessage("Billing address City cannot exceed 100 characters.");

            RuleFor(x => x.BillingAddress!.CountryCode)
                .NotEmpty()
                .WithMessage("Billing address CountryCode is required.")
                .Length(2)
                .WithMessage("Billing address CountryCode must be a 2-character ISO 3166-1 code (e.g. US, GB).")
                .Matches("^[A-Z]{2}$")
                .WithMessage("Billing address CountryCode must be uppercase letters only (e.g. US, GB).");

            RuleFor(x => x.BillingAddress!.PostalCode)
                .NotEmpty()
                .WithMessage("Billing address PostalCode is required.")
                .MaximumLength(20)
                .WithMessage("Billing address PostalCode cannot exceed 20 characters.");
        });
    }
}

/// <summary>
/// Validates a single line item on the invoice command.
/// Extracted to its own class so it can be tested independently
/// and reused if a bulk-update command is added later.
/// </summary>
public sealed class CreateInvoiceLineItemCommandValidator : AbstractValidator<CreateInvoiceLineItemCommand>
{
    public CreateInvoiceLineItemCommandValidator()
    {
        RuleFor(x => x.Description)
            .NotEmpty()
            .WithMessage("Line item description is required.")
            .MaximumLength(500)
            .WithMessage("Line item description cannot exceed 500 characters.");

        RuleFor(x => x.UnitPrice)
            .GreaterThan(0m)
            .WithMessage("Unit price must be greater than zero.")
            // 10 million per unit seems like a reasonable upper bound for a SaaS billing line.
            // Prevents fat-finger amounts that would corrupt financial reports.
            .LessThanOrEqualTo(10_000_000m)
            .WithMessage("Unit price cannot exceed 10,000,000.");

        RuleFor(x => x.Quantity)
            .GreaterThan(0m)
            .WithMessage("Quantity must be greater than zero.")
            .LessThanOrEqualTo(100_000m)
            .WithMessage("Quantity cannot exceed 100,000.");

        RuleFor(x => x.DiscountPercentage)
            .InclusiveBetween(0m, 100m)
            .WithMessage("Line item discount must be between 0 and 100.");

        RuleFor(x => x.ProductReference)
            .MaximumLength(100)
            .WithMessage("Product reference cannot exceed 100 characters.")
            .When(x => x.ProductReference is not null);
    }
}
