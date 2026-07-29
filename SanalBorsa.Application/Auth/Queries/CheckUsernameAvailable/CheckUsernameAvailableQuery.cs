using MediatR;
using SanalBorsa.Domain.Interfaces;

namespace SanalBorsa.Application.Auth.Queries.CheckUsernameAvailable;

public record CheckUsernameAvailableQuery(string Username) : IRequest<UsernameAvailabilityDto>;

public record UsernameAvailabilityDto(string Username, bool Available, string? Reason);

public class CheckUsernameAvailableQueryHandler
    : IRequestHandler<CheckUsernameAvailableQuery, UsernameAvailabilityDto>
{
    private readonly IUnitOfWork _uow;

    public CheckUsernameAvailableQueryHandler(IUnitOfWork uow)
    {
        _uow = uow;
    }

    public async Task<UsernameAvailabilityDto> Handle(
        CheckUsernameAvailableQuery request,
        CancellationToken cancellationToken)
    {
        var username = (request.Username ?? string.Empty).Trim();
        if (username.Length < 3 || username.Length > 32)
            return new UsernameAvailabilityDto(username, false, "3–32 karakter olmalı.");

        if (!char.IsLetter(username[0]))
            return new UsernameAvailabilityDto(username, false, "Harfle başlamalı.");

        foreach (var ch in username)
        {
            if (!(char.IsLetterOrDigit(ch) || ch == '_'))
                return new UsernameAvailabilityDto(username, false, "Sadece harf, rakam ve _.");
        }

        var taken = await _uow.Users.UsernameExistsAsync(username, cancellationToken);
        return taken
            ? new UsernameAvailabilityDto(username, false, "Bu kullanıcı adı alınmış.")
            : new UsernameAvailabilityDto(username, true, null);
    }
}
