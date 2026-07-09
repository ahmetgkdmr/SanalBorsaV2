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

    /// <summary>Split and dividend-adjusted closing price provided by Yahoo Finance</summary>
    public decimal AdjustedClose { get; set; }

    public long Volume { get; set; }

    public DateTime CreatedAt { get; set; }
}
