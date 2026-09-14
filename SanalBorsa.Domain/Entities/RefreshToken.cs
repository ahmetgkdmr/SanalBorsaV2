namespace SanalBorsa.Domain.Entities;

/// <summary>
/// Yenileme token'ının sunucu tarafındaki kaydı.
///
/// <para>
/// Önce tamamen stateless'tı: token sadece imzalıydı, sunucu onu hiç tanımıyordu. Sonuç olarak
/// sızan bir token <c>RefreshTokenDays</c> (30 gün) boyunca geçerli kalıyor ve çıkış yapmak onu
/// iptal etmiyordu — kullanıcı "çıkış yaptım" sansa da token çalışmaya devam ediyordu.
/// </para>
///
/// <para>
/// Token'ın kendisi saklanmaz; içindeki <see cref="Jti"/> (benzersiz token kimliği) saklanır.
/// İmza zaten token'ın sahiciliğini garanti ettiği için kimlik üzerinden iptal kontrolü yeterli,
/// ve veritabanı sızsa bile oradan kullanılabilir bir token üretilemez.
/// </para>
/// </summary>
public class RefreshToken
{
    public long Id { get; set; }

    /// <summary>Token'ın içindeki <c>jti</c> claim'i — arama anahtarı.</summary>
    public Guid Jti { get; set; }

    public Guid UserId { get; set; }

    public User User { get; set; } = null!;

    public DateTime ExpiresAt { get; set; }

    public DateTime CreatedAt { get; set; }

    /// <summary>Dolu ise token iptal edilmiştir (çıkış ya da rotasyon).</summary>
    public DateTime? RevokedAt { get; set; }

    /// <summary>
    /// Rotasyonda bu token'ın yerine geçen yeni token'ın kimliği. Aynı token ikinci kez
    /// kullanılmaya çalışılırsa (çalınmış olabilir) zincirin izlenebilmesini sağlar.
    /// </summary>
    public Guid? ReplacedByJti { get; set; }

    public bool IsActive(DateTime utcNow) => RevokedAt is null && ExpiresAt > utcNow;
}
