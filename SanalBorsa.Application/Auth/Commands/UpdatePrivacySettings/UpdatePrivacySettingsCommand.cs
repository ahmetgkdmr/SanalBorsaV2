using MediatR;
using SanalBorsa.Application.Auth.Commands.LoginWithFirebase;
using SanalBorsa.Application.Common.Exceptions;
using SanalBorsa.Domain.Interfaces;

namespace SanalBorsa.Application.Auth.Commands.UpdatePrivacySettings;

public record UpdatePrivacySettingsCommand(Guid UserId, bool ShowTradeHistoryPublic)
    : IRequest<UserDto>;

public class UpdatePrivacySettingsCommandHandler
    : IRequestHandler<UpdatePrivacySettingsCommand, UserDto>
{
    private readonly IUnitOfWork _uow;

    public UpdatePrivacySettingsCommandHandler(IUnitOfWork uow)
    {
        _uow = uow;
    }

    public async Task<UserDto> Handle(
        UpdatePrivacySettingsCommand request,
        CancellationToken cancellationToken)
    {
        var user = await _uow.Users.GetByIdAsync(request.UserId, cancellationToken)
            ?? throw new NotFoundException("User", request.UserId);

        user.ShowTradeHistoryPublic = request.ShowTradeHistoryPublic;
        user.UpdatedAt = DateTime.UtcNow;
        _uow.Users.Update(user);
        await _uow.SaveChangesAsync(cancellationToken);

        var portfolio = await _uow.Portfolios.GetByUserIdAsync(user.Id, cancellationToken);
        return new UserDto(
            user.Id,
            user.Username,
            string.IsNullOrWhiteSpace(user.DisplayName) ? user.Username : user.DisplayName,
            user.Email,
            user.PhoneNumber,
            user.AvatarUrl,
            user.Provider.ToString().ToLowerInvariant(),
            portfolio?.Cash ?? 1_000_000m,
            portfolio?.CashUsd ?? 100_000m,
            user.ShowTradeHistoryPublic);
    }
}
