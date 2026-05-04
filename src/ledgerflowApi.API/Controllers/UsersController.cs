using ledgerflowApi.API.Authorization;
using ledgerflowApi.Application.Common.Interfaces;
using ledgerflowApi.Application.Features.Users.Queries.GetUser;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ledgerflowApi.API.Controllers;

/// <summary>
/// User management within the caller's tenant.
/// All operations are tenant-scoped via the JWT — no cross-tenant access possible.
/// </summary>
[Authorize]
public class UsersController : BaseApiController
{
    private readonly ICurrentUserService _currentUser;

    public UsersController(ICurrentUserService currentUser)
    {
        _currentUser = currentUser;
    }

    /// <summary>
    /// Gets a user profile by ID, scoped to the caller's tenant.
    /// Returns 404 if the user doesn't exist OR belongs to a different tenant.
    /// </summary>
    [HttpGet("{id:guid}")]
    [Authorize(Policy = AuthorizationPolicies.RequireAuthenticated)]
    [ProducesResponseType(typeof(UserDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetUser(Guid id, CancellationToken cancellationToken)
    {
        var tenantId = _currentUser.TenantId;
        if (tenantId is null) return Unauthorized();

        var result = await Mediator.Send(new GetUserQuery(id, tenantId.Value), cancellationToken);

        if (!result.Succeeded)
            return NotFound(new ProblemDetails
            {
                Title  = "User not found",
                Detail = result.Errors.FirstOrDefault(),
                Status = StatusCodes.Status404NotFound
            });

        return Ok(result.Data);
    }
}
