using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace SanalBorsa.API.Security;

/// <summary>
/// Operasyonel (sync / bootstrap / compute / audit / rename) endpoint'lerini korur.
/// Bunlar kullanıcı arayüzünden HİÇ çağrılmaz; saatler sürebilen, dış servisleri yoran ve
/// veriyi değiştiren bakım işleridir — anonim erişime açık kalmaları hem DoS hem veri bozma
/// riski demekti.
///
/// <para>
/// Doğrulama <c>X-Admin-Key</c> başlığının <c>Admin:ApiKey</c> ayarıyla eşleşmesine dayanır.
/// Anahtar <b>tanımlı değilse</b> davranış ortama göre değişir:
/// geliştirmede serbest bırakılır (yerel akış bozulmasın), üretimde reddedilir — yani
/// yapılandırma unutulduğunda güvenli tarafa düşer, sessizce açık kalmaz.
/// </para>
///
/// <para>
/// Hangfire'ın zamanlanmış işleri bu filtreden etkilenmez: onlar HTTP üzerinden değil,
/// doğrudan MediatR handler'ları çağırarak çalışır (bkz. <c>RecurringJobRegistrar</c>).
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public sealed class AdminApiKeyAttribute : Attribute, IAsyncAuthorizationFilter
{
    private const string HeaderName = "X-Admin-Key";

    public Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var services = context.HttpContext.RequestServices;
        var config = services.GetRequiredService<IConfiguration>();
        var env = services.GetRequiredService<IWebHostEnvironment>();

        var expected = config["Admin:ApiKey"];

        if (string.IsNullOrWhiteSpace(expected))
        {
            // Üretimde anahtarsız = kapalı. Geliştirmede engellemiyoruz.
            if (env.IsProduction())
            {
                context.Result = new ObjectResult(new
                {
                    message = "Yönetim endpoint'i yapılandırılmamış. 'Admin:ApiKey' ayarlanmalı.",
                })
                {
                    StatusCode = StatusCodes.Status503ServiceUnavailable,
                };
            }

            return Task.CompletedTask;
        }

        var provided = context.HttpContext.Request.Headers[HeaderName].ToString();

        if (string.IsNullOrEmpty(provided) || !FixedTimeEquals(provided, expected))
        {
            context.Result = new UnauthorizedObjectResult(new
            {
                message = $"Geçersiz veya eksik {HeaderName}.",
            });
        }

        return Task.CompletedTask;
    }

    /// <summary>Uzunluk farkında bile erken dönmemek için sabit zamanlı karşılaştırma.</summary>
    private static bool FixedTimeEquals(string a, string b)
        => CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(a),
            Encoding.UTF8.GetBytes(b));
}
