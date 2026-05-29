using ledgerflowApi.Application.Common.Models;
using ledgerflowApi.Domain.Interfaces;
using MediatR;

namespace ledgerflowApi.Application.Features.Users.Queries.GetUser;

public sealed class GetUserQueryHandler : IRequestHandler<GetUserQuery, Result<UserDto>>
{
    private readonly IUserRepository _userRepository;

    public GetUserQueryHandler(IUserRepository userRepository)
    {
        _userRepository = userRepository;
    }

    public async Task<Result<UserDto>> Handle(GetUserQuery request, CancellationToken cancellationToken)
    {
        var user = await _userRepository.GetByIdAsync(request.Id, cancellationToken);

        // Tenant-scope check: if the user exists but belongs to a different tenant,
        // return the same 404 response as "not found" — never leak cross-tenant existence.
        if (user is null || user.TenantId != request.TenantId)
            return Result<UserDto>.Failure($"User with ID '{request.Id}' was not found.");

        var dto = new UserDto(
            Id:        user.Id,
            FirstName: user.FirstName,
            LastName:  user.LastName,
            Email:     user.Email,
            Role:      user.Role,
            IsActive:  user.IsActive,
            CreatedAt: user.CreatedAt);

        return Result<UserDto>.Success(dto);
    }
}
