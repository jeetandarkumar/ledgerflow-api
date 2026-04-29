using CleanArch.Application.Common.Models;
using MediatR;

namespace CleanArch.Application.Features.Users.Commands.CreateUser;

public record CreateUserCommand(
    string FirstName,
    string LastName,
    string Email) : IRequest<Result<Guid>>;
