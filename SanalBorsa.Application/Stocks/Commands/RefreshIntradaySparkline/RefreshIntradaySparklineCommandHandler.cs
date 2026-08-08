using MediatR;
using Microsoft.Extensions.Logging;
using SanalBorsa.Application.Common;
using SanalBorsa.Application.Common.Interfaces;
using SanalBorsa.Application.Common.Services;
using SanalBorsa.Domain.Entities;
using SanalBorsa.Domain.Interfaces;

namespace SanalBorsa.Application.Stocks.Commands.RefreshIntradaySparkline;

public class RefreshIntradaySparklineCommandHandler
    : IRequestHandler<RefreshIntradaySparklineCommand, RefreshIntradaySparklineResult>
{
    private const int DelayMs = 200;

    private readonly IUnitOfWork _uow;
    private readonly ITradingViewHistoryService _tv;
    private readonly ILogger<RefreshIntradaySparklineCommandHandler> _logger;
    private readonly MarketDataCacheVersion _cacheVersion;

    public RefreshIntradaySparklineCommandHandler(
        IUnitOfWork uow,
        ITradingViewHistoryService tv,
        ILogger<RefreshIntradaySparklineCommandHandler> logger,
        MarketDataCacheVersion cacheVersion)
    {
        _uow = uow;
        _tv = tv;
        _logger = logger;
        _cacheVersion = cacheVersion;
    }

    public async Task<RefreshIntradaySparklineResult> Handle(
        RefreshIntradaySparklineCommand request,
        CancellationToken cancellationToken)
    {
        var stocks = await _uow.Stocks.GetAllActiveAsync(cancellationToken, request.Market);

        var allBars = new List<StockIntradayBar>();
        var synced = 0;
        var failed = 0;
        var done = 0;

        foreach (var stock in stocks)
        {
            var tvSymbol = request.Market == MarketType.UsStocks
                ? UsExchangeResolver.ToTvSymbol(stock.Exchange, stock.Symbol)
                : $"BIST:{stock.Symbol}";

            try
            {
                var bars = await _tv.GetIntradayBarsByTvSymbolAsync(tvSymbol, "15", cancellationToken);
                if (bars.Count == 0)
                {
                    failed++;
                }
                else
                {
                    allBars.AddRange(bars.Select(b => new StockIntradayBar
                    {
                        StockId = stock.Id,
                        BarTime = b.BarTimeUtc,
                        Close = b.Close,
                    }));
                    synced++;
                }
            }
            catch (Exception ex)
            {
                failed++;
                _logger.LogWarning(ex, "Intraday sparkline sync failed for {Symbol}", stock.Symbol);
            }

            done++;
            if (DelayMs > 0 && done < stocks.Count)
                await Task.Delay(DelayMs, cancellationToken);
        }

        await _uow.IntradayBars.DeleteAllByMarketAsync(request.Market, cancellationToken);
        await _uow.IntradayBars.BulkInsertAsync(allBars, cancellationToken);

        _logger.LogInformation(
            "Intraday sparkline sync ({Market}) — attempted={A} synced={S} failed={F} bars={B}",
            request.Market, stocks.Count, synced, failed, allBars.Count);

        if (allBars.Count > 0)
        {
            if (request.Market == MarketType.UsStocks) _cacheVersion.BumpUs();
            else if (request.Market == MarketType.Bist) _cacheVersion.BumpBist();
        }

        return new RefreshIntradaySparklineResult(stocks.Count, synced, failed, allBars.Count);
    }
}
