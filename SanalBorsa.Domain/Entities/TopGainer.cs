using SanalBorsa.Domain.Enums;

namespace SanalBorsa.Domain.Entities;

/// <summary>
/// Haftalık güncellenen dönem şampiyonları (1W / 1M / 1Y en çok kazanan).
/// Her dönem için Rank=1 tutulur; job yeniden hesaplayınca tablo yenilenir.
/// </summary>
public class TopGainer
{
    public int Id { get; set; }

    public TopGainerPeriod Period { get; set; }

    public int Rank { get; set; }

    public int StockId { get; set; }

    public Stock Stock { get; set; } = null!;

    public string Symbol { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>Dönem getirisi (%).</summary>
    public decimal ReturnPct { get; set; }

    public decimal StartPrice { get; set; }

    public decimal EndPrice { get; set; }

    public DateTime StartDate { get; set; }

    public DateTime EndDate { get; set; }

    public DateTime ComputedAt { get; set; }
}
