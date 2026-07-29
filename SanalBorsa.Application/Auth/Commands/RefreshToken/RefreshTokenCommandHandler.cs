using MediatR;
using SanalBorsa.Application.Auth.Commands.LoginWithFirebase;
using SanalBorsa.Application.Common.Interfaces;
using SanalBorsa.Domain.Interfaces;

namespace SanalBorsa.Application.Auth.Commands.RefreshToken;

public class RefreshTokenCommandHandler : IRequestHandler<RefreshTokenCommand, LoginResult>
{
    private readonly IUnitOfWork _uow;
    private readonly IJwtService _jwt;

    public RefreshTokenCommandHandler(IUnitOfWork uow, IJwtService jwt)
    {
        _uow = uow;
        _jwt = jwt;
    }

    public async Task<LoginResult> Handle(
        RefreshTokenCommand request,
        CancellationToken cancellationToken)
    {
        var userId = _jwt.ValidateRefreshToken(request.RefreshToken)
            ?? throw new UnauthorizedAccessException("Refresh token geçersiz veya süresi dolmuş.");

        var user = await _uow.Users.GetByIdAsync(userId, cancellationToken)
            ?? throw new UnauthorizedAccessException("Kullanıcı bulunamadı.");

        var portfolio = await _uow.Portfolios.GetByUserIdAsync(user.Id, cancellationToken);
        var tokens = _jwt.Generate(user);

        return new LoginResult(
            tokens.AccessToken,
            tokens.RefreshToken,
            tokens.ExpiresAt,
            new LoginWithFirebase.UserDto(
                user.Id,
                user.Username,
                string.IsNullOrWhiteSpace(user.DisplayName) ? user.Username : user.DisplayName,
                user.Email,
                user.PhoneNumber,
                user.AvatarUrl,
                user.Provider.ToString().ToLowerInvariant(),
                portfolio?.Cash ?? 1_000_000m,
                portfolio?.CashUsd ?? 100_000m,
                user.ShowTradeHistoryPublic));
    }
}
