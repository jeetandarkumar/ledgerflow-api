using ledgerflowApi.Domain.Entities;

namespace ledgerflowApi.Application.Common.Interfaces;

public interface ITokenService
{
    string GenerateAccessToken(User user);
    string GenerateRefreshToken();
    Guid? ValidateToken(string token);
}
