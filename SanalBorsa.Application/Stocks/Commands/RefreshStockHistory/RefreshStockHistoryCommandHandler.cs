using MediatR;
using Microsoft.Extensions.Logging;
using SanalBorsa.Application.Common.Interfaces;
using SanalBorsa.Domain.Entities;
using SanalBorsa.Domain.Interfaces;

namespace SanalBorsa.Application.Stocks.Commands.RefreshStockHistory;

public class RefreshStockHistoryCommandHandler : IRequestHandler<RefreshStockHistoryCommand, RefreshStockHistoryResult>
{
    private readonly IUnitOfWork _uow;
    private readonly IYahooFinanceService _yahoo;
    private readonly ILogger<RefreshStockHistoryCommandHandler> _logger;

    public RefreshStockHistoryCommandHandler(
        IUnitOfWork uow,
        IYahooFinanceService yahoo,
        ILogger<RefreshStockHistoryCommandHandler> logger)
    {
        _uow = uow;
        _yahoo = yahoo;
        _logger = logger;
    }

    public async Task<RefreshStockHistoryResult> Handle(
        RefreshStockHistoryCommand request,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<Stock> stocks;

        if (!string.IsNullOrWhiteSpace(request.Symbol))
        {
            var stock = await _uow.Stocks.GetBySymbolAsync(request.Symbol.ToUpperInvariant(), cancellationToken);
            stocks = stock is null ? [] : [stock];
        }
        else
        {
            stocks = request.ForceAll
                ? await _uow.Stocks.GetAllActiveAsync(cancellationToken)
                : await _uow.Stocks.GetStocksNeedingRefreshAsync(cancellationToken);
        }

        int totalRefreshed = 0, totalInserted = 0;

        foreach (var stock in stocks.Where(s => s.MarketType == MarketType.Bist))
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                _logger.LogInformation("Full history refresh starting for {Symbol}", stock.Symbol);

                // Delete existing price records for a clean re-insert
                await _uow.PriceHistories.DeleteAllByStockIdAsync(stock.Id, cancellationToken);

                // Fetch all available history since Unix epoch
                var allHistory = await _yahoo.GetPriceHistoryAsync(
                    stock.YahooSymbol,
                    DateTime.UnixEpoch,
                    DateTime.UtcNow,
                    cancellationToken);

                foreach (var record in allHistory)
                {
                    record.StockId = stock.Id;
                }

                await _uow.PriceHistories.BulkInsertAsync(allHistory, cancellationToken);

                if (allHistory.Count > 0)
                {
                    stock.EarliestDataDate = allHistory.Min(p => p.Date);
                    stock.LatestDataDate = allHistory.Max(p => p.Date);
                }

                stock.NeedsHistoryRefresh = false;
                stock.UpdatedAt = DateTime.UtcNow;
                _uow.Stocks.Update(stock);

                await _uow.SaveChangesAsync(cancellationToken);

                totalRefreshed++;
                totalInserted += allHistory.Count;

                _logger.LogInformation(
                    "Full history refresh completed for {Symbol}: {Count} records",
                    stock.Symbol, allHistory.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to refresh history for {Symbol}", stock.Symbol);
            }
        }

        return new RefreshStockHistoryResult(totalRefreshed, totalInserted);
    }
}
