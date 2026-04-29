using CleanArch.Application.Common.Models;
using MediatR;

namespace CleanArch.Application.Features.Users.Queries.GetUser;

public record GetUserQuery(Guid Id, Guid TenantId) : IRequest<Result<UserDto>>;
