using ledgerflowApi.Domain.Enums;

namespace ledgerflowApi.Application.Features.Users.Queries.GetUser;

public record UserDto(
    Guid Id,
    string FirstName,
    string LastName,
    string Email,
    UserRole Role,
    bool IsActive,
    DateTime CreatedAt);
