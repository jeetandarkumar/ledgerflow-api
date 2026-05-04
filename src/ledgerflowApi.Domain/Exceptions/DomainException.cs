namespace ledgerflowApi.Domain.Exceptions;

/// <summary>
/// Base for all domain rule violations.
/// These are expected errors (business rule failures), not bugs.
/// The API layer maps them to 400/422 responses, never 500.
/// </summary>
public class DomainException : Exception
{
    public DomainException(string message) : base(message) { }
    public DomainException(string message, Exception innerException) : base(message, innerException) { }
}
