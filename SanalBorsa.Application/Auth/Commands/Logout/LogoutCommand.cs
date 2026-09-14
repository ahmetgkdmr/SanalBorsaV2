using MediatR;
using SanalBorsa.Application.Common.Interfaces;

namespace SanalBorsa.Application.Auth.Commands.Logout;

/// <summary>
/// Çıkış: kullanıcının sunucudaki TÜM aktif yenileme token'larını iptal eder.
/// Önceden çıkış tamamen istemci tarafındaydı (sadece localStorage temizleniyordu) — token
/// süresi dolana kadar (30 gün) geçerli kalmaya devam ediyordu.
/// </summary>
public record LogoutCommand(Guid UserId) : IRequest<Unit>;

public class LogoutCommandHandler : IRequestHandler<LogoutCommand, Unit>
{
    private readonly IJwtService _jwt;

    public LogoutCommandHandler(IJwtService jwt) => _jwt = jwt;

    public async Task<Unit> Handle(LogoutCommand request, CancellationToken cancellationToken)
    {
        await _jwt.RevokeAllForUserAsync(request.UserId, cancellationToken);
        return Unit.Value;
    }
}
