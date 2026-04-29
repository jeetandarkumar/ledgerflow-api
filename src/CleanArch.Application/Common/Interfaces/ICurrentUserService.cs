namespace CleanArch.Application.Common.Interfaces;

public interface ICurrentUserService
{
    Guid? UserId { get; }
    string? UserName { get; }
    Guid? TenantId { get; }

    string? DefaultCurrency { get; }
    bool IsAuthenticated { get; }
}
