using MediatR;
using SanalBorsa.Application.DTOs;

namespace SanalBorsa.Application.Notifications.Queries.GetNotifications;

public record GetNotificationsQuery(Guid UserId) : IRequest<GetNotificationsResult>;

public record GetNotificationsResult(
    IReadOnlyList<NotificationDto> Items,
    int UnreadCount);
