using MediatR;
using Microsoft.Extensions.Logging;
using SanalBorsa.Application.Common.Interfaces;
using SanalBorsa.Application.Common.Seeds;
using SanalBorsa.Domain.Entities;
using SanalBorsa.Domain.Interfaces;

namespace SanalBorsa.Application.Stocks.Commands.SyncBistDailyPrices;

public class SyncBistDailyPricesCommandHandler
    : IRequestHandler<SyncBistDailyPricesCommand, SyncBistDailyPricesResult>
{
    /// <summary>Artımlı çekimde son günleri yeniden yazmak için geriye dönük pencere.</summary>
    private const int OverlapDays = 3;

    private const int DelayMs = 40;

    private readonly IUnitOfWork _uow;
    private readonly IBistRawPriceService _prices;
    private readonly ILogger<SyncBistDailyPricesCommandHandler> _logger;

    public SyncBistDailyPricesCommandHandler(
        IUnitOfWork uow,
        IBistRawPriceService prices,
        ILogger<SyncBistDailyPricesCommandHandler> logger)
    {
        _uow = uow;
        _prices = prices;
        _logger = logger;
    }

    public async Task<SyncBistDailyPricesResult> Handle(
        SyncBistDailyPricesCommand request,
        CancellationToken cancellationToken)
    {
        List<Stock> stocks;

        if (!string.IsNullOrWhiteSpace(request.Symbol))
        {
            var one = await _uow.Stocks.GetBySymbolAsync(
                request.Symbol.Trim().ToUpperInvariant(), cancellationToken);
            stocks = one is null ? [] : [one];
        }
        else
        {
            stocks = (await _uow.Stocks.GetAllActiveAsync(cancellationToken, MarketType.Bist))
                .Where(s => s.MarketType == MarketType.Bist)
                .Where(s => !MarketInstrumentSeed.IsMarketInstrument(s.Exchange))
                .Where(s => s.Exchange is "IST" or "BIST")
                .OrderBy(s => s.Symbol)
                .ToList();
        }

        if (stocks.Count == 0)
            return new SyncBistDailyPricesResult(0, 0, 0, 0, null, "Aktif BIST hissesi yok.");

        var to = DateTime.UtcNow.Date;
        var synced = 0;
        var barsTotal = 0;
        var failed = 0;
        DateTime? maxLatest = null;
        var done = 0;

        foreach (var stock in stocks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            done++;

            try
            {
                var from = request.Full || stock.LatestDataDate is null
                    ? DateTime.UnixEpoch
                    : stock.LatestDataDate.Value.Date.AddDays(-OverlapDays);

                if (from > to)
                {
                    synced++;
                    if (stock.LatestDataDate is { } ld && (maxLatest is null || ld > maxLatest))
                        maxLatest = ld;
                    continue;
                }

                var history = await _prices.GetDailyBarsAsync(stock.Symbol, from, to, cancellationToken);
                if (history.Count == 0)
                {
                    failed++;
                    _logger.LogWarning("BIST ham sync: {Symbol} için bar gelmedi", stock.Symbol);
                    continue;
                }

                var rangeFrom = history.Min(h => h.Date).Date;
                var rangeTo = history.Max(h => h.Date).Date;
                var now = DateTime.UtcNow;

                await _uow.PriceHistories.DeleteByStockIdAndDateRangeAsync(
                    stock.Id, rangeFrom, rangeTo, cancellationToken);

                foreach (var bar in history)
                {
                    bar.StockId = stock.Id;
                    bar.CreatedAt = now;
                    // Placeholder — AdjustedClose TV sync ayrı komutla doldurulur
                    if (bar.AdjustedClose <= 0)
                        bar.AdjustedClose = bar.Close;
                }

                await _uow.PriceHistories.BulkInsertAsync(history, cancellationToken);

                var earliest = await _uow.PriceHistories.GetEarliestByStockIdAsync(stock.Id, cancellationToken);
                var latest = await _uow.PriceHistories.GetLatestByStockIdAsync(stock.Id, cancellationToken);

                stock.EarliestDataDate = earliest?.Date;
                stock.LatestDataDate = latest?.Date;
                stock.NeedsHistoryRefresh = false;
                stock.UpdatedAt = now;
                _uow.Stocks.Update(stock);
                await _uow.SaveChangesAsync(cancellationToken);
                _uow.ClearChanges();

                synced++;
                barsTotal += history.Count;
                if (latest is not null && (maxLatest is null || latest.Date > maxLatest))
                    maxLatest = latest.Date;

                if (done % 25 == 0 || done == stocks.Count)
                {
                    _logger.LogInformation(
                        "BIST ham sync progress: {Done}/{Total} — {Symbol} (+{Bars} bars, latest {Latest:yyyy-MM-dd})",
                        done, stocks.Count, stock.Symbol, history.Count, stock.LatestDataDate);
                }
            }
            catch (Exception ex)
            {
                failed++;
                _uow.ClearChanges();
                _logger.LogError(ex, "BIST ham sync failed for {Symbol}", stock.Symbol);
            }

            if (DelayMs > 0 && done < stocks.Count)
                await Task.Delay(DelayMs, cancellationToken);
        }

        _logger.LogInformation(
            "BIST ham sync done — attempted={Attempted} synced={Synced} bars={Bars} failed={Failed} maxLatest={Max:yyyy-MM-dd}",
            stocks.Count, synced, barsTotal, failed, maxLatest);

        return new SyncBistDailyPricesResult(stocks.Count, synced, barsTotal, failed, maxLatest, null);
    }
}
