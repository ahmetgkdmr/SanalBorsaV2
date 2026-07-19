using MediatR;
using SanalBorsa.Application.Stocks.Commands.ReplaceStockPriceHistory;

namespace SanalBorsa.Application.Stocks.Commands.UpsertStockPriceHistory;

/// <summary>
/// Inserts/updates daily bars without wiping older history.
/// Overlapping dates are replaced; earlier history is kept.
/// </summary>
public record UpsertStockPriceHistoryCommand(
    string Symbol,
    IReadOnlyList<PriceBarDto> Bars,
    string? Source = null
) : IRequest<UpsertStockPriceHistoryResult>;

public record UpsertStockPriceHistoryResult(
    string Symbol,
    int BarsUpserted,
    DateTime? Earliest,
    DateTime? Latest,
    string? Error
);
