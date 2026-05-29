namespace ledgerflowApi.Domain.Exceptions;

/// <summary>
/// Thrown when code tries to move an entity into a status that isn't
/// reachable from the current status (e.g. Draft → Paid, skipping Issued).
/// </summary>
public class InvalidStatusTransitionException : DomainException
{
    public InvalidStatusTransitionException(string entityName, string from, string to)
        : base($"Cannot transition {entityName} from '{from}' to '{to}'.") { }
}
