using CleanArch.API.Authorization;
using CleanArch.Application.Common.Interfaces;
using CleanArch.Application.Features.Auth.Commands.Login;
using CleanArch.Application.Features.Auth.Commands.RegisterUser;
using CleanArch.Application.Features.Auth.DTOs;
using CleanArch.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CleanArch.API.Controllers;

/// <summary>
/// Authentication endpoints: login and user registration.
/// </summary>
[Route("api/v1/auth")]
public class AuthController : BaseApiController
{
    private readonly ICurrentUserService _currentUser;

    public AuthController(ICurrentUserService currentUser)
    {
        _currentUser = currentUser;
    }

    /// <summary>
    /// Authenticates a user and returns a JWT access token.
    /// </summary>
    /// <remarks>
    /// Supply the tenant's GUID in the `X-Tenant-Id` header. This resolves which tenant
    /// to authenticate against — the same email can exist in multiple tenants.
    ///
    /// On success, you receive:
    /// - `accessToken` — JWT, valid for **60 minutes**. Send as `Authorization: Bearer {token}`.
    /// - `refreshToken` — opaque 64-byte token. Use `POST /auth/refresh` to get a new access token.
    ///
    /// After **5 consecutive failures** the account is locked for **30 minutes**.
    /// The same error message is returned whether the email doesn't exist or the password
    /// is wrong — this is intentional (prevents account enumeration).
    /// </remarks>
    /// <param name="request">Email and password.</param>
    /// <param name="cancellationToken">Request cancellation token.</param>
    /// <response code="200">Authentication successful. Body contains tokens and user info.</response>
    /// <response code="400">Invalid credentials or account locked.</response>
    /// <response code="422">Validation failed (missing/invalid fields).</response>
    [HttpPost("login")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Login(
        [FromBody] LoginRequest request,
        [FromHeader(Name = "X-Tenant-Id")] Guid? tenantIdHeader,
        CancellationToken cancellationToken)
    {
        // Tenant can be supplied either via the header or via a tenant-prefixed URL
        // in a future version. For now, the header is the canonical source.
        if (tenantIdHeader is null || tenantIdHeader == Guid.Empty)
        {
            return BadRequest(new ProblemDetails
            {
                Title  = "Tenant not specified",
                Detail = "Supply the tenant identifier in the X-Tenant-Id header.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        var command = new LoginCommand(
            TenantId: tenantIdHeader.Value,
            Email:    request.Email,
            Password: request.Password);

        var result = await Mediator.Send(command, cancellationToken);

        if (!result.Succeeded)
            return BadRequest(new ProblemDetails
            {
                Title  = "Authentication failed",
                Detail = string.Join(" ", result.Errors),
                Status = StatusCodes.Status400BadRequest
            });

        return Ok(result.Data);
    }

    /// <summary>
    /// Registers a new user within the caller's tenant. Requires Admin role.
    /// </summary>
    /// <remarks>
    /// Only users with the **Admin** role can call this endpoint.
    /// The new user is created in the **same tenant** as the calling admin —
    /// tenant selection is not possible through this endpoint.
    ///
    /// **Password rules:**
    /// - Minimum 8 characters, maximum 72 characters
    /// - Must contain uppercase, lowercase, digit, and special character
    ///
    /// **Role assignment:**
    /// - Admin can assign `Viewer` or `Member` to new users
    /// - Admin can create other Admins (to delegate management)
    /// - `SuperAdmin` is never assignable through this endpoint
    ///
    /// Returns tokens for the newly created user. The calling Admin's session is unaffected.
    /// </remarks>
    /// <param name="request">New user details.</param>
    /// <param name="cancellationToken">Request cancellation token.</param>
    /// <response code="201">User created. Body contains tokens for the new user.</response>
    /// <response code="400">Business rule violation (e.g. email already exists).</response>
    /// <response code="401">Not authenticated.</response>
    /// <response code="403">Caller does not have Admin role.</response>
    /// <response code="422">Validation failed.</response>
    [HttpPost("register")]
    [Authorize(Policy = AuthorizationPolicies.RequireAdmin)]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Register(
        [FromBody] RegisterUserRequest request,
        CancellationToken cancellationToken)
    {
        var tenantId = _currentUser.TenantId;
        var callerId = _currentUser.UserId;

        if (tenantId is null || callerId is null)
            return Unauthorized();

        // Parse the requested role — default to Member on unrecognised input.
        if (!Enum.TryParse<UserRole>(request.Role, ignoreCase: true, out var role))
            role = UserRole.Member;

        var command = new RegisterUserCommand(
            TenantId:        tenantId.Value,
            CallerUserId:    callerId.Value,
            CallerUserName:  _currentUser.UserName ?? "Unknown",
            FirstName:       request.FirstName,
            LastName:        request.LastName,
            Email:           request.Email,
            Password:        request.Password,
            Role:            role);

        var result = await Mediator.Send(command, cancellationToken);

        if (!result.Succeeded)
            return BadRequest(new ProblemDetails
            {
                Title  = "Registration failed",
                Detail = string.Join(" ", result.Errors),
                Status = StatusCodes.Status400BadRequest
            });

        // 201 Created — new resource was created.
        // Location header intentionally omitted here: the new user's profile endpoint
        // (GET /users/{id}) is the canonical URL but that query isn't implemented yet.
        return StatusCode(StatusCodes.Status201Created, result.Data);
    }
}
