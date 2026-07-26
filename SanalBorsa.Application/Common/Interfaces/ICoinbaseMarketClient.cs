namespace SanalBorsa.Application.Common.Interfaces;

public interface ICoinbaseMarketClient
{
    /// <summary>
    /// Coinbase Exchange daily candles (USD pair). Paginated; dates &lt; toExclusive.
    /// </summary>
    Task<IReadOnlyList<CoinbaseDailyBar>> GetDailyUsdCandlesAsync(
        string productId,
        DateTime fromUtc,
        DateTime toUtcExclusive,
        CancellationToken ct = default);

    /// <summary>Online USD quote product id set (örn. BTC-USD).</summary>
    Task<IReadOnlySet<string>> GetUsdProductIdsAsync(CancellationToken ct = default);
}

public sealed record CoinbaseDailyBar(
    DateTime Date,
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close,
    decimal Volume);