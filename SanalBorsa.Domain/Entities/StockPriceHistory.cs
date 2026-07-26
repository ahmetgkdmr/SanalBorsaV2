namespace SanalBorsa.Domain.Entities;

public class StockPriceHistory
{
    public long Id { get; set; }

    public int StockId { get; set; }

    public Stock Stock { get; set; } = null!;

    public DateTime Date { get; set; }

    public decimal Open { get; set; }

    public decimal High { get; set; }

    public decimal Low { get; set; }

    public decimal Close { get; set; }

    /// <summary>
    /// TradingView düzeltilmiş kapanış (adjustment=dividends).
    /// Ham seri <see cref="Close"/>; bu alan TV'den ayrı çekilir.
    /// </summary>
    public decimal AdjustedClose { get; set; }

    public long Volume { get; set; }

    public DateTime CreatedAt { get; set; }
}
