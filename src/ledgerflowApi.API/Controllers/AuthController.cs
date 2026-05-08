using ledgerflowApi.API.Authorization;
using ledgerflowApi.API.Extensions;
using ledgerflowApi.Application.Common.Interfaces;
using ledgerflowApi.Application.Features.Auth.Commands.Login;
using ledgerflowApi.Application.Features.Auth.Commands.RegisterUser;
using ledgerflowApi.Application.Features.Auth.DTOs;
using ledgerflowApi.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ledgerflowApi.API.Controllers;

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
    /// Returns an access token (valid 60 min) and a refresh token.
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
    /// Creates the user in the same tenant as the calling admin.
    /// Password rules: min 8 chars, max 72, must include uppercase, lowercase, digit, and special character.
    /// Valid roles: `Viewer`, `Member`, `Admin`. `SuperAdmin` cannot be assigned here.
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
        if (_currentUser.TenantId is not { } tenantId || _currentUser.UserId is not { } callerId)
            return Unauthorized();

        if (!Enum.TryParse<UserRole>(request.Role, ignoreCase: true, out var role))
            role = UserRole.Member;

        var command = new RegisterUserCommand(
            TenantId: tenantId,
            CallerUserId: callerId,
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
