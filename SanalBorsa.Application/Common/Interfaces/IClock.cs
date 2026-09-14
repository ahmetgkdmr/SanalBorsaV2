namespace SanalBorsa.Application.Common.Interfaces;

/// <summary>
/// Şimdiki zaman. Doğrudan <see cref="DateTimeOffset.UtcNow"/> çağırmak yerine bunun
/// enjekte edilmesi, seans saatine bağlı davranışın (BIST 19:00–09:30) test edilebilmesini
/// sağlar — aksi hâlde alım/satım testleri günün saatine göre bazen geçip bazen kalırdı.
/// </summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
