namespace SanalBorsa.Application.DTOs;

public record StockDetailDto(
    int Id,
    string Symbol,
    string YahooSymbol,
    string Name,
    string? Sector,
    string? Industry,
    string Currency,
    string Exchange,
    bool IsActive,
    DateTime? EarliestDataDate,
    DateTime? LatestDataDate,
    bool NeedsHistoryRefresh,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    IReadOnlyList<PriceHistoryDto> RecentPrices,
    IReadOnlyList<CorporateActionDto> CorporateActions,
    // Liste (StockDto) ile aynı quote alanları — modal/taç açılışında lastClose/hacim/değişim için
    decimal? LastClose = null,
    decimal? LastOpen = null,
    decimal? PreviousClose = null,
    long? LastVolume = null,
    IReadOnlyList<decimal>? Sparkline = null
);
