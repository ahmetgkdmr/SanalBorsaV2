namespace SanalBorsa.Domain.Models;

public record MarketPriceSnapshot(
    decimal? LastClose,
    decimal? LastOpen,
    decimal? PreviousClose,
    long? LastVolume,
    IReadOnlyList<decimal> Sparkline);
