namespace ledgerflowApi.Domain.Exceptions;

public class InsufficientPermissionsException : DomainException
{
    public InsufficientPermissionsException(string action)
        : base($"The current user does not have permission to: {action}.") { }
}
