namespace CleanArch.Application.Features.Auth.DTOs;

/// <summary>
/// Sent by the client to authenticate and receive a JWT.
/// Both fields are required — the validator enforces this.
/// </summary>
public sealed class LoginRequest
{
    public string Email { get; init; } = string.Empty;
    public string Password { get; init; } = string.Empty;
}

/// <summary>
/// Sent by a tenant Admin to create a new user account within their tenant.
/// The TenantId is NOT in this payload — it is always read from the caller's JWT
/// so a client cannot self-select which tenant to register into.
/// </summary>
public sealed class RegisterUserRequest
{
    public string FirstName { get; init; } = string.Empty;
    public string LastName { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string Password { get; init; } = string.Empty;

    /// <summary>
    /// Role to assign the new user. Defaults to Member.
    /// Callers with Admin role can assign Member or Viewer.
    /// SuperAdmin is never assignable through this endpoint — the handler enforces this.
    /// </summary>
    public string Role { get; init; } = "Member";
}

/// <summary>
/// Returned after a successful login or registration.
/// The access token is a signed JWT; the refresh token is an opaque random string.
/// Clients should store both securely (httpOnly cookie or secure storage — never localStorage).
/// </summary>
public sealed class AuthResponse
{
    public string AccessToken { get; init; } = string.Empty;
    public string RefreshToken { get; init; } = string.Empty;
    public DateTime ExpiresAt { get; init; }
    public UserAuthInfo User { get; init; } = null!;
}

/// <summary>
/// Minimal user information embedded in the auth response.
/// Saves the client an extra GET /users/{id} call after login.
/// </summary>
public sealed class UserAuthInfo
{
    public Guid Id { get; init; }
    public string FullName { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string Role { get; init; } = string.Empty;
    public Guid TenantId { get; init; }
    public string TenantName { get; init; } = string.Empty;
}
