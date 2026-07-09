using MediatR;

namespace SanalBorsa.Application.Stocks.Commands.SyncStocks;

/// <summary>
/// Triggers a full data synchronization:
/// - Seeds any missing BIST symbols
/// - Fetches metadata from Yahoo Finance for new stocks
/// - Fetches price history and corporate actions for all active stocks
/// </summary>
public record SyncStocksCommand : IRequest<SyncStocksResult>;

public record SyncStocksResult(
    int StocksAdded,
    int StocksUpdated,
    int PriceRecordsAdded,
    int ActionsAdded
);
