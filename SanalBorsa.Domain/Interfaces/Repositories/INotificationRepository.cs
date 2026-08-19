using SanalBorsa.Domain.Entities;

namespace SanalBorsa.Domain.Interfaces.Repositories;

public interface INotificationRepository : IRepository<Notification>
{
    /// <summary>Kullanıcının en yeni N bildirimi (en yeni önce).</summary>
    Task<IReadOnlyList<Notification>> GetForUserAsync(
        Guid userId, int take = 30, CancellationToken ct = default);

    Task<int> GetUnreadCountAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Kullanıcının tüm okunmamış bildirimlerini okundu işaretler — bildirim panelini
    /// açtığında (bkz. proje sohbeti: panel-açılınca-hepsi-okundu tercihi).</summary>
    Task MarkAllAsReadAsync(Guid userId, CancellationToken ct = default);
}
