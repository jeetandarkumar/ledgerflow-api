namespace CleanArch.Domain.Enums;

/// <summary>
/// The type of change recorded in an AuditLog entry.
/// Kept as a closed enum so audit consumers can safely switch on it
/// without worrying about unexpected values.
/// </summary>
public enum AuditAction
{
    Created  = 0,
    Updated  = 1,
    Deleted  = 2,
    StatusChanged = 3,
    PaymentReceived = 4,
    PaymentRefunded = 5,
    LoginSucceeded = 6,
    LoginFailed = 7
}
