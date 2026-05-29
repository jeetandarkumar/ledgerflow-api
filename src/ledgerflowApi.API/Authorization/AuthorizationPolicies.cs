using Microsoft.AspNetCore.Authorization;

namespace ledgerflowApi.API.Authorization;

public static class AuthorizationPolicies
{
    public const string RequireAdmin = "RequireAdmin";
    public const string RequireMember = "RequireMember";
    public const string RequireAuthenticated = "RequireAuthenticated";

    public static IServiceCollection AddAuthorizationPolicies(this IServiceCollection services)
    {
        services.AddAuthorization(options =>
        {
            options.AddPolicy(RequireAdmin, policy =>
                policy.RequireAuthenticatedUser()
                      .RequireRole(Roles.Admin, Roles.SuperAdmin));

            options.AddPolicy(RequireMember, policy =>
                policy.RequireAuthenticatedUser()
                      .RequireRole(Roles.Member, Roles.Admin, Roles.SuperAdmin));

            options.AddPolicy(RequireAuthenticated, policy =>
                policy.RequireAuthenticatedUser()
                      .RequireClaim(Infrastructure.Services.CurrentUserService.TenantIdClaimType));
        });

        return services;
    }
}

/// <summary>
/// Role name constants that match UserRole enum values.
/// Use these in [Authorize(Roles = Roles.Admin)] attributes.
/// </summary>
public static class Roles
{
    public const string Viewer    = "Viewer";
    public const string Member    = "Member";
    public const string Admin     = "Admin";
    public const string SuperAdmin = "SuperAdmin";
}
