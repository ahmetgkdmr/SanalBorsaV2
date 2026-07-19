using MediatR;
using Microsoft.Extensions.Logging;
using SanalBorsa.Application.Common.Interfaces;
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

                // Corporate actions: KAP via CorporateActionSyncJob / POST …/corporate-actions/sync
                // Prices: TradingView via TradingViewPriceSyncJob / sync.py
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
