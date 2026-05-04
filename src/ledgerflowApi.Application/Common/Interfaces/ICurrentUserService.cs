namespace ledgerflowApi.Application.Common.Interfaces;

/// <summary>
/// Provides identity context for the currently authenticated user.
/// Populated from the JWT claims in the current HTTP request.
/// </summary>
public interface ICurrentUserService
{
    /// <summary>The authenticated user's GUID. Null if the request is unauthenticated.</summary>
    Guid? UserId { get; }

    /// <summary>The user's display name (FirstName + LastName from the JWT).</summary>
    string? UserName { get; }

    /// <summary>The tenant this user belongs to. Null if unauthenticated.</summary>
    Guid? TenantId { get; }

    /// <summary>
    /// The tenant's default currency from the JWT (e.g. "USD").
    /// Carried in the token so controllers can resolve it without a DB lookup.
    /// </summary>
    string? DefaultCurrency { get; }

    bool IsAuthenticated { get; }
}
