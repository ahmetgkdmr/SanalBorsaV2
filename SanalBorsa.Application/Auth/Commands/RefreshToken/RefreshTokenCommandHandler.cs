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
        var validated = await _jwt.ValidateRefreshTokenAsync(request.RefreshToken, cancellationToken)
            ?? throw new UnauthorizedAccessException("Refresh token geçersiz veya süresi dolmuş.");

        var user = await _uow.Users.GetByIdAsync(validated.UserId, cancellationToken)
            ?? throw new UnauthorizedAccessException("Kullanıcı bulunamadı.");

        var portfolio = await _uow.Portfolios.GetByUserIdAsync(user.Id, cancellationToken);

        // ROTASYON: yeni çift üretilir, eski token hemen iptal edilir. Böylece bir yenileme
        // token'ı yalnızca bir kez kullanılabilir — kopyası sızmış olsa bile ikinci kullanımda
        // reddedilir ve log'a düşer (bkz. JwtService.ValidateRefreshTokenAsync).
        var tokens = await _jwt.GenerateAsync(user, cancellationToken);
        await _jwt.RevokeAsync(validated.Jti, ct: cancellationToken);

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
                user.ShowTradeHistoryPublic));
    }
}
