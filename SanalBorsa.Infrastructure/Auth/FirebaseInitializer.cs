using System.Text;
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
                    if (TryCreateFromJson(_config["Firebase:ServiceAccountJson"], logger)
                        || TryCreateFromJson(_config["FIREBASE_SERVICE_ACCOUNT_JSON"], logger)
                        || TryCreateFromFile(_config["Firebase:ServiceAccountPath"], logger))
                    {
                        // ok
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
                    "Firebase başlatılamadı. Render'da FIREBASE_SERVICE_ACCOUNT_JSON env, " +
                    "veya API kökünde firebase-service-account.json gerekli. " +
                    "(Firebase Console → Project settings → Service accounts → Generate new private key). " +
                    "Configured path: {Configured}",
                    _config["Firebase:ServiceAccountPath"]);
            }
            finally
            {
                _attempted = true;
            }
        }
    }

    private bool TryCreateFromJson(string? json, ILogger? logger)
    {
        if (string.IsNullOrWhiteSpace(json)) return false;

        var trimmed = json.Trim();
        // Render bazen Base64 ile saklar
        if (!trimmed.StartsWith('{'))
        {
            try
            {
                trimmed = Encoding.UTF8.GetString(Convert.FromBase64String(trimmed));
            }
            catch
            {
                return false;
            }
        }

        FirebaseApp.Create(new AppOptions
        {
            Credential = GoogleCredential.FromJson(trimmed),
        });
        logger?.LogInformation("Firebase Admin SDK JSON credential ile başlatıldı (env/config).");
        return true;
    }

    private bool TryCreateFromFile(string? configuredPath, ILogger? logger)
    {
        var credentialPath = ResolveCredentialPath(configuredPath);
        if (credentialPath is null) return false;

        FirebaseApp.Create(new AppOptions
        {
            Credential = GoogleCredential.FromFile(credentialPath),
        });
        logger?.LogInformation("Firebase Admin SDK başlatıldı: {Path}", credentialPath);
        return true;
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
