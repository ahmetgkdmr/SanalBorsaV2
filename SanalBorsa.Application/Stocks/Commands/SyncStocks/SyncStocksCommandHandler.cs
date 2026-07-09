using MediatR;
using Microsoft.Extensions.Logging;
using SanalBorsa.Application.Common.Interfaces;
using SanalBorsa.Domain.Entities;
using SanalBorsa.Domain.Interfaces;

namespace SanalBorsa.Application.Stocks.Commands.SyncStocks;

public class SyncStocksCommandHandler : IRequestHandler<SyncStocksCommand, SyncStocksResult>
{
    private readonly IUnitOfWork _uow;
    private readonly IYahooFinanceService _yahoo;
    private readonly ILogger<SyncStocksCommandHandler> _logger;

    public SyncStocksCommandHandler(
        IUnitOfWork uow,
        IYahooFinanceService yahoo,
        ILogger<SyncStocksCommandHandler> logger)
    {
        _uow = uow;
        _yahoo = yahoo;
        _logger = logger;
    }

    public async Task<SyncStocksResult> Handle(SyncStocksCommand request, CancellationToken cancellationToken)
    {
        int stocksAdded = 0, stocksUpdated = 0, pricesAdded = 0, actionsAdded = 0;

        var activeStocks = await _uow.Stocks.GetAllActiveAsync(cancellationToken);

        foreach (var stock in activeStocks)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                // Fetch metadata and update name/sector/industry if changed
                var meta = await _yahoo.GetStockMetadataAsync(stock.YahooSymbol, cancellationToken);
                if (meta is not null)
                {
                    stock.Name = meta.LongName;
                    stock.Sector = meta.Sector;
                    stock.Industry = meta.Industry;
                    stock.UpdatedAt = DateTime.UtcNow;
                    _uow.Stocks.Update(stock);
                    stocksUpdated++;
                }

                // Fetch corporate actions and detect new ones
                var incomingActions = await _yahoo.GetCorporateActionsAsync(stock.YahooSymbol, cancellationToken);
                foreach (var action in incomingActions)
                {
                    action.StockId = stock.Id;
                    var exists = await _uow.CorporateActions.ExistsAsync(
                        stock.Id, action.ActionDate, action.ActionType, cancellationToken);

                    if (!exists)
                    {
                        await _uow.CorporateActions.AddAsync(action, cancellationToken);
                        stock.NeedsHistoryRefresh = true;
                        actionsAdded++;
                    }
                }

                // Only fetch prices if we have not done so, or if refresh is not needed
                // (full refresh is handled by RefreshStockHistory command separately)
                if (!stock.NeedsHistoryRefresh)
                {
                    var from = stock.LatestDataDate?.AddDays(1) ?? DateTime.UnixEpoch;
                    if (from.Date < DateTime.UtcNow.Date)
                    {
                        var newPrices = await _yahoo.GetPriceHistoryAsync(
                            stock.YahooSymbol, from, DateTime.UtcNow, cancellationToken);

                        foreach (var price in newPrices)
                        {
                            price.StockId = stock.Id;
                        }

                        await _uow.PriceHistories.BulkInsertAsync(newPrices, cancellationToken);
                        pricesAdded += newPrices.Count;

                        if (newPrices.Count > 0)
                        {
                            stock.LatestDataDate = newPrices.Max(p => p.Date);
                            _uow.Stocks.Update(stock);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to sync stock {Symbol}", stock.Symbol);
            }
        }

        await _uow.SaveChangesAsync(cancellationToken);

        return new SyncStocksResult(stocksAdded, stocksUpdated, pricesAdded, actionsAdded);
    }
}
