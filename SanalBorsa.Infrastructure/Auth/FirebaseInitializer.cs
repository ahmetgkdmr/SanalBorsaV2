using FirebaseAdmin;
using Google.Apis.Auth.OAuth2;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace SanalBorsa.Infrastructure.Auth;

/// <summary>
/// Firebase App'i tek seferlik başlatır.
/// Singleton olarak kayıtlıdır; ilk kullanımda çağrılır.
/// </summary>
public sealed class FirebaseInitializer
{
    private readonly IConfiguration _config;
    private readonly IHostEnvironment _env;
    private static readonly object _lock = new();
    private static bool _attempted;
    private static bool _ready;

    public FirebaseInitializer(IConfiguration config, IHostEnvironment env)
    {
        _config = config;
        _env = env;
    }

    public bool IsReady => _ready || FirebaseApp.DefaultInstance is not null;

    public void EnsureInitialized(ILogger? logger = null)
    {
        if (_attempted) return;

        lock (_lock)
        {
            if (_attempted) return;

            try
            {
                if (FirebaseApp.DefaultInstance is null)
                {
                    var credentialPath = ResolveCredentialPath(_config["Firebase:ServiceAccountPath"]);
                    if (credentialPath is not null)
                    {
                        FirebaseApp.Create(new AppOptions
                        {
                            Credential = GoogleCredential.FromFile(credentialPath),
                        });
                        logger?.LogInformation("Firebase Admin SDK başlatıldı: {Path}", credentialPath);
                    }
                    else
                    {
                        // Cloud Run / GCE Application Default Credentials
                        FirebaseApp.Create();
                        logger?.LogInformation("Firebase Admin SDK ADC ile başlatıldı.");
                    }
                }

                _ready = FirebaseApp.DefaultInstance is not null;
            }
            catch (Exception ex)
            {
                _ready = false;
                logger?.LogWarning(
                    ex,
                    "Firebase başlatılamadı. '{File}' dosyasını API proje köküne koyun " +
                    "(Firebase Console → Project settings → Service accounts → Generate new private key). " +
                    "Configured path: {Configured}",
                    "firebase-service-account.json",
                    _config["Firebase:ServiceAccountPath"]);
            }
            finally
            {
                _attempted = true;
            }
        }
    }

    private string? ResolveCredentialPath(string? configured)
    {
        if (string.IsNullOrWhiteSpace(configured)) return null;

        foreach (var candidate in EnumerateCandidates(configured))
        {
            try
            {
                var full = Path.GetFullPath(candidate);
                if (File.Exists(full)) return full;
            }
            catch
            {
                // ignore invalid paths
            }
        }

        return null;
    }

    private IEnumerable<string> EnumerateCandidates(string configured)
    {
        if (Path.IsPathRooted(configured))
            yield return configured;

        yield return Path.Combine(_env.ContentRootPath, configured);
        yield return Path.Combine(Directory.GetCurrentDirectory(), configured);
        yield return Path.Combine(AppContext.BaseDirectory, configured);

        // bin/Debug/net8.0 → proje kökü
        yield return Path.Combine(AppContext.BaseDirectory, "..", "..", "..", configured);
        yield return Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", configured);
    }
}
