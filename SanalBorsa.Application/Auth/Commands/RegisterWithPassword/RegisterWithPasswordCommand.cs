using System.Text.RegularExpressions;
using MediatR;
using Microsoft.Extensions.Logging;
using SanalBorsa.Application.Auth.Commands.LoginWithFirebase;
using SanalBorsa.Application.Common.Interfaces;
using SanalBorsa.Domain.Entities;
using SanalBorsa.Domain.Interfaces;

namespace SanalBorsa.Application.Auth.Commands.RegisterWithPassword;

public record RegisterWithPasswordCommand(
    string Username,
    string Password,
    string? PasswordConfirm = null,
    string? DisplayName = null)
    : IRequest<LoginResult>;

public class RegisterWithPasswordCommandHandler
    : IRequestHandler<RegisterWithPasswordCommand, LoginResult>
{
    private static readonly Regex UsernameRx = new(
        @"^[a-zA-Z][a-zA-Z0-9_]{2,31}$",
        RegexOptions.Compiled);

    private readonly IUnitOfWork _uow;
    private readonly IPasswordHasher _hasher;
    private readonly IJwtService _jwt;
    private readonly ILogger<RegisterWithPasswordCommandHandler> _logger;

    public RegisterWithPasswordCommandHandler(
        IUnitOfWork uow,
        IPasswordHasher hasher,
        IJwtService jwt,
        ILogger<RegisterWithPasswordCommandHandler> logger)
    {
        _uow = uow;
        _hasher = hasher;
        _jwt = jwt;
        _logger = logger;
    }

    public async Task<LoginResult> Handle(
        RegisterWithPasswordCommand request,
        CancellationToken cancellationToken)
    {
        var username = (request.Username ?? string.Empty).Trim();
        if (!UsernameRx.IsMatch(username))
        {
            throw new InvalidOperationException(
                "Kullanıcı adı 3–32 karakter olmalı; harfle başlamalı; sadece harf, rakam ve alt çizgi.");
        }

        var password = request.Password ?? string.Empty;
        if (password.Length < 6)
            throw new InvalidOperationException("Şifre en az 6 karakter olmalı.");

        if (!string.Equals(password, request.PasswordConfirm, StringComparison.Ordinal))
            throw new InvalidOperationException("Şifreler eşleşmiyor.");

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
            FirebaseUid = null,
            Provider = AuthProvider.Local,
            Username = username,
            DisplayName = display,
            PasswordHash = _hasher.Hash(password),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

        await _uow.Users.AddAsync(user, cancellationToken);
        await _uow.Portfolios.AddAsync(new UserPortfolio
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            Cash = 1_000_000m,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        }, cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Local user registered: {Username}", user.Username);

        var portfolio = await _uow.Portfolios.GetByUserIdAsync(user.Id, cancellationToken);
        var tokens = _jwt.Generate(user);
        return new LoginResult(
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
                "local",
                portfolio?.Cash ?? 1_000_000m,
                user.ShowTradeHistoryPublic));
    }
}
