using MediatR;

namespace SanalBorsa.Application.Stocks.Commands.RefreshStockHistory;

/// <summary>
/// Re-fetches the complete price history for all stocks marked with NeedsHistoryRefresh = true.
/// Called automatically by the HistoryRefreshJob after new corporate actions are detected.
/// Can also be triggered manually for a specific stock symbol.
/// </summary>
public record RefreshStockHistoryCommand(string? Symbol = null, bool ForceAll = false) : IRequest<RefreshStockHistoryResult>;

public record RefreshStockHistoryResult(int StocksRefreshed, int PriceRecordsInserted);
