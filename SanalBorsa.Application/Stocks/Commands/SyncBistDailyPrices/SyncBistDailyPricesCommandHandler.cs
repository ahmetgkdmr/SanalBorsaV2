using MediatR;
using Microsoft.Extensions.Logging;
using SanalBorsa.Application.Common;
using SanalBorsa.Application.Common.Interfaces;
using SanalBorsa.Application.Common.Seeds;
using SanalBorsa.Application.Common.Services;
using SanalBorsa.Domain.Entities;
using SanalBorsa.Domain.Interfaces;

namespace SanalBorsa.Application.Stocks.Commands.SyncBistDailyPrices;

public class SyncBistDailyPricesCommandHandler
    : IRequestHandler<SyncBistDailyPricesCommand, SyncBistDailyPricesResult>
{
    /// <summary>Artımlı çekimde son günleri yeniden yazmak için geriye dönük pencere.</summary>
    private const int OverlapDays = 5;

    private const int DelayMs = 40;

    private readonly IUnitOfWork _uow;
    private readonly IBistRawPriceService _prices;
    private readonly PriceAnomalyGuard _anomalyGuard;
    private readonly ILogger<SyncBistDailyPricesCommandHandler> _logger;
    private readonly MarketDataCacheVersion _cacheVersion;

    public SyncBistDailyPricesCommandHandler(
        IUnitOfWork uow,
        IBistRawPriceService prices,
        PriceAnomalyGuard anomalyGuard,
        ILogger<SyncBistDailyPricesCommandHandler> logger,
        MarketDataCacheVersion cacheVersion)
    {
        _uow = uow;
        _prices = prices;
        _anomalyGuard = anomalyGuard;
        _logger = logger;
        _cacheVersion = cacheVersion;
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

        var to = GetInclusiveToDate();
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
                DateTime from;
                if (request.Full || stock.LatestDataDate is null)
                    from = DateTime.UnixEpoch;
                else if (request.LookbackDays is > 0)
                    from = to.AddDays(-request.LookbackDays.Value);
                else
                    from = stock.LatestDataDate.Value.Date.AddDays(-OverlapDays);

                if (from > to)
                {
                    synced++;
                    if (stock.LatestDataDate is { } ld && (maxLatest is null || ld > maxLatest))
                        maxLatest = ld;
                    continue;
                }

                var historyRaw = await _prices.GetDailyBarsAsync(stock.Symbol, from, to, cancellationToken);
                if (historyRaw.Count == 0)
                {
                    failed++;
                    _logger.LogWarning("BIST ham sync: {Symbol} için bar gelmedi", stock.Symbol);
                    continue;
                }

                var rangeFrom = historyRaw.Min(h => h.Date).Date;
                var rangeTo = historyRaw.Max(h => h.Date).Date;
                var history = await _anomalyGuard.SanitizeAsync(stock, historyRaw, cancellationToken);

                if (history.Count == 0)
                {
                    // Bozuk TV bar'larını sil, yerine bir şey yazma
                    await _uow.PriceHistories.DeleteByStockIdAndDateRangeAsync(
                        stock.Id, rangeFrom, rangeTo, cancellationToken);
                    failed++;
                    _logger.LogWarning("BIST ham sync: {Symbol} tüm barlar outlier filtresine takıldı", stock.Symbol);
                    continue;
                }

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

        if (barsTotal > 0) _cacheVersion.BumpBist();

        return new SyncBistDailyPricesResult(stocks.Count, synced, barsTotal, failed, maxLatest, null);
    }

    /// <summary>
    /// BIST seansı kapanmadan (18:15 TR) bugünün barını yazma —
    /// TV bazen yarım/bozuk "forming" mum gönderiyor.
    /// </summary>
    private static DateTime GetInclusiveToDate()
    {
        var utc = DateTime.UtcNow;
        DateTime trNow;
        try
        {
            var tz = TimeZoneInfo.FindSystemTimeZoneById(
                OperatingSystem.IsWindows() ? "Turkey Standard Time" : "Europe/Istanbul");
            trNow = TimeZoneInfo.ConvertTimeFromUtc(utc, tz);
        }
        catch
        {
            trNow = utc.AddHours(3);
        }

        var todayTr = trNow.Date;
        if (trNow.TimeOfDay < new TimeSpan(18, 15, 0))
            return todayTr.AddDays(-1);

        return todayTr;
    }
}
