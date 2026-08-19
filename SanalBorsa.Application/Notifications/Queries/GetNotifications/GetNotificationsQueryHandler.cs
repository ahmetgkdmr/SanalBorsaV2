using MediatR;
using SanalBorsa.Application.DTOs;
using SanalBorsa.Domain.Interfaces;

namespace SanalBorsa.Application.Notifications.Queries.GetNotifications;

public class GetNotificationsQueryHandler : IRequestHandler<GetNotificationsQuery, GetNotificationsResult>
{
    private readonly IUnitOfWork _uow;

    public GetNotificationsQueryHandler(IUnitOfWork uow)
    {
        _uow = uow;
    }

    public async Task<GetNotificationsResult> Handle(GetNotificationsQuery request, CancellationToken ct)
    {
        var notifications = await _uow.Notifications.GetForUserAsync(request.UserId, ct: ct);
        var unreadCount = await _uow.Notifications.GetUnreadCountAsync(request.UserId, ct);

        var dtos = notifications
            .Select(n => new NotificationDto(n.Id, n.Title, n.Message, n.IsRead, n.CreatedAt))
            .ToList();

        return new GetNotificationsResult(dtos, unreadCount);
    }
}
