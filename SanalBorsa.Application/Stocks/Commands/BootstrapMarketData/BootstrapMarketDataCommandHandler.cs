using MediatR;
using Microsoft.Extensions.Logging;
using SanalBorsa.Application.Common.Interfaces;
using SanalBorsa.Domain.Entities;
using SanalBorsa.Domain.Interfaces;

namespace SanalBorsa.Application.Stocks.Commands.BootstrapMarketData;

public class BootstrapMarketDataCommandHandler : IRequestHandler<BootstrapMarketDataCommand, BootstrapMarketDataResult>
{
    private readonly IUnitOfWork _uow;
    private readonly IYahooFinanceService _yahoo;
    private readonly IIsYatirimPriceService _isYatirim;
    private readonly IBistSymbolProvider _symbolProvider;
    private readonly ILogger<BootstrapMarketDataCommandHandler> _logger;

    private const int DelayBetweenStocksMs = 400;

    public BootstrapMarketDataCommandHandler(
        IUnitOfWork uow,
        IYahooFinanceService yahoo,
        IIsYatirimPriceService isYatirim,
        IBistSymbolProvider symbolProvider,
        ILogger<BootstrapMarketDataCommandHandler> logger)
    {
        _uow = uow;
        _yahoo = yahoo;
        _isYatirim = isYatirim;
        _symbolProvider = symbolProvider;
        _logger = logger;
    }

    public async Task<BootstrapMarketDataResult> Handle(
        BootstrapMarketDataCommand request,
        CancellationToken cancellationToken)
    {
        int stocksAdded = 0, actionsAdded = 0, priceRecords = 0, failed = 0;

        _logger.LogInformation("Market bootstrap started — seeding missing BIST symbols");

        var bistSymbols = await _symbolProvider.GetSymbolsAsync(cancellationToken);
        var existingStocks = await _uow.Stocks.GetAllAsync(cancellationToken);
        var existingSymbols = existingStocks.Select(s => s.Symbol).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in bistSymbols)
        {
            if (existingSymbols.Contains(entry.Symbol))
                continue;

            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var yahooSymbol = entry.Symbol + ".IS";
                var meta = await _yahoo.GetStockMetadataAsync(yahooSymbol, cancellationToken);

                var stock = new Stock
                {
                    Symbol = entry.Symbol,
                    YahooSymbol = yahooSymbol,
                    Name = meta?.LongName ?? entry.Name,
                    Sector = meta?.Sector,
                    Industry = meta?.Industry,
                    Currency = meta?.Currency ?? "TRY",
                    Exchange = meta?.Exchange ?? "IST",
                    IsActive = true,
                    NeedsHistoryRefresh = true,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                await _uow.Stocks.AddAsync(stock, cancellationToken);
                await _uow.SaveChangesAsync(cancellationToken);
                existingSymbols.Add(entry.Symbol);
                stocksAdded++;

                await Task.Delay(DelayBetweenStocksMs, cancellationToken);
            }
            catch (Exception ex)
            {
                failed++;
                _logger.LogWarning(ex, "Failed to seed stock {Symbol}", entry.Symbol);
            }
        }

        _logger.LogInformation("Stock seed phase done — {Added} new stocks added", stocksAdded);

        // Only fetch data for stocks that still need it (newly seeded or previously failed)
        var stocksToProcess = await _uow.Stocks.GetStocksNeedingRefreshAsync(cancellationToken);

        if (stocksToProcess.Count == 0)
        {
            _logger.LogInformation("No stocks need data fetch — bootstrap skipped history phase");
            return new BootstrapMarketDataResult(stocksAdded, 0, 0, 0, failed);
        }

        _logger.LogInformation(
            "History fetch phase — {Count} stocks need data (skipping already-synced stocks)",
            stocksToProcess.Count);

        int processed = 0;

        foreach (var stock in stocksToProcess)
        {
            cancellationToken.ThrowIfCancellationRequested();
            processed++;

            try
            {
                _logger.LogInformation(
                    "Bootstrap [{Current}/{Total}] processing {Symbol}",
                    processed, stocksToProcess.Count, stock.Symbol);

                // Corporate actions come from KAP (CorporateActionSyncJob / POST …/corporate-actions/sync).

                // Full price history — delete existing and re-fetch from earliest available
                await _uow.PriceHistories.DeleteAllByStockIdAsync(stock.Id, cancellationToken);

                var allHistory = await _yahoo.GetPriceHistoryAsync(
                    stock.YahooSymbol,
                    DateTime.UnixEpoch,
                    DateTime.UtcNow,
                    cancellationToken);

                if (allHistory.Count == 0)
                {
                    _logger.LogWarning(
                        "Yahoo returned no data for {Symbol}, trying İş Yatırım",
                        stock.Symbol);

                    allHistory = await _isYatirim.GetPriceHistoryAsync(
                        stock.Symbol,
                        DateTime.UnixEpoch,
                        DateTime.UtcNow,
                        cancellationToken);
                }

                foreach (var record in allHistory)
                    record.StockId = stock.Id;

                if (allHistory.Count > 0)
                    await _uow.PriceHistories.BulkInsertAsync(allHistory, cancellationToken);

                priceRecords += allHistory.Count;

                if (allHistory.Count > 0)
                {
                    stock.EarliestDataDate = allHistory.Min(p => p.Date);
                    stock.LatestDataDate = allHistory.Max(p => p.Date);
                    stock.NeedsHistoryRefresh = false;
                }
                else
                {
                    stock.NeedsHistoryRefresh = true;
                    _logger.LogWarning(
                        "No price data from Yahoo or İş Yatırım for {Symbol} — will retry later",
                        stock.Symbol);
                }
                stock.UpdatedAt = DateTime.UtcNow;
                _uow.Stocks.Update(stock);

                await _uow.SaveChangesAsync(cancellationToken);

                _logger.LogInformation(
                    "Bootstrap completed for {Symbol}: {Prices} price records, corporate actions synced",
                    stock.Symbol, allHistory.Count);

                await Task.Delay(DelayBetweenStocksMs, cancellationToken);
            }
            catch (Exception ex)
            {
                failed++;
                _logger.LogError(ex, "Bootstrap failed for {Symbol}", stock.Symbol);
            }
        }

        _logger.LogInformation(
            "Market bootstrap finished — Stocks added: {Added}, Processed: {Processed}, Actions: {Actions}, Prices: {Prices}, Failed: {Failed}",
            stocksAdded, processed, actionsAdded, priceRecords, failed);

        return new BootstrapMarketDataResult(stocksAdded, processed, actionsAdded, priceRecords, failed);
    }
}
