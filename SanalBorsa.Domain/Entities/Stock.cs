namespace SanalBorsa.Domain.Entities;

public class Stock
{
    public int Id { get; set; }

    /// <summary>BIST ticker symbol, e.g. "THYAO"</summary>
    public string Symbol { get; set; } = string.Empty;

    /// <summary>Yahoo Finance symbol with .IS suffix, e.g. "THYAO.IS"</summary>
    public string YahooSymbol { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Sector { get; set; }

    public string? Industry { get; set; }

    public string Currency { get; set; } = "TRY";

    public string Exchange { get; set; } = "IST";

    public bool IsActive { get; set; } = true;

    /// <summary>Earliest available historical data date from Yahoo Finance</summary>
    public DateTime? EarliestDataDate { get; set; }

    /// <summary>Most recent price record date</summary>
    public DateTime? LatestDataDate { get; set; }

    /// <summary>Signals that a new corporate action arrived; full history re-fetch is needed</summary>
    public bool NeedsHistoryRefresh { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public ICollection<StockPriceHistory> PriceHistories { get; set; } = new List<StockPriceHistory>();

    public ICollection<CorporateAction> CorporateActions { get; set; } = new List<CorporateAction>();
}
