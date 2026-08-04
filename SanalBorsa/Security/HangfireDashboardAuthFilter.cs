using System.Security.Cryptography;
using System.Text;
using Hangfire.Dashboard;

namespace SanalBorsa.API.Security;

/// <summary>
/// Hangfire dashboard'u (/hangfire) herkese açık bırakmamak için HTTP Basic Auth.
/// Kimlik bilgileri config'ten okunur (Hangfire:DashboardUser / Hangfire:DashboardPassword) —
/// appsettings.Production.json'a gerçek değer YAZILMAZ, Render ortam değişkeni olarak set edilir.
/// </summary>
public sealed class HangfireDashboardAuthFilter : IDashboardAuthorizationFilter
{
    private readonly string _user;
    private readonly string _password;

    public HangfireDashboardAuthFilter(IConfiguration config)
    {
        // Hangfire:DashboardUser — appsettings'ten oku, yoksa ortam değişkeninden (Hangfire__DashboardUser)
        _user = config["Hangfire:DashboardUser"]
            ?? Environment.GetEnvironmentVariable("Hangfire__DashboardUser")
            ?? throw new InvalidOperationException(
                "Hangfire:DashboardUser eksik. Appsettings'te 'Hangfire:DashboardUser' veya ortam değişkeni 'Hangfire__DashboardUser' set edin.");

        // Hangfire:DashboardPassword — appsettings'ten oku, yoksa ortam değişkeninden (Hangfire__DashboardPassword)
        _password = config["Hangfire:DashboardPassword"]
            ?? Environment.GetEnvironmentVariable("Hangfire__DashboardPassword")
            ?? throw new InvalidOperationException(
                "Hangfire:DashboardPassword eksik. Appsettings'te 'Hangfire:DashboardPassword' veya ortam değişkeni 'Hangfire__DashboardPassword' set edin.");
    }

    public bool Authorize(DashboardContext context)
    {
        var httpContext = context.GetHttpContext();
        var header = httpContext.Request.Headers.Authorization.ToString();

        if (!header.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
        {
            Challenge(httpContext);
            return false;
        }

        try
        {
            var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(header["Basic ".Length..].Trim()));
            var sep = decoded.IndexOf(':');
            if (sep < 0)
            {
                Challenge(httpContext);
                return false;
            }

            var user = decoded[..sep];
            var pass = decoded[(sep + 1)..];
            var ok = FixedTimeEquals(user, _user) && FixedTimeEquals(pass, _password);
            if (!ok) Challenge(httpContext);
            return ok;
        }
        catch
        {
            Challenge(httpContext);
            return false;
        }
    }

    private static bool FixedTimeEquals(string a, string b)
        => CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));

    private static void Challenge(HttpContext httpContext)
    {
        httpContext.Response.Headers.WWWAuthenticate = "Basic realm=\"Hangfire\"";
        httpContext.Response.StatusCode = StatusCodes.Status401Unauthorized;
    }
}
