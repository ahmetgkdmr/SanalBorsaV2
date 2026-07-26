using SanalBorsa.Domain.Enums;

namespace SanalBorsa.Domain.Entities;

/// <summary>
/// Bir işlem gününde alım yapılsaydı bugüne kadar ne kazandırırdı sorusunun önceden
/// hesaplanmış cevabı. Bist/Crypto için <see cref="Rank"/> 1..5 en çok kazandıranlar,
/// Parity için sabit sıra (1 USD/TRY, 2 EUR/TRY, 3 gram altın).
/// </summary>
/// <remarks>
/// Getiri bitiş fiyatına bağlı olduğu için her yeni kapanışta geçmişteki bütün günlerin
/// sıralaması değişir; tablo gece işinde kategori bazında komple yeniden üretilir.
/// </remarks>
public class TimeMachineLeader
{
    public long Id { get; set; }

    public TimeMachineCategory Category { get; set; }

    /// <summary>Alım günü (işlem günü).</summary>
    public DateTime StartDate { get; set; }

    public int Rank { get; set; }

    public int StockId { get; set; }

    public string Symbol { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public decimal StartPrice { get; set; }

    public decimal EndPrice { get; set; }

    /// <summary><see cref="StartDate"/> → <see cref="EndDate"/> arası getiri (%).</summary>
    public decimal ReturnPct { get; set; }

    /// <summary>Hesabın dayandığı son kapanış günü.</summary>
    public DateTime EndDate { get; set; }

    public DateTime ComputedAt { get; set; }
}
