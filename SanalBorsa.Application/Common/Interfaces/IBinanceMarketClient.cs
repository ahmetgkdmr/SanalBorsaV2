namespace SanalBorsa.Application.Common.Interfaces;

/// <summary>Binance public market data (no API key).</summary>
public interface IBinanceMarketClient
{
    Task<IReadOnlyList<BinanceTicker24hr>> GetTickers24hrAsync(CancellationToken ct = default);

    Task<BinanceOrderBook> GetDepthAsync(string symbol, int limit = 20, CancellationToken ct = default);

    /// <summary>Symbol → PRICE_FILTER tickSize / ondalık hane (exchangeInfo).</summary>
    Task<IReadOnlyDictionary<string, BinancePriceFilter>> GetPriceFiltersAsync(CancellationToken ct = default);

    /// <summary>Günlük OHLCV mumları (listing → end). Sayfalı, throttle'lı.</summary>
    Task<IReadOnlyList<BinanceKline>> GetDailyKlinesAsync(
        string symbol,
        DateTime? startUtc = null,
        DateTime? endUtc = null,
        CancellationToken ct = default);

    /// <summary>İlk günlük mumun open time (Binance listing günü yaklaşık).</summary>
    Task<DateTime?> GetFirstDailyKlineDateAsync(string symbol, CancellationToken ct = default);
}

public record BinanceKline(
    DateTime OpenTimeUtc,
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close,
    decimal Volume);

public record BinancePriceFilter(
    string Symbol,
    string BaseAsset,
    decimal TickSize,
    int PriceDecimals);

public record BinanceTicker24hr(
    string Symbol,
    decimal LastPrice,
    decimal PriceChangePercent,
    decimal QuoteVolume,
    decimal HighPrice,
    decimal LowPrice);

public record BinanceOrderBook(
    string Symbol,
    IReadOnlyList<BinanceDepthLevel> Bids,
    IReadOnlyList<BinanceDepthLevel> Asks);

public record BinanceDepthLevel(decimal Price, decimal Quantity);
