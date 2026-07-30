using System.Text.RegularExpressions;
using MediatR;
using Microsoft.Extensions.Logging;
using SanalBorsa.Application.Auth.Commands.LoginWithFirebase;
using SanalBorsa.Application.Common.Interfaces;
using SanalBorsa.Domain.Entities;
using SanalBorsa.Domain.Interfaces;

namespace SanalBorsa.Application.Auth.Commands.CompleteRegistration;

public record CompleteRegistrationCommand(
    string IdToken,
    string Username,
    string? DisplayName = null)
    : IRequest<LoginResult>;

public class CompleteRegistrationCommandHandler
    : IRequestHandler<CompleteRegistrationCommand, LoginResult>
{
    private static readonly Regex UsernameRx = new(
        @"^[a-zA-Z][a-zA-Z0-9_]{2,31}$",
        RegexOptions.Compiled);

    private readonly IUnitOfWork _uow;
    private readonly IFirebaseAuthProvider _firebase;
    private readonly IJwtService _jwt;
    private readonly ILogger<CompleteRegistrationCommandHandler> _logger;

    public CompleteRegistrationCommandHandler(
        IUnitOfWork uow,
        IFirebaseAuthProvider firebase,
        IJwtService jwt,
        ILogger<CompleteRegistrationCommandHandler> logger)
    {
        _uow = uow;
        _firebase = firebase;
        _jwt = jwt;
        _logger = logger;
    }

    public async Task<LoginResult> Handle(
        CompleteRegistrationCommand request,
        CancellationToken cancellationToken)
    {
        var username = (request.Username ?? string.Empty).Trim();
        if (!UsernameRx.IsMatch(username))
        {
            throw new InvalidOperationException(
                "Kullanıcı adı 3–32 karakter olmalı; harfle başlamalı; sadece harf, rakam ve alt çizgi.");
        }

        var claims = await _firebase.VerifyIdTokenAsync(request.IdToken, cancellationToken)
            ?? throw new UnauthorizedAccessException("Firebase token geçersiz veya süresi dolmuş.");

        var existing = await _uow.Users.GetByFirebaseUidAsync(claims.Uid, cancellationToken);
        if (existing is not null)
        {
            // Zaten kayıtlıysa doğrudan login dön
            var portfolioExisting = await _uow.Portfolios.GetByUserIdAsync(existing.Id, cancellationToken);
            var tokensExisting = _jwt.Generate(existing);
            return new LoginResult(
                tokensExisting.AccessToken,
                tokensExisting.RefreshToken,
                tokensExisting.ExpiresAt,
                ToDto(existing, claims.Provider, portfolioExisting?.Cash ?? 1_000_000m, portfolioExisting?.CashUsd ?? 100_000m));
        }

        if (await _uow.Users.UsernameExistsAsync(username, cancellationToken))
            throw new InvalidOperationException("Bu kullanıcı adı alınmış. Başka bir tane dene.");

        var display = string.IsNullOrWhiteSpace(request.DisplayName)
            ? username
            : request.DisplayName.Trim();
        if (display.Length > 100)
            display = display[..100];

        var user = new User
        {
            Id = Guid.NewGuid(),
            FirebaseUid = claims.Uid,
            Provider = claims.Provider.Contains("phone", StringComparison.OrdinalIgnoreCase)
                ? AuthProvider.Phone
                : AuthProvider.Google,
            Username = username,
            DisplayName = display,
            Email = claims.Email,
            PhoneNumber = claims.PhoneNumber,
            EmailVerified = claims.EmailVerified,
            PhoneVerified = claims.PhoneNumber is not null,
            AvatarUrl = claims.Picture,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

        await _uow.Users.AddAsync(user, cancellationToken);
        await _uow.Portfolios.AddAsync(new UserPortfolio
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            Cash = 1_000_000m,
            CashUsd = 100_000m,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        }, cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "User registered: {Username} ({Uid}) via {Provider}",
            user.Username, claims.Uid, claims.Provider);

        var portfolio = await _uow.Portfolios.GetByUserIdAsync(user.Id, cancellationToken);
        var tokens = _jwt.Generate(user);
        return new LoginResult(
            tokens.AccessToken,
            tokens.RefreshToken,
            tokens.ExpiresAt,
            ToDto(user, claims.Provider, portfolio?.Cash ?? 1_000_000m, portfolio?.CashUsd ?? 100_000m));
    }

    private static UserDto ToDto(
        User user,
        string provider,
        decimal cashTry,
        decimal cashUsd)
        => new(
            user.Id,
            user.Username,
            string.IsNullOrWhiteSpace(user.DisplayName) ? user.Username : user.DisplayName,
            user.Email,
            user.PhoneNumber,
            user.AvatarUrl,
            provider,
            cashTry,
            cashUsd,
            user.ShowTradeHistoryPublic);
}
