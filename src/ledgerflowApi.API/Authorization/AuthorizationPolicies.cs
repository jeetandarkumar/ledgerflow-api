using Microsoft.AspNetCore.Authorization;

namespace ledgerflowApi.API.Authorization;

/// <summary>
/// Central definitions for all authorization policy names used across the API.
///
/// Why constants instead of inline strings?
/// Typos in [Authorize(Policy = "...")] strings fail silently at runtime — the request
/// is just rejected with 403 and there's no compile-time error. Using these constants
/// gives you a compile error if a name changes, and makes "find all usages" work in the IDE.
///
/// Policy vs Roles:
/// For simple role checks we use [Authorize(Roles = Roles.Admin)] directly — it's clear
/// and ASP.NET Core handles it natively from the ClaimTypes.Role claim in the JWT.
/// Policies are used when the authorization rule is more complex than a single role
/// (e.g. "must be Admin OR be the resource owner").
/// </summary>
public static class AuthorizationPolicies
{
    /// <summary>Caller must hold Admin or SuperAdmin role within their tenant.</summary>
    public const string RequireAdmin = "RequireAdmin";

    /// <summary>Caller must hold Member, Admin, or SuperAdmin role (excludes Viewer).</summary>
    public const string RequireMember = "RequireMember";

    /// <summary>Any authenticated user with a valid tenant claim.</summary>
    public const string RequireAuthenticated = "RequireAuthenticated";

    /// <summary>
    /// Registers all policies with the DI container.
    /// Called once from Program.cs / AuthenticationExtensions.
    /// </summary>
    public static IServiceCollection AddAuthorizationPolicies(this IServiceCollection services)
    {
        services.AddAuthorization(options =>
        {
            // Admin-only: tenant administrators and platform super-admins.
            options.AddPolicy(RequireAdmin, policy =>
                policy.RequireAuthenticatedUser()
                      .RequireRole(Roles.Admin, Roles.SuperAdmin));

            // Member+: anyone who can create/modify records (not read-only Viewers).
            options.AddPolicy(RequireMember, policy =>
                policy.RequireAuthenticatedUser()
                      .RequireRole(Roles.Member, Roles.Admin, Roles.SuperAdmin));

            // Any authenticated user with a tenant context.
            options.AddPolicy(RequireAuthenticated, policy =>
                policy.RequireAuthenticatedUser()
                      .RequireClaim(Infrastructure.Services.CurrentUserService.TenantIdClaimType));
        });

        return services;
    }
}

/// <summary>
/// Role name constants that exactly match the UserRole enum ToString() values.
/// Used in [Authorize(Roles = Roles.Admin)] attributes and policy definitions.
/// </summary>
public static class Roles
{
    public const string Viewer    = "Viewer";
    public const string Member    = "Member";
    public const string Admin     = "Admin";
    public const string SuperAdmin = "SuperAdmin";
}
