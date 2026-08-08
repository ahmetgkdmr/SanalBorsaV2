using MediatR;
using Microsoft.Extensions.Logging;
using SanalBorsa.Application.Common;
using SanalBorsa.Application.Common.Interfaces;
using SanalBorsa.Application.Common.Services;
using SanalBorsa.Domain.Entities;
using SanalBorsa.Domain.Interfaces;

namespace SanalBorsa.Application.Stocks.Commands.SyncUsDailyPrices;

/// <summary>
/// BIST'teki SyncBistDailyPricesCommandHandler ile birebir aynı desen: ham fiyat TradingView'dan
/// (adjustment=none) çekilir — BIST'teki gibi split/temettü fiyata gömülmez, ayrı CorporateAction
/// olarak uygulanır (bkz. SyncUsCorporateActionsCommandHandler). Yahoo sadece sembolün gerçek
/// borsasını (NASDAQ/NYSE) çözmek için kullanılır — TV sembolü "{Exchange}:{Symbol}" şeklinde kurulur.
/// </summary>
public class SyncUsDailyPricesCommandHandler
    : IRequestHandler<SyncUsDailyPricesCommand, SyncUsDailyPricesResult>
{
    /// <summary>Artımlı çekimde son günleri yeniden yazmak için geriye dönük pencere.</summary>
    private const int OverlapDays = 5;

    private const int DelayMs = 250;

    private readonly IUnitOfWork _uow;
    private readonly ITradingViewHistoryService _tv;
    private readonly IYahooFinanceService _yahoo;
    private readonly PriceAnomalyGuard _anomalyGuard;
    private readonly ILogger<SyncUsDailyPricesCommandHandler> _logger;
    private readonly MarketDataCacheVersion _cacheVersion;

    public SyncUsDailyPricesCommandHandler(
        IUnitOfWork uow,
        ITradingViewHistoryService tv,
        IYahooFinanceService yahoo,
        PriceAnomalyGuard anomalyGuard,
        ILogger<SyncUsDailyPricesCommandHandler> logger,
        MarketDataCacheVersion cacheVersion)
    {
        _uow = uow;
        _tv = tv;
        _yahoo = yahoo;
        _anomalyGuard = anomalyGuard;
        _logger = logger;
        _cacheVersion = cacheVersion;
    }

    public async Task<SyncUsDailyPricesResult> Handle(
        SyncUsDailyPricesCommand request,
        CancellationToken cancellationToken)
    {
        List<Stock> stocks;

        if (!string.IsNullOrWhiteSpace(request.Symbol))
        {
            var one = await _uow.Stocks.GetBySymbolAsync(
                request.Symbol.Trim().ToUpperInvariant(), cancellationToken, MarketType.UsStocks);
            stocks = one is null ? [] : [one];
        }
        else
        {
            stocks = (await _uow.Stocks.GetAllActiveAsync(cancellationToken, MarketType.UsStocks))
                .Where(s => s.MarketType == MarketType.UsStocks)
                .OrderBy(s => s.Symbol)
                .ToList();
        }

        if (stocks.Count == 0)
            return new SyncUsDailyPricesResult(0, 0, 0, 0, null, "Aktif ABD hissesi yok.");

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
                // Eski (Yahoo dönemi) kayıtlarda Exchange hâlâ jenerik "US" olabilir — TV sembolü
                // kurmadan önce gerçek borsayı çözüp kalıcı olarak günceller (kendi kendini onarır).
                if (string.IsNullOrWhiteSpace(stock.Exchange) || stock.Exchange == "US")
                {
                    var meta = await _yahoo.GetStockMetadataAsync(stock.YahooSymbol, cancellationToken);
                    stock.Exchange = UsExchangeResolver.ToTvPrefix(meta?.Exchange);
                    _uow.Stocks.Update(stock);
                }

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

                var tvSymbol = UsExchangeResolver.ToTvSymbol(stock.Exchange, stock.Symbol);
                var historyRawFetched = await _tv.GetDailyBarsByTvSymbolAsync(tvSymbol, from, to, cancellationToken);
                if (historyRawFetched.Count == 0)
                {
                    failed++;
                    _logger.LogWarning("ABD hisse sync: {Symbol} ({TvSymbol}) için bar gelmedi", stock.Symbol, tvSymbol);
                    continue;
                }

                // TV'nin "request_more_data" ile parçalı çektiği çok uzun (onlarca yıllık) seriler
                // sınır günlerinde nadiren aynı tarihi iki kez üretebilir — unique index ihlali
                // yaşamamak için tarihe göre son değeri tutarak tekilleştiriyoruz.
                var historyRaw = historyRawFetched
                    .GroupBy(b => b.Date.Date)
                    .Select(g => g.Last())
                    .ToList();

                var rangeFrom = historyRaw.Min(h => h.Date).Date;
                var rangeTo = historyRaw.Max(h => h.Date).Date;
                var history = await _anomalyGuard.SanitizeAsync(stock, historyRaw, cancellationToken);

                if (history.Count == 0)
                {
                    await _uow.PriceHistories.DeleteByStockIdAndDateRangeAsync(
                        stock.Id, rangeFrom, rangeTo, cancellationToken);
                    failed++;
                    _logger.LogWarning("ABD hisse sync: {Symbol} tüm barlar outlier filtresine takıldı", stock.Symbol);
                    continue;
                }

                var now = DateTime.UtcNow;

                // Full resync'te sadece bu çekimin kapsadığı aralığı değil, hissenin TÜM eski
                // satırlarını siliyoruz — TV'nin "request_more_data" zinciri bu sefer daha kısa bir
                // aralık dönmüş olabilir (ör. bağlantı erken kesildi), bu durumda range-bazlı silme
                // eski (Yahoo döneminden kalma, split-ayarlı) satırları temizlemeden bırakır ve iki
                // farklı kaynağın verisi aynı hissede karışır (bkz. AAPL 1980-1986 arası bulunan bug).
                if (request.Full)
                    await _uow.PriceHistories.DeleteAllByStockIdAsync(stock.Id, cancellationToken);
                else
                    await _uow.PriceHistories.DeleteByStockIdAndDateRangeAsync(
                        stock.Id, rangeFrom, rangeTo, cancellationToken);

                foreach (var bar in history)
                {
                    bar.StockId = stock.Id;
                    bar.CreatedAt = now;
                }

                await _uow.PriceHistories.BulkInsertAsync(history, cancellationToken);

                // Ekstra bir SELECT ile en eski/en yeni tarihi tekrar sormaya gerek yok — full
                // resync'te sildiğimiz+yazdığımız TEK aralık `history`'nin kendisi (min/max bellekte
                // zaten var); incremental'da ise sadece SON birkaç gün değişiyor, en eski tarih
                // dokunulmadan kalıyor. Bu iki sorgu, hisse başına 44-60 saniyeye varan gidiş-dönüş
                // gecikmesinin önemli bir kısmıydı (bkz. proje sohbeti — BAC/BALL ölçümü).
                var latest = history.Max(h => h.Date).Date;
                var earliest = request.Full
                    ? history.Min(h => h.Date).Date
                    : stock.EarliestDataDate ?? history.Min(h => h.Date).Date;

                stock.EarliestDataDate = earliest;
                stock.LatestDataDate = latest;
                stock.NeedsHistoryRefresh = false;
                stock.UpdatedAt = now;
                _uow.Stocks.Update(stock);
                await _uow.SaveChangesAsync(cancellationToken);
                _uow.ClearChanges();

                synced++;
                barsTotal += history.Count;
                if (maxLatest is null || latest > maxLatest)
                    maxLatest = latest;

                _logger.LogInformation(
                    "ABD hisse sync progress: {Done}/{Total} — {Symbol} (+{Bars} bars, earliest {Earliest:yyyy-MM-dd}, latest {Latest:yyyy-MM-dd})",
                    done, stocks.Count, stock.Symbol, history.Count, stock.EarliestDataDate, stock.LatestDataDate);
            }
            catch (Exception ex)
            {
                failed++;
                _uow.ClearChanges();
                _logger.LogError(ex, "ABD hisse sync failed for {Symbol}", stock.Symbol);
            }

            if (DelayMs > 0 && done < stocks.Count)
                await Task.Delay(DelayMs, cancellationToken);
        }

        _logger.LogInformation(
            "ABD hisse sync done — attempted={Attempted} synced={Synced} bars={Bars} failed={Failed} maxLatest={Max:yyyy-MM-dd}",
            stocks.Count, synced, barsTotal, failed, maxLatest);

        if (barsTotal > 0) _cacheVersion.BumpUs();

        return new SyncUsDailyPricesResult(stocks.Count, synced, barsTotal, failed, maxLatest, null);
    }
}
