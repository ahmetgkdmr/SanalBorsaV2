namespace SanalBorsa.Domain.Entities;

/// <summary>
/// Kullanıcıya yönelik bildirim (ör. bedelsiz/bedelli/temettü hesaba yansıdığında) — proje sohbeti:
/// web'de bir zil ikonu + okunmamış sayısı rozeti + panel açılınca hepsi okundu işaretlenir.
/// </summary>
public class Notification
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public bool IsRead { get; set; }

    public DateTime CreatedAt { get; set; }
}
