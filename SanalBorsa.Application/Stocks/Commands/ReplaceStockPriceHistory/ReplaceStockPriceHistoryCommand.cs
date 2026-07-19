using MediatR;

namespace SanalBorsa.Application.Stocks.Commands.ReplaceStockPriceHistory;

public record PriceBarDto(
    DateTime Date,
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close,
    long Volume,
    decimal? AdjustedClose = null);

public record ReplaceStockPriceHistoryCommand(
    string Symbol,
    IReadOnlyList<PriceBarDto> Bars,
    string? Source = null
) : IRequest<ReplaceStockPriceHistoryResult>;

public record ReplaceStockPriceHistoryResult(
    string Symbol,
    int BarsInserted,
    DateTime? Earliest,
    DateTime? Latest,
    string? Error);
