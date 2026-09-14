using SanalBorsa.Domain.Entities;

namespace SanalBorsa.Domain.Interfaces.Repositories;

public interface IRefreshTokenRepository : IRepository<RefreshToken>
{
    /// <summary>Token kimliğiyle kaydı bulur (iptal/son kullanma kontrolü için).</summary>
    Task<RefreshToken?> GetByJtiAsync(Guid jti, CancellationToken ct = default);

    /// <summary>Kullanıcının tüm aktif token'larını iptal eder — çıkışta çağrılır.</summary>
    Task<int> RevokeAllForUserAsync(Guid userId, DateTime revokedAt, CancellationToken ct = default);

    /// <summary>Süresi geçmiş kayıtları siler (tablo sınırsız büyümesin).</summary>
    Task<int> DeleteExpiredAsync(DateTime olderThanUtc, CancellationToken ct = default);
}
