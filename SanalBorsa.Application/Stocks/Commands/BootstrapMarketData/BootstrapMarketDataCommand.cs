using MediatR;

namespace SanalBorsa.Application.Stocks.Commands.BootstrapMarketData;

/// <summary>
/// One-time full market bootstrap:
/// 1. Seeds all missing BIST symbols into Stocks
/// 2. Fetches corporate actions for every active stock
/// 3. Fetches full adjusted price history from earliest available date to today
/// </summary>
public record BootstrapMarketDataCommand : IRequest<BootstrapMarketDataResult>;

public record BootstrapMarketDataResult(
    int StocksAdded,
    int StocksProcessed,
    int CorporateActionsAdded,
    int PriceRecordsInserted,
    int FailedStocks
);
