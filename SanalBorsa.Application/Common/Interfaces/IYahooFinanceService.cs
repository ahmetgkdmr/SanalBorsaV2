using SanalBorsa.Domain.Entities;

namespace SanalBorsa.Application.Common.Interfaces;

public interface IYahooFinanceService
{
    /// <summary>
    /// Fetches the full OHLCV + adjusted-close price history for a given Yahoo symbol.
    /// Pass period1 = DateTime.UnixEpoch to retrieve all available history.
    /// </summary>
    Task<IReadOnlyList<StockPriceHistory>> GetPriceHistoryAsync(
        string yahooSymbol,
        DateTime from,
        DateTime to,
        CancellationToken ct = default);

    /// <summary>
    /// Fetches dividend and split events for a symbol.
    /// Returns corporate actions detected by Yahoo Finance (dividends + splits).
    /// </summary>
    Task<IReadOnlyList<CorporateAction>> GetCorporateActionsAsync(
        string yahooSymbol,
        CancellationToken ct = default);

    /// <summary>
    /// Fetches basic metadata (name, sector, industry, currency) for a symbol.
    /// Returns null if the symbol is not found or delisted.
    /// </summary>
    Task<StockMetadata?> GetStockMetadataAsync(string yahooSymbol, CancellationToken ct = default);
}

public record StockMetadata(
    string Symbol,
    string YahooSymbol,
    string LongName,
    string? Sector,
    string? Industry,
    string Currency,
    string Exchange
);
