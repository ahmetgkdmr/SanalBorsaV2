using MediatR;

namespace SanalBorsa.Application.Stocks.Commands.DeleteOldPriceHistories;

/// <summary>
/// Deletes StockPriceHistories rows with CreatedAt &lt; cutoff (UTC).
/// Used to drop non-TradingView leftovers (pre 2026-07-15 imports).
/// </summary>
public record DeleteOldPriceHistoriesCommand(DateTime CreatedBeforeUtc)
    : IRequest<DeleteOldPriceHistoriesResult>;

public record DeleteOldPriceHistoriesResult(
    int DeletedRows,
    DateTime CreatedBeforeUtc,
    int StocksReset);
