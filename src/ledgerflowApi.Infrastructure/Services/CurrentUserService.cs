using System.Security.Claims;
using ledgerflowApi.Application.Common.Interfaces;
using Microsoft.AspNetCore.Http;

namespace ledgerflowApi.Infrastructure.Services;

public class CurrentUserService : ICurrentUserService
{
    // Custom claim type for the tenant — added to the JWT by TokenService.GenerateAccessToken()
    public const string TenantIdClaimType = "tenant_id";
    public const string DefaultCurrencyClaimType = "tenant_currency";

    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentUserService(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public Guid? UserId
    {
        get
        {
            var value = _httpContextAccessor.HttpContext?.User?.FindFirstValue(ClaimTypes.NameIdentifier);
            return Guid.TryParse(value, out var id) ? id : null;
        }
    }

    public string? UserName =>
        _httpContextAccessor.HttpContext?.User?.FindFirstValue(ClaimTypes.Name);

    public Guid? TenantId
    {
        get
        {
            var value = _httpContextAccessor.HttpContext?.User?.FindFirstValue(TenantIdClaimType);
            return Guid.TryParse(value, out var id) ? id : null;
        }
    }

    public string? DefaultCurrency =>
        _httpContextAccessor.HttpContext?.User?.FindFirstValue(DefaultCurrencyClaimType);

    public bool IsAuthenticated =>
        _httpContextAccessor.HttpContext?.User?.Identity?.IsAuthenticated ?? false;
}
