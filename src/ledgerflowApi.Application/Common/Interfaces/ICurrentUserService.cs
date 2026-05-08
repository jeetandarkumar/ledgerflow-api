namespace ledgerflowApi.Application.Common.Interfaces;

/// <summary>Provides identity context for the currently authenticated user from JWT claims.</summary>
public interface ICurrentUserService
{
    Guid? UserId { get; }
    string? UserName { get; }
    Guid? TenantId { get; }
    /// <summary>Tenant's default ISO 4217 currency (e.g. "USD"). Carried in the JWT to avoid a DB lookup.</summary>
    string? DefaultCurrency { get; }
    bool IsAuthenticated { get; }
}
