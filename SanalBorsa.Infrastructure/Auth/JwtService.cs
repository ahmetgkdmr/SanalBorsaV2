using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using SanalBorsa.Application.Common.Interfaces;
using SanalBorsa.Domain.Entities;

namespace SanalBorsa.Infrastructure.Auth;

public class JwtService : IJwtService
{
    private readonly string _secret;
    private readonly string _issuer;
    private readonly string _audience;
    private readonly int    _accessMinutes;
    private readonly int    _refreshDays;

    public JwtService(IConfiguration config)
    {
        var s = config.GetSection("Jwt");
        _secret        = s["Secret"]    ?? throw new InvalidOperationException("Jwt:Secret is missing.");
        _issuer        = s["Issuer"]    ?? "SanalBorsa";
        _audience      = s["Audience"]  ?? "SanalBorsa";
        _accessMinutes = int.TryParse(s["AccessTokenMinutes"], out var am) ? am : 60;
        _refreshDays   = int.TryParse(s["RefreshTokenDays"],   out var rd) ? rd : 30;
    }

    public TokenPair Generate(User user)
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

        var accessToken  = new JwtSecurityTokenHandler().WriteToken(token);
        var refreshToken = GenerateRefreshToken(user.Id);

        return new TokenPair(accessToken, refreshToken, expires);
    }

    public Guid? ValidateRefreshToken(string refreshToken)
    {
        try
        {
            var handler    = new JwtSecurityTokenHandler();
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
            return Guid.TryParse(sub, out var id) ? id : null;
        }
        catch
        {
            return null;
        }
    }

    private string GenerateRefreshToken(Guid userId)
    {
        var key    = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_secret + "_refresh"));
        var creds  = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var claims = new[] { new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()) };

        var token = new JwtSecurityToken(
            claims:             claims,
            expires:            DateTime.UtcNow.AddDays(_refreshDays),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
