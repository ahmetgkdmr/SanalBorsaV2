using SanalBorsa.Domain.Entities;

namespace SanalBorsa.Application.Common.Interfaces;

public interface IJwtService
{
    TokenPair Generate(User user);

    /// <summary>Refresh token'ı doğrular ve içindeki UserId'yi döner.</summary>
    Guid? ValidateRefreshToken(string refreshToken);
}

public record TokenPair(string AccessToken, string RefreshToken, DateTime ExpiresAt);
