namespace SanalBorsa.Application.Common.Interfaces;

public interface ICryptoMarketService
{
    IReadOnlyList<string> GetTrackedSymbols();

    Task<IReadOnlyList<CryptoTickerDto>> GetTickersAsync(CancellationToken ct = default);

    Task<CryptoTickerDto?> GetTickerAsync(string symbol, CancellationToken ct = default);

    Task<CryptoDepthDto> GetDepthAsync(string symbol, CancellationToken ct = default);

    Task<CryptoFillPreviewDto> PreviewBuyAsync(string symbol, decimal? quoteUsd, decimal? quantity, CancellationToken ct = default);

    Task<CryptoFillPreviewDto> PreviewSellAsync(string symbol, decimal quantity, CancellationToken ct = default);
}

public record CryptoTickerDto(
    string Symbol,
    string BaseAsset,
    decimal Price,
    decimal ChangePercent24h,
    decimal QuoteVolume24h,
    decimal High24h,
    decimal Low24h,
    int PriceDecimals = 8);

public record CryptoDepthDto(
    string Symbol,
    IReadOnlyList<CryptoDepthLevelDto> Bids,
    IReadOnlyList<CryptoDepthLevelDto> Asks);

public record CryptoDepthLevelDto(decimal Price, decimal Quantity);

public record CryptoFillLevelDto(decimal Price, decimal Quantity, decimal Cost);

public record CryptoFillPreviewDto(
    string Symbol,
    string Side,
    decimal FilledQuantity,
    decimal AvgPrice,
    decimal Total,
    bool FullyFilled,
    IReadOnlyList<CryptoFillLevelDto> Levels);
