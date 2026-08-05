using FirebaseAdmin;
using FirebaseAdmin.Auth;
using Microsoft.Extensions.Logging;
using SanalBorsa.Application.Common.Interfaces;

namespace SanalBorsa.Infrastructure.Auth;

/// <summary>
/// Firebase Admin SDK kullanarak ID Token doğrular.
/// İleride başka bir auth sağlayıcısı ile değiştirmek istersen sadece
/// <see cref="IFirebaseAuthProvider"/> arayüzünü implement eden yeni bir sınıf yaz
/// ve DI kaydını güncelle — başka hiçbir şeyi değiştirmene gerek yok.
/// </summary>
public class FirebaseAuthProvider : IFirebaseAuthProvider
{
    private readonly FirebaseInitializer _initializer;
    private readonly ILogger<FirebaseAuthProvider> _logger;

    public FirebaseAuthProvider(
        FirebaseInitializer initializer,
        ILogger<FirebaseAuthProvider> logger)
    {
        _initializer = initializer;
        _logger = logger;
    }

    public async Task<FirebaseTokenClaims?> VerifyIdTokenAsync(
        string idToken,
        CancellationToken ct = default)
    {
        _initializer.EnsureInitialized(_logger);

        if (!_initializer.IsReady || FirebaseApp.DefaultInstance is null)
        {
            throw new InvalidOperationException(
                "Sunucu Firebase kimlik doğrulaması yapılandırılmamış. " +
                "Geliştirici: firebase-service-account.json dosyasını API proje köküne ekleyip uygulamayı yeniden başlatın.");
        }

        try
        {
            var decoded = await FirebaseAuth.DefaultInstance.VerifyIdTokenAsync(idToken, ct);

            // Firebase SDK v3: sign-in provider is in claims under "firebase" > "sign_in_provider"
            var provider = "unknown";
            if (decoded.Claims.TryGetValue("firebase", out var fbClaim)
                && fbClaim is System.Collections.Generic.Dictionary<string, object> fbDict
                && fbDict.TryGetValue("sign_in_provider", out var sipObj))
            {
                provider = sipObj?.ToString() ?? "unknown";
            }

            decoded.Claims.TryGetValue("name",         out var name);
            decoded.Claims.TryGetValue("picture",      out var picture);
            decoded.Claims.TryGetValue("email",        out var email);
            decoded.Claims.TryGetValue("email_verified", out var emailVerified);
            decoded.Claims.TryGetValue("phone_number", out var phone);

            return new FirebaseTokenClaims(
                Uid:           decoded.Uid,
                Email:         email?.ToString(),
                EmailVerified: emailVerified?.ToString()?.Equals("True", StringComparison.OrdinalIgnoreCase) == true,
                PhoneNumber:   phone?.ToString(),
                Name:          name?.ToString(),
                Picture:       picture?.ToString(),
                Provider:      provider);
        }
        catch (FirebaseAuthException ex)
        {
            _logger.LogWarning("Firebase token verification failed: {Message}", ex.Message);
            return null;
        }
    }

    public async Task<int> DeleteAllUsersAsync(CancellationToken ct = default)
    {
        _initializer.EnsureInitialized(_logger);

        if (!_initializer.IsReady || FirebaseApp.DefaultInstance is null)
        {
            throw new InvalidOperationException(
                "Sunucu Firebase kimlik doğrulaması yapılandırılmamış.");
        }

        var uids = new List<string>();
        await foreach (var user in FirebaseAuth.DefaultInstance.ListUsersAsync(null).WithCancellation(ct))
            uids.Add(user.Uid);

        var deleted = 0;
        foreach (var batch in uids.Chunk(1000))
        {
            var result = await FirebaseAuth.DefaultInstance.DeleteUsersAsync(batch, ct);
            deleted += result.SuccessCount;
            if (result.FailureCount > 0)
            {
                foreach (var err in result.Errors)
                    _logger.LogWarning(
                        "Firebase kullanıcı silme hatası (index {Index}): {Reason}",
                        err.Index, err.Reason);
            }
        }

        _logger.LogInformation("Firebase: {Count} kullanıcı silindi.", deleted);
        return deleted;
    }
}
