using FirebaseAdmin;
using Google.Apis.Auth.OAuth2;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace SanalBorsa.Infrastructure.Auth;

/// <summary>
/// Firebase App'i tek seferlik başlatır.
/// Singleton olarak kayıtlıdır; ilk kullanımda çağrılır.
/// </summary>
public sealed class FirebaseInitializer
{
    private readonly IConfiguration _config;
    private static readonly object _lock = new();
    private static bool _initialized;

    public FirebaseInitializer(IConfiguration config)
    {
        _config = config;
    }

    public void EnsureInitialized(ILogger? logger = null)
    {
        if (_initialized) return;

        lock (_lock)
        {
            if (_initialized) return;

            try
            {
                if (FirebaseApp.DefaultInstance is null)
                {
                    var credentialPath = _config["Firebase:ServiceAccountPath"];
                    if (!string.IsNullOrWhiteSpace(credentialPath) && File.Exists(credentialPath))
                    {
                        FirebaseApp.Create(new AppOptions
                        {
                            Credential = GoogleCredential.FromFile(credentialPath),
                        });
                    }
                    else
                    {
                        // Application Default Credentials (Cloud ortamı)
                        FirebaseApp.Create();
                    }
                }

                _initialized = true;
            }
            catch (Exception ex)
            {
                logger?.LogWarning("Firebase başlatılamadı: {Message}", ex.Message);
            }
        }
    }
}
