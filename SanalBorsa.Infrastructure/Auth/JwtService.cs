using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using SanalBorsa.Application.Common.Interfaces;
using SanalBorsa.Domain.Entities;
using SanalBorsa.Domain.Interfaces;

namespace SanalBorsa.Infrastructure.Auth;

public class JwtService : IJwtService
{
    private readonly string _secret;
    private readonly string _issuer;
    private readonly string _audience;
    private readonly int    _accessMinutes;
    private readonly int    _refreshDays;

    private readonly IUnitOfWork _uow;
    private readonly ILogger<JwtService> _logger;

    public JwtService(IConfiguration config, IUnitOfWork uow, ILogger<JwtService> logger)
    {
        var s = config.GetSection("Jwt");
        _secret        = s["Secret"]    ?? throw new InvalidOperationException("Jwt:Secret is missing.");
        _issuer        = s["Issuer"]    ?? "SanalBorsa";
        _audience      = s["Audience"]  ?? "SanalBorsa";
        _accessMinutes = int.TryParse(s["AccessTokenMinutes"], out var am) ? am : 60;
        _refreshDays   = int.TryParse(s["RefreshTokenDays"],   out var rd) ? rd : 30;

        _uow = uow;
        _logger = logger;
    }

    public async Task<TokenPair> GenerateAsync(User user, CancellationToken ct = default)
    {
        var key     = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_secret));
        var creds   = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var expires = DateTime.UtcNow.AddMinutes(_accessMinutes);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new Claim("displayName",               user.DisplayName),
            new Claim("provider",                  user.Provider.ToString().ToLowerInvariant()),
        };

        var token = new JwtSecurityToken(
            issuer:             _issuer,
            audience:           _audience,
            claims:             claims,
            expires:            expires,
            signingCredentials: creds);

        var accessToken = new JwtSecurityTokenHandler().WriteToken(token);

        // Yenileme token'ı: her üretimde yeni bir jti alır ve o jti sunucuda kaydedilir.
        // Kayıt olmadan "bu token iptal edilmiş mi" sorusu cevaplanamaz.
        var refreshJti = Guid.NewGuid();
        var refreshExpires = DateTime.UtcNow.AddDays(_refreshDays);
        var refreshToken = BuildRefreshToken(user.Id, refreshJti, refreshExpires);

        await _uow.RefreshTokens.AddAsync(new RefreshToken
        {
            Jti       = refreshJti,
            UserId    = user.Id,
            ExpiresAt = refreshExpires,
            CreatedAt = DateTime.UtcNow,
        }, ct);
        await _uow.SaveChangesAsync(ct);

        return new TokenPair(accessToken, refreshToken, expires);
    }

    public async Task<RefreshTokenValidation?> ValidateRefreshTokenAsync(
        string refreshToken, CancellationToken ct = default)
    {
        Guid userId, jti;

        try
        {
            // MapInboundClaims = false ŞART: varsayılan davranışta JwtSecurityTokenHandler,
            // "sub" claim'ini ClaimTypes.NameIdentifier'a (uzun bir XML şema URI'si) eşliyor ve
            // FindFirst("sub") null dönüyor. Eşleme kapatılmazsa token geçerli olsa bile
            // kullanıcı kimliği okunamıyor, yenileme sessizce başarısız oluyordu.
            var handler    = new JwtSecurityTokenHandler { MapInboundClaims = false };
            var key        = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_secret + "_refresh"));
            var validation = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey         = key,
                ValidateIssuer           = false,
                ValidateAudience         = false,
                ClockSkew                = TimeSpan.Zero,
            };

            var principal = handler.ValidateToken(refreshToken, validation, out _);

            var sub = principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
            var jtiRaw = principal.FindFirst(JwtRegisteredClaimNames.Jti)?.Value;

            if (!Guid.TryParse(sub, out userId) || !Guid.TryParse(jtiRaw, out jti))
                return null;
        }
        catch
        {
            // İmza geçersiz / süresi dolmuş / biçim bozuk — hepsi aynı sonuç.
            return null;
        }

        // İmza geçerli olsa bile sunucu kaydı son sözü söyler: iptal edilmiş ya da hiç
        // tanınmayan bir token kabul edilmez.
        var stored = await _uow.RefreshTokens.GetByJtiAsync(jti, ct);
        if (stored is null)
        {
            _logger.LogWarning("Bilinmeyen yenileme token'ı reddedildi (jti {Jti}).", jti);
            return null;
        }

        if (!stored.IsActive(DateTime.UtcNow))
        {
            _logger.LogWarning(
                "İptal edilmiş/süresi geçmiş yenileme token'ı kullanılmaya çalışıldı (jti {Jti}, kullanıcı {UserId}).",
                jti, stored.UserId);
            return null;
        }

        return new RefreshTokenValidation(stored.UserId, stored.Jti);
    }

    public async Task RevokeAsync(Guid jti, Guid? replacedByJti = null, CancellationToken ct = default)
    {
        var stored = await _uow.RefreshTokens.GetByJtiAsync(jti, ct);
        if (stored is null || stored.RevokedAt is not null) return;

        stored.RevokedAt = DateTime.UtcNow;
        stored.ReplacedByJti = replacedByJti;
        await _uow.SaveChangesAsync(ct);
    }

    public async Task RevokeAllForUserAsync(Guid userId, CancellationToken ct = default)
    {
        var count = await _uow.RefreshTokens.RevokeAllForUserAsync(userId, DateTime.UtcNow, ct);
        if (count > 0)
            _logger.LogInformation("{Count} yenileme token'ı iptal edildi (kullanıcı {UserId}).", count, userId);
    }

    private string BuildRefreshToken(Guid userId, Guid jti, DateTime expires)
    {
        var key    = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_secret + "_refresh"));
        var creds  = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim(JwtRegisteredClaimNames.Jti, jti.ToString()),
        };

        var token = new JwtSecurityToken(
            claims:             claims,
            expires:            expires,
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
