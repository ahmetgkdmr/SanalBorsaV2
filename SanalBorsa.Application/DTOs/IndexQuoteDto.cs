namespace SanalBorsa.Application.DTOs;

public record IndexQuoteDto(
    string Symbol,
    string Name,
    string DisplayName,
    string Exchange,
    decimal Value,
    decimal? PreviousClose,
    decimal ChangePct,
    bool IsUp,
    int Decimals,
    DateTime? LatestDate,
    IReadOnlyList<decimal> Sparkline,
    DateTime? EarliestDate = null);
