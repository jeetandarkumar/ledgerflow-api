using FluentAssertions;
using FluentValidation.TestHelper;
using ledgerflowApi.Application.Features.Auth.Commands.Login;
using ledgerflowApi.Application.Features.Invoices.Commands.CreateInvoice;
using ledgerflowApi.Application.Features.Invoices.Commands.ProcessPayment;
using Xunit;

namespace LedgerFlow.UnitTests.Application.Validation;

/// <summary>
/// Tests for all FluentValidation validators.
/// These are pure unit tests — no DI, no database.
/// The validators are critical gatekeepers; bad input that slips through
/// them could corrupt financial data.
/// </summary>
public class ValidatorTests
{
    // ── LoginCommandValidator ─────────────────────────────────────────────────

    public class LoginCommandValidatorTests
    {
        private readonly LoginCommandValidator _validator = new();

        [Fact]
        public void Validate_ValidCommand_PassesValidation()
        {
            var command = new LoginCommand(Guid.NewGuid(), "alice@acme.com", "Password123!");
            _validator.TestValidate(command).ShouldNotHaveAnyValidationErrors();
        }

        [Fact]
        public void Validate_EmptyTenantId_FailsWithTenantContextMessage()
        {
            var command = new LoginCommand(Guid.Empty, "alice@acme.com", "Password123!");
            _validator.TestValidate(command)
                .ShouldHaveValidationErrorFor(x => x.TenantId)
                .WithErrorMessage("Tenant context is required.");
        }

        [Theory]
        [InlineData("")]
        [InlineData("not-an-email")]
        [InlineData("missingdomain@")]
        public void Validate_InvalidEmail_FailsValidation(string email)
        {
            var command = new LoginCommand(Guid.NewGuid(), email, "Password123!");
            _validator.TestValidate(command).ShouldHaveValidationErrorFor(x => x.Email);
        }

        [Fact]
        public void Validate_EmailOver256Chars_FailsValidation()
        {
            var longEmail = new string('a', 250) + "@x.com"; // >256 chars
            var command = new LoginCommand(Guid.NewGuid(), longEmail, "Password123!");
            _validator.TestValidate(command).ShouldHaveValidationErrorFor(x => x.Email);
        }

        [Fact]
        public void Validate_EmptyPassword_FailsValidation()
        {
            var command = new LoginCommand(Guid.NewGuid(), "alice@acme.com", "");
            _validator.TestValidate(command).ShouldHaveValidationErrorFor(x => x.Password);
        }
    }

    // ── CreateInvoiceCommandValidator ─────────────────────────────────────────

    public class CreateInvoiceCommandValidatorTests
    {
        private readonly CreateInvoiceCommandValidator _validator = new();

        private static CreateInvoiceCommand MakeValid()
            => new(
                TenantId: Guid.NewGuid(),
                CustomerName: "Acme Corp",
                CustomerEmail: "billing@acme.com",
                Currency: "USD",
                TaxRatePercentage: 20m,
                DiscountPercentage: 0m,
                LineItems: [new("Consulting", 100m, 1m, 0m, null)],
                Notes: null,
                BillingAddress: null);

        [Fact]
        public void Validate_ValidCommand_PassesValidation()
        {
            _validator.TestValidate(MakeValid()).ShouldNotHaveAnyValidationErrors();
        }

        [Fact]
        public void Validate_EmptyCustomerName_FailsValidation()
        {
            var command = MakeValid() with { CustomerName = "" };
            _validator.TestValidate(command).ShouldHaveValidationErrorFor(x => x.CustomerName);
        }

        [Fact]
        public void Validate_InvalidCustomerEmail_FailsValidation()
        {
            var command = MakeValid() with { CustomerEmail = "not-an-email" };
            _validator.TestValidate(command).ShouldHaveValidationErrorFor(x => x.CustomerEmail);
        }

        [Theory]
        [InlineData("US")]
        [InlineData("USDD")]
        [InlineData("")]
        public void Validate_InvalidCurrencyCode_FailsValidation(string currency)
        {
            var command = MakeValid() with { Currency = currency };
            _validator.TestValidate(command).ShouldHaveValidationErrorFor(x => x.Currency);
        }

