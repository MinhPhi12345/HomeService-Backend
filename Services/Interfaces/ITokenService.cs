using HomeService.Api.Models;

namespace HomeService.Api.Services.Interfaces;

public interface ITokenService
{
    (string token, DateTime expiresAt) GenerateAccessToken(User user);
}
