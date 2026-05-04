using CleanArch.API.Authorization;
using CleanArch.API.Extensions;
using CleanArch.Application.Common.Interfaces;
using CleanArch.Application.Features.Auth.Commands.Login;
using CleanArch.Application.Features.Auth.Commands.RegisterUser;
using CleanArch.Application.Features.Auth.DTOs;
using CleanArch.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace CleanArch.API.Controllers;

/// <summary>Authentication: login and user registration.</summary>
[Route("api/v1/auth")]
public class AuthController : BaseApiController
{
    private readonly ICurrentUserService _currentUser;

    public AuthController(ICurrentUserService currentUser)
    {
        _currentUser = currentUser;
    }

    /// <summary>Authenticates a user and returns a JWT access token.</summary>
    /// <remarks>
    /// Supply the tenant GUID in the `X-Tenant-Id` header.
    ///
    /// On success returns `accessToken` (valid 60 min) and `refreshToken`.
    /// After 5 consecutive failures the account is locked for 30 minutes.
    /// </remarks>
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitingExtensions.AuthPolicy)]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Login(
        [FromBody] LoginRequest request,
        [FromHeader(Name = "X-Tenant-Id")] Guid? tenantIdHeader,
        CancellationToken cancellationToken)
    {
        if (tenantIdHeader is null || tenantIdHeader == Guid.Empty)
            return BadRequest(new ProblemDetails
            {
                Title = "Tenant not specified",
                Detail = "Supply the tenant identifier in the X-Tenant-Id header.",
                Status = StatusCodes.Status400BadRequest
            });

        var result = await Mediator.Send(
            new LoginCommand(tenantIdHeader.Value, request.Email, request.Password),
            cancellationToken);

        if (!result.Succeeded)
            return BadRequest(new ProblemDetails
            {
                Title = "Authentication failed",
                Detail = string.Join(" ", result.Errors),
                Status = StatusCodes.Status400BadRequest
            });

        return Ok(result.Data);
    }

    /// <summary>Registers a new user within the caller's tenant. Requires Admin role.</summary>
    /// <remarks>
    /// The new user is created in the **same tenant** as the calling admin.
    ///
    /// Password rules: min 8 chars, max 72, must contain uppercase, lowercase, digit, special char.
    ///
    /// Valid roles: `Viewer`, `Member`, `Admin`. `SuperAdmin` is never assignable here.
    /// </remarks>
    [HttpPost("register")]
    [Authorize(Policy = AuthorizationPolicies.RequireAdmin)]
    [EnableRateLimiting(RateLimitingExtensions.StrictPolicy)]
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
        if (tenantId is null || callerId is null) return Unauthorized();

        if (!Enum.TryParse<UserRole>(request.Role, ignoreCase: true, out var role))
            role = UserRole.Member;

        var command = new RegisterUserCommand(
            TenantId: tenantId.Value,
            CallerUserId: callerId.Value,
            CallerUserName: _currentUser.UserName ?? "Unknown",
            FirstName: request.FirstName,
            LastName: request.LastName,
            Email: request.Email,
            Password: request.Password,
            Role: role);

        var result = await Mediator.Send(command, cancellationToken);

        if (!result.Succeeded)
            return BadRequest(new ProblemDetails
            {
                Title = "Registration failed",
                Detail = string.Join(" ", result.Errors),
                Status = StatusCodes.Status400BadRequest
            });

        return StatusCode(StatusCodes.Status201Created, result.Data);
    }
}