        [Fact]
        public void Validate_EmptyLineItems_FailsValidation()
        {
            var command = MakeValid() with { LineItems = [] };
            _validator.TestValidate(command).ShouldHaveValidationErrorFor(x => x.LineItems);
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(101)]
        public void Validate_TaxRateOutOfRange_FailsValidation(decimal taxRate)
        {
            var command = MakeValid() with { TaxRatePercentage = taxRate };
            _validator.TestValidate(command).ShouldHaveValidationErrorFor(x => x.TaxRatePercentage);
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(101)]
        public void Validate_DiscountOutOfRange_FailsValidation(decimal discount)
        {
            var command = MakeValid() with { DiscountPercentage = discount };
            _validator.TestValidate(command).ShouldHaveValidationErrorFor(x => x.DiscountPercentage);
        }

        [Fact]
        public void Validate_LineItemWithZeroPrice_FailsValidation()
        {
            var command = MakeValid() with
            {
                LineItems = [new("Item", 0m, 1m, 0m, null)]  // zero price not allowed
            };
            _validator.TestValidate(command)
                .ShouldHaveValidationErrorFor("LineItems[0].UnitPrice");
        }

        [Fact]
        public void Validate_LineItemWithZeroQuantity_FailsValidation()
        {
            var command = MakeValid() with
            {
                LineItems = [new("Item", 100m, 0m, 0m, null)]
            };
            _validator.TestValidate(command)
                .ShouldHaveValidationErrorFor("LineItems[0].Quantity");
        }
    }

    // ── ProcessPaymentCommandValidator ────────────────────────────────────────

    public class ProcessPaymentCommandValidatorTests
    {
        private readonly ProcessPaymentCommandValidator _validator = new();

        private static ProcessPaymentCommand MakeValid()
            => new(
                TenantId: Guid.NewGuid(),
                InvoiceId: Guid.NewGuid(),
                Amount: 100m,
                Currency: "USD",
                PaymentMethod: "card",
                PaymentType: "Standard",
                ExternalReference: "pi_test_123",
                RefundedPaymentId: null,
                InitiatedByUserId: Guid.NewGuid(),
                InitiatedByUserName: "Alice",
                Notes: null);

        [Fact]
        public void Validate_ValidStandardPayment_PassesValidation()
        {
            _validator.TestValidate(MakeValid()).ShouldNotHaveAnyValidationErrors();
        }

        [Fact]
        public void Validate_ZeroAmount_FailsValidation()
        {
            var command = MakeValid() with { Amount = 0m };
            _validator.TestValidate(command)
                .ShouldHaveValidationErrorFor(x => x.Amount)
                .WithErrorMessage("*greater than zero*");
        }

        [Fact]
        public void Validate_AmountExceedsMax_FailsValidation()
        {
            var command = MakeValid() with { Amount = 10_000_001m };
            _validator.TestValidate(command).ShouldHaveValidationErrorFor(x => x.Amount);
        }

        [Theory]
        [InlineData("US")]
        [InlineData("XYZ")]  // not in supported list
        public void Validate_UnsupportedCurrency_FailsValidation(string currency)
        {
            var command = MakeValid() with { Currency = currency };
            _validator.TestValidate(command).ShouldHaveValidationErrorFor(x => x.Currency);
        }

        [Theory]
        [InlineData("Standard")]
        [InlineData("Refund")]
        public void Validate_ValidPaymentTypes_PassValidation(string type)
        {
            var command = MakeValid() with
            {
                PaymentType = type,
                RefundedPaymentId = type == "Refund" ? Guid.NewGuid() : null
            };
            _validator.TestValidate(command).ShouldNotHaveAnyValidationErrors();
        }

        [Fact]
        public void Validate_InvalidPaymentType_FailsValidation()
        {
            var command = MakeValid() with { PaymentType = "Unknown" };
            _validator.TestValidate(command).ShouldHaveValidationErrorFor(x => x.PaymentType);
        }

        [Fact]
        public void Validate_RefundWithoutRefundedPaymentId_FailsValidation()
        {
            var command = MakeValid() with
            {
                PaymentType = "Refund",
                RefundedPaymentId = null
            };
            _validator.TestValidate(command)
                .ShouldHaveValidationErrorFor(x => x.RefundedPaymentId);
        }
    }
}
