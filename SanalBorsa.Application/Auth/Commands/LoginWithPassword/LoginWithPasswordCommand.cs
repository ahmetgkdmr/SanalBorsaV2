using MediatR;
using SanalBorsa.Application.Auth.Commands.LoginWithFirebase;
using SanalBorsa.Application.Common.Interfaces;
using SanalBorsa.Domain.Entities;
using SanalBorsa.Domain.Interfaces;

namespace SanalBorsa.Application.Auth.Commands.LoginWithPassword;

public record LoginWithPasswordCommand(string Username, string Password) : IRequest<LoginResult>;

public class LoginWithPasswordCommandHandler
    : IRequestHandler<LoginWithPasswordCommand, LoginResult>
{
    private readonly IUnitOfWork _uow;
    private readonly IPasswordHasher _hasher;
    private readonly IJwtService _jwt;

    public LoginWithPasswordCommandHandler(
        IUnitOfWork uow,
        IPasswordHasher hasher,
        IJwtService jwt)
    {
        _uow = uow;
        _hasher = hasher;
        _jwt = jwt;
    }

    public async Task<LoginResult> Handle(
        LoginWithPasswordCommand request,
        CancellationToken cancellationToken)
    {
        var username = (request.Username ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(request.Password))
            throw new UnauthorizedAccessException("Kullanıcı adı veya şifre hatalı.");

        var user = await _uow.Users.GetByUsernameAsync(username, cancellationToken);
        if (user is null || user.Provider != AuthProvider.Local || string.IsNullOrEmpty(user.PasswordHash))
            throw new UnauthorizedAccessException("Kullanıcı adı veya şifre hatalı.");

        if (!_hasher.Verify(request.Password, user.PasswordHash))
            throw new UnauthorizedAccessException("Kullanıcı adı veya şifre hatalı.");

        user.UpdatedAt = DateTime.UtcNow;
        _uow.Users.Update(user);
        await _uow.SaveChangesAsync(cancellationToken);

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
                portfolio?.CashUsd ?? 100_000m,
                user.ShowTradeHistoryPublic));
    }
}
