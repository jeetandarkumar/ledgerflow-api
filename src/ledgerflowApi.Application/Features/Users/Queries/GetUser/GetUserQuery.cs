using ledgerflowApi.Application.Common.Models;
using MediatR;

namespace ledgerflowApi.Application.Features.Users.Queries.GetUser;

/// <summary>
/// Returns a user by ID, scoped to the caller's tenant.
/// TenantId is required to prevent cross-tenant data leakage —
/// the handler rejects the request if the user belongs to a different tenant.
/// </summary>
public record GetUserQuery(Guid Id, Guid TenantId) : IRequest<Result<UserDto>>;
