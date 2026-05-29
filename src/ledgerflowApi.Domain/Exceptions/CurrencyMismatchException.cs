namespace ledgerflowApi.Domain.Exceptions;

/// <summary>
/// Thrown when a monetary operation is attempted across different currencies
/// without an explicit exchange rate conversion.
/// Prevents the silent loss of precision that would occur if currencies were mixed.
/// </summary>
public class CurrencyMismatchException : DomainException
{
    public CurrencyMismatchException(string currency1, string currency2)
        : base($"Cannot operate on different currencies: {currency1} and {currency2}. " +
               "Convert to a common currency using an exchange rate service first.") { }
}
