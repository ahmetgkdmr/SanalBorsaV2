namespace SanalBorsa.Application.Common.Interfaces;

/// <summary>
/// Soyutlanmış Firebase Auth arayüzü.
/// İleride Twilio / kendi OTP sisteminizle değiştirebilirsiniz;
/// sadece bu interface'i implemente eden yeni bir sınıf yazmanız yeterli.
/// </summary>
public interface IFirebaseAuthProvider
{
    /// <summary>
    /// Firebase ID Token'ı doğrular ve içindeki claim'leri döner.
    /// Google ile giriş ve telefon OTP sonrası frontend'den gönderilen token buradan geçer.
    /// </summary>
    Task<FirebaseTokenClaims?> VerifyIdTokenAsync(string idToken, CancellationToken ct = default);

    /// <summary>
    /// Firebase'deki TÜM kullanıcıları siler (toplu, sayfalı). Geri alınamaz — sadece
    /// bilinçli bir bakım/temizlik işlemi için kullanılmalı. Silinen kullanıcı sayısını döner.
    /// </summary>
    Task<int> DeleteAllUsersAsync(CancellationToken ct = default);
}

public record FirebaseTokenClaims(
    string Uid,
    string? Email,
    bool   EmailVerified,
    string? PhoneNumber,
    string? Name,
    string? Picture,
    string  Provider   // "google.com" | "phone" | ...
);
