using MediatR;

namespace SanalBorsa.Application.Notifications.Commands.MarkNotificationsRead;

/// <summary>Kullanıcının TÜM okunmamış bildirimlerini okundu işaretler — bildirim panelini
/// açtığında çağrılır (proje sohbeti: panel-açılınca-hepsi-okundu tercihi).</summary>
public record MarkNotificationsReadCommand(Guid UserId) : IRequest;
