using MediatR;
using SanalBorsa.Application.Auth.Commands.LoginWithFirebase;
using SanalBorsa.Application.Common.Exceptions;
using SanalBorsa.Domain.Interfaces;

namespace SanalBorsa.Application.Auth.Queries.GetMe;

public class GetMeQueryHandler : IRequestHandler<GetMeQuery, UserDto>
{
    private readonly IUnitOfWork _uow;

    public GetMeQueryHandler(IUnitOfWork uow)
    {
        _uow = uow;
    }

    public async Task<UserDto> Handle(GetMeQuery request, CancellationToken cancellationToken)
    {
        var user = await _uow.Users.GetByIdAsync(request.UserId, cancellationToken)
            ?? throw new NotFoundException("User", request.UserId);

        var portfolio = await _uow.Portfolios.GetByUserIdAsync(user.Id, cancellationToken);

        return new UserDto(
            user.Id,
            user.DisplayName,
            user.Email,
            user.PhoneNumber,
            user.AvatarUrl,
            user.Provider.ToString().ToLowerInvariant(),
            portfolio?.Cash ?? 1_000_000m,
            portfolio?.CashUsd ?? 100_000m);
    }
}
