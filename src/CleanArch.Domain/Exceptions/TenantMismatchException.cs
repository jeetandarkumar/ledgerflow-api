namespace CleanArch.Domain.Exceptions;

/// <summary>
/// Thrown when an operation tries to associate records from different tenants.
/// e.g. applying a Payment from TenantA to an Invoice from TenantB.
/// This is a hard guard against cross-tenant data leakage.
/// </summary>
public class TenantMismatchException : DomainException
{
    public TenantMismatchException(string operation)
        : base($"Cross-tenant operation rejected: {operation}. All related records must belong to the same tenant.") { }
}
