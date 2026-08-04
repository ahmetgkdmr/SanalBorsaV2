namespace SanalBorsa.Domain.Entities;

public class UserPortfolio
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    /// <summary>BIST nakit bakiyesi (TRY).</summary>
    public decimal Cash { get; set; } = 1_000_000m;

    /// <summary>Kripto nakit bakiyesi (USD).</summary>
    public decimal CashUsd { get; set; } = 100_000m;

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public User User { get; set; } = null!;

    public ICollection<PortfolioHolding> Holdings { get; set; } = new List<PortfolioHolding>();

    public ICollection<PortfolioTransaction> Transactions { get; set; } = new List<PortfolioTransaction>();
}

public class PortfolioHolding
{
    public Guid Id { get; set; }

    public Guid PortfolioId { get; set; }

    public string Symbol { get; set; } = string.Empty;

    public MarketType MarketType { get; set; } = MarketType.Bist;

    /// <summary>BIST: tam lot; kripto: fractional miktar.</summary>
    public decimal Quantity { get; set; }

    public decimal AvgCost { get; set; }

    public UserPortfolio Portfolio { get; set; } = null!;
}

public class PortfolioTransaction
{
    public Guid Id { get; set; }

    public Guid PortfolioId { get; set; }

    public string Symbol { get; set; } = string.Empty;

    public MarketType MarketType { get; set; } = MarketType.Bist;

    public TxSide Side { get; set; }

    public decimal Quantity { get; set; }

    public decimal Price { get; set; }

    public decimal Total { get; set; }

    /// <summary>Kripto emirlerinde kademe erime özeti (JSON).</summary>
    public string? FillBreakdownJson { get; set; }

    public DateTime ExecutedAt { get; set; }

    public UserPortfolio Portfolio { get; set; } = null!;
}

public enum TxSide
{
    Buy  = 1,
    Sell = 2,
}

public enum MarketType
{
    Bist     = 1,
    Crypto   = 2,
    UsStocks = 3,
}
