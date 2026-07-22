using MediatR;
using Microsoft.Extensions.Logging;
using SanalBorsa.Application.Common.Interfaces;
using SanalBorsa.Domain.Entities;
using SanalBorsa.Domain.Interfaces;

namespace SanalBorsa.Application.Auth.Commands.LoginWithFirebase;

public class LoginWithFirebaseCommandHandler
    : IRequestHandler<LoginWithFirebaseCommand, LoginResult>
{
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

    public async Task<LoginResult> Handle(
        LoginWithFirebaseCommand request,
        CancellationToken cancellationToken)
    {
        var claims = await _firebase.VerifyIdTokenAsync(request.IdToken, cancellationToken)
            ?? throw new UnauthorizedAccessException("Firebase token geçersiz veya süresi dolmuş.");

        var user = await _uow.Users.GetByFirebaseUidAsync(claims.Uid, cancellationToken);

        if (user is null)
        {
            user = CreateUser(claims);
            await _uow.Users.AddAsync(user, cancellationToken);

            var portfolio = new UserPortfolio
            {
                Id        = Guid.NewGuid(),
                UserId    = user.Id,
                Cash      = 1_000_000m,
                CashUsd   = 100_000m,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            };
            await _uow.Portfolios.AddAsync(portfolio, cancellationToken);
            await _uow.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "New user registered: {Uid} via {Provider}", claims.Uid, claims.Provider);
        }
        else
        {
            // Profil bilgilerini güncelle (isim, fotoğraf değişmiş olabilir)
            user.DisplayName = claims.Name ?? user.DisplayName;
            user.AvatarUrl   = claims.Picture ?? user.AvatarUrl;
            user.UpdatedAt   = DateTime.UtcNow;
            _uow.Users.Update(user);
            await _uow.SaveChangesAsync(cancellationToken);
        }

        var portfolio2 = await _uow.Portfolios.GetByUserIdAsync(user.Id, cancellationToken);
        var tokens = _jwt.Generate(user);

        return new LoginResult(
            tokens.AccessToken,
            tokens.RefreshToken,
            tokens.ExpiresAt,
            new UserDto(
                user.Id,
                user.DisplayName,
                user.Email,
                user.PhoneNumber,
                user.AvatarUrl,
                claims.Provider,
                portfolio2?.Cash ?? 1_000_000m,
                portfolio2?.CashUsd ?? 100_000m));
    }

    private static User CreateUser(FirebaseTokenClaims claims) => new()
    {
        Id            = Guid.NewGuid(),
        FirebaseUid   = claims.Uid,
        Provider      = claims.Provider.Contains("phone") ? AuthProvider.Phone : AuthProvider.Google,
        DisplayName   = claims.Name ?? BuildDisplayName(claims),
        Email         = claims.Email,
        PhoneNumber   = claims.PhoneNumber,
        EmailVerified = claims.EmailVerified,
        PhoneVerified = claims.PhoneNumber is not null,
        AvatarUrl     = claims.Picture,
        CreatedAt     = DateTime.UtcNow,
        UpdatedAt     = DateTime.UtcNow,
    };

    private static string BuildDisplayName(FirebaseTokenClaims claims)
    {
        if (claims.Email is not null)
            return claims.Email.Split('@')[0];
        if (claims.PhoneNumber is not null)
            return "User" + claims.PhoneNumber[^4..];
        return "User" + claims.Uid[..6];
    }
}
