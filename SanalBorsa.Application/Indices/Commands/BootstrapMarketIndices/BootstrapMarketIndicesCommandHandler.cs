using MediatR;
using Microsoft.Extensions.Logging;
using SanalBorsa.Application.Common.Interfaces;
using SanalBorsa.Application.Common.Seeds;
using SanalBorsa.Domain.Entities;
using SanalBorsa.Domain.Interfaces;

namespace SanalBorsa.Application.Indices.Commands.BootstrapMarketIndices;

public class BootstrapMarketIndicesCommandHandler
    : IRequestHandler<BootstrapMarketIndicesCommand, BootstrapMarketIndicesResult>
{
    private readonly IUnitOfWork _uow;
    private readonly IYahooFinanceService _yahoo;
    private readonly ILogger<BootstrapMarketIndicesCommandHandler> _logger;

    private const int DelayMs = 300;

    public BootstrapMarketIndicesCommandHandler(
        IUnitOfWork uow,
        IYahooFinanceService yahoo,
        ILogger<BootstrapMarketIndicesCommandHandler> logger)
    {
        _uow = uow;
        _yahoo = yahoo;
        _logger = logger;
    }

    public async Task<BootstrapMarketIndicesResult> Handle(
        BootstrapMarketIndicesCommand request,
        CancellationToken cancellationToken)
    {
        int added = 0, processed = 0, prices = 0, failed = 0;

        foreach (var entry in MarketInstrumentSeed.All)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var stock = await _uow.Stocks.GetBySymbolAsync(entry.Symbol, cancellationToken);
                if (stock is null)
                {
                    stock = new Stock
                    {
                        Symbol = entry.Symbol,
                        YahooSymbol = entry.YahooSymbol,
                        Name = entry.Name,
                        Currency = entry.Currency,
                        Exchange = entry.Exchange,
                        IsActive = true,
                        NeedsHistoryRefresh = true,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow,
                    };

                    await _uow.Stocks.AddAsync(stock, cancellationToken);
                    await _uow.SaveChangesAsync(cancellationToken);
                    added++;
                }

                if (!stock.NeedsHistoryRefresh && stock.EarliestDataDate is not null)
                {
                    var latest = await _uow.PriceHistories.GetLatestByStockIdAsync(stock.Id, cancellationToken);
                    if (latest is not null)
                        continue;
                }

                processed++;
                _logger.LogInformation("Bootstrapping market instrument {Symbol}", entry.Symbol);

                await _uow.PriceHistories.DeleteAllByStockIdAsync(stock.Id, cancellationToken);

                var history = await _yahoo.GetPriceHistoryAsync(
                    stock.YahooSymbol,
                    DateTime.UnixEpoch,
                    DateTime.UtcNow,
                    cancellationToken);

                foreach (var record in history)
                    record.StockId = stock.Id;

                if (history.Count > 0)
                {
                    await _uow.PriceHistories.BulkInsertAsync(history, cancellationToken);
                    stock.EarliestDataDate = history.Min(p => p.Date);
                    stock.LatestDataDate = history.Max(p => p.Date);
                    stock.NeedsHistoryRefresh = false;
                    prices += history.Count;
                }
                else
                {
                    stock.NeedsHistoryRefresh = true;
                    _logger.LogWarning("No price history returned for instrument {Symbol}", entry.Symbol);
                }

                stock.UpdatedAt = DateTime.UtcNow;
                _uow.Stocks.Update(stock);
                await _uow.SaveChangesAsync(cancellationToken);

                await Task.Delay(DelayMs, cancellationToken);
            }
            catch (Exception ex)
            {
                failed++;
                _logger.LogError(ex, "Failed to bootstrap instrument {Symbol}", entry.Symbol);
            }
        }

        return new BootstrapMarketIndicesResult(added, processed, prices, failed);
    }
}
