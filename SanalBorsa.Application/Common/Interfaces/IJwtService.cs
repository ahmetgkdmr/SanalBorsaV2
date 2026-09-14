using SanalBorsa.Domain.Entities;

namespace SanalBorsa.Application.Common.Interfaces;

public interface IJwtService
{
    /// <summary>
    /// Erişim + yenileme token'ı üretir ve yenileme token'ını sunucuda kaydeder.
    /// Kayıt olmadan iptal mümkün değildi (bkz. <see cref="RefreshToken"/>), bu yüzden
    /// metot artık asenkron.
    /// </summary>
    Task<TokenPair> GenerateAsync(User user, CancellationToken ct = default);

    /// <summary>
    /// Yenileme token'ını doğrular: imza geçerli mi, süresi dolmuş mu, İPTAL EDİLMİŞ Mİ.
    /// Geçerliyse kullanıcı kimliğini ve token kimliğini döner.
    /// </summary>
    Task<RefreshTokenValidation?> ValidateRefreshTokenAsync(
        string refreshToken, CancellationToken ct = default);

    /// <summary>Tek bir yenileme token'ını iptal eder ve yerine geçeni işaretler (rotasyon).</summary>
    Task RevokeAsync(Guid jti, Guid? replacedByJti = null, CancellationToken ct = default);

    /// <summary>Kullanıcının tüm aktif yenileme token'larını iptal eder — çıkış.</summary>
    Task RevokeAllForUserAsync(Guid userId, CancellationToken ct = default);
}

public record TokenPair(string AccessToken, string RefreshToken, DateTime ExpiresAt);

public record RefreshTokenValidation(Guid UserId, Guid Jti);
