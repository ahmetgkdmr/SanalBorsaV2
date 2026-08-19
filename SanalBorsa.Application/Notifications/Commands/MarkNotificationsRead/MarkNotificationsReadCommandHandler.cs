using MediatR;
using SanalBorsa.Domain.Interfaces;

namespace SanalBorsa.Application.Notifications.Commands.MarkNotificationsRead;

public class MarkNotificationsReadCommandHandler : IRequestHandler<MarkNotificationsReadCommand>
{
    private readonly IUnitOfWork _uow;

    public MarkNotificationsReadCommandHandler(IUnitOfWork uow)
    {
        _uow = uow;
    }

    public async Task Handle(MarkNotificationsReadCommand request, CancellationToken ct)
        => await _uow.Notifications.MarkAllAsReadAsync(request.UserId, ct);
}
