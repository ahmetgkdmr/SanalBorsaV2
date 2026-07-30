using System.Text.RegularExpressions;
using MediatR;
using Microsoft.Extensions.Logging;
using SanalBorsa.Application.Common.Interfaces;
using SanalBorsa.Domain.Entities;
using SanalBorsa.Domain.Interfaces;

namespace SanalBorsa.Application.Auth.Commands.LoginWithFirebase;

/// <summary>
/// Firebase ID Token ile giriş.
/// Yeni kullanıcıda kayıt tamamlanmadıysa NeedsProfile=true döner (otomatik oluşturmaz).
/// </summary>
public record LoginWithFirebaseCommand(string IdToken) : IRequest<AuthExchangeResult>;

public record AuthExchangeResult(
    bool NeedsProfile,
    string? AccessToken,
    string? RefreshToken,
    DateTime? ExpiresAt,
    UserDto? User,
    ProfileSetupHint? ProfileHint);

public record ProfileSetupHint(
    string? Email,
    string? SuggestedDisplayName,
    string? AvatarUrl,
    string SuggestedUsername);

public record LoginResult(
    string AccessToken,
    string RefreshToken,
    DateTime ExpiresAt,
    UserDto User);

public record UserDto(
    Guid Id,
    string Username,
    string DisplayName,
    string? Email,
    string? PhoneNumber,
    string? AvatarUrl,
    string Provider,
    decimal PortfolioCashTry,
    decimal PortfolioCashUsd,
    bool ShowTradeHistoryPublic = true);

public class LoginWithFirebaseCommandHandler
    : IRequestHandler<LoginWithFirebaseCommand, AuthExchangeResult>
{
    private static readonly Regex UsernameSanitizeRx = new(
        @"[^a-zA-Z0-9_]",
        RegexOptions.Compiled);

    private readonly IUnitOfWork _uow;
    private readonly IFirebaseAuthProvider _firebase;
    private readonly IJwtService _jwt;
    private readonly ILogger<LoginWithFirebaseCommandHandler> _logger;

    public LoginWithFirebaseCommandHandler(
        IUnitOfWork uow,
        IFirebaseAuthProvider firebase,
        IJwtService jwt,
        ILogger<LoginWithFirebaseCommandHandler> logger)
    {
        _uow = uow;
        _firebase = firebase;
        _jwt = jwt;
        _logger = logger;
    }

    public async Task<AuthExchangeResult> Handle(
        LoginWithFirebaseCommand request,
        CancellationToken cancellationToken)
    {
        var claims = await _firebase.VerifyIdTokenAsync(request.IdToken, cancellationToken)
            ?? throw new UnauthorizedAccessException("Firebase token geçersiz veya süresi dolmuş.");

        var user = await _uow.Users.GetByFirebaseUidAsync(claims.Uid, cancellationToken);

        if (user is null)
        {
            var suggested = await SuggestUsernameAsync(claims, cancellationToken);
            return new AuthExchangeResult(
                NeedsProfile: true,
                AccessToken: null,
                RefreshToken: null,
                ExpiresAt: null,
                User: null,
                ProfileHint: new ProfileSetupHint(
                    claims.Email,
                    claims.Name,
                    claims.Picture,
                    suggested));
        }

        // Mevcut kullanıcı: DisplayName'i Google ile ezme (kullanıcı seçimini koru)
        if (string.IsNullOrWhiteSpace(user.AvatarUrl) && !string.IsNullOrWhiteSpace(claims.Picture))
            user.AvatarUrl = claims.Picture;
        user.UpdatedAt = DateTime.UtcNow;
        _uow.Users.Update(user);
        await _uow.SaveChangesAsync(cancellationToken);

        var portfolio = await _uow.Portfolios.GetByUserIdAsync(user.Id, cancellationToken);
        var tokens = _jwt.Generate(user);

        return new AuthExchangeResult(
            NeedsProfile: false,
            tokens.AccessToken,
            tokens.RefreshToken,
            tokens.ExpiresAt,
            new UserDto(
                user.Id,
                user.Username,
                string.IsNullOrWhiteSpace(user.DisplayName) ? user.Username : user.DisplayName,
                user.Email,
                user.PhoneNumber,
                user.AvatarUrl,
                claims.Provider,
                portfolio?.Cash ?? 1_000_000m,
                portfolio?.CashUsd ?? 100_000m,
                user.ShowTradeHistoryPublic),
            ProfileHint: null);
    }

    private async Task<string> SuggestUsernameAsync(
        FirebaseTokenClaims claims,
        CancellationToken ct)
    {
        var seed = claims.Email?.Split('@')[0]
                   ?? claims.Name
                   ?? ("user" + claims.Uid[..Math.Min(6, claims.Uid.Length)]);

        var baseName = UsernameSanitizeRx.Replace(seed, "").ToLowerInvariant();
        if (baseName.Length < 3)
            baseName = "user" + claims.Uid[..Math.Min(6, claims.Uid.Length)].ToLowerInvariant();
        if (baseName.Length > 24)
            baseName = baseName[..24];

        if (!await _uow.Users.UsernameExistsAsync(baseName, ct))
            return baseName;

        for (var i = 2; i < 1000; i++)
        {
            var candidate = $"{baseName}{i}";
            if (candidate.Length > 32)
                candidate = baseName[..Math.Max(3, 32 - i.ToString().Length)] + i;
            if (!await _uow.Users.UsernameExistsAsync(candidate, ct))
                return candidate;
        }

        return "user" + Guid.NewGuid().ToString("N")[..8];
    }
}
