using SanalBorsa.Domain.Enums;

namespace SanalBorsa.Domain.Entities;

/// <summary>
/// Dönem şampiyonları (1W / 1M / 1Y / 5Y / 10Y). BIST ve Crypto ayrı satırlar.
/// Her (MarketType, Period) için Rank=1; job yeniden hesaplayınca ilgili market yenilenir.
/// </summary>
public class TopGainer
{
    public int Id { get; set; }

    public MarketType MarketType { get; set; } = MarketType.Bist;

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
