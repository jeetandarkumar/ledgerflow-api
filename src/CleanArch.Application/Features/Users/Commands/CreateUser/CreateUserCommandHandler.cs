using CleanArch.Application.Common.Exceptions;
using CleanArch.Application.Common.Models;
using CleanArch.Domain.Entities;
using CleanArch.Domain.Interfaces;
using MediatR;

namespace CleanArch.Application.Features.Users.Commands.CreateUser;

public class CreateUserCommandHandler : IRequestHandler<CreateUserCommand, Result<Guid>>
{
    private readonly IUserRepository _userRepository;

    public CreateUserCommandHandler(IUserRepository userRepository)
    {
        _userRepository = userRepository;
    }

    public async Task<Result<Guid>> Handle(CreateUserCommand request, CancellationToken cancellationToken)
    {
        var emailExists = await _userRepository.EmailExistsAsync( request.Email, cancellationToken);
        if (emailExists)
            return Result<Guid>.Failure("A user with this email already exists.");

        var user = User.Create(request.FirstName, request.LastName, request.Email);
        await _userRepository.AddAsync(user, cancellationToken);

        return Result<Guid>.Success(user.Id);
    }
}
