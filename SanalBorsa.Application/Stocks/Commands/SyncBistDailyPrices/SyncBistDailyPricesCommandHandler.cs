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
    private const int OverlapDays = 5;

    /// <summary>
    /// Önceki kapanışa göre bu oranın dışında kalan bar'lar TV glitch / yarım seans kabul edilir.
    /// (Gerçek bedelsiz/bölünme nadiren tüm piyasada aynı gün olur; sync overlap zaten düzeltir.)
    /// </summary>
    private const decimal MaxDayMoveRatio = 0.55m;

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
                var history = FilterOutlierBars(stock.Symbol, historyRaw);

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

    private List<StockPriceHistory> FilterOutlierBars(
        string symbol,
        IReadOnlyList<StockPriceHistory> bars)
    {
        if (bars.Count <= 1) return bars.ToList();

        var ordered = bars.OrderBy(b => b.Date).ToList();
        var kept = new List<StockPriceHistory>(ordered.Count) { ordered[0] };

        for (var i = 1; i < ordered.Count; i++)
        {
            var prev = ordered[i - 1];
            var cur = ordered[i];

            if (prev.Close <= 0 || !IsExtremeMove(prev.Close, cur.Close))
            {
                kept.Add(cur);
                continue;
            }

            // İzole glitch: öncekiyle kopuk, sonraki (varsa) yine eski seviyeye yakın.
            // Bedelsiz/bölünme: sonraki bar yeni seviyeyi sürdürür → tut.
            var next = i + 1 < ordered.Count ? ordered[i + 1] : null;
            var looksLikeGlitch = next is null
                || IsExtremeMove(cur.Close, next.Close) && !IsExtremeMove(prev.Close, next.Close);

            if (looksLikeGlitch)
            {
                _logger.LogWarning(
                    "BIST ham sync outlier atıldı: {Symbol} {Date:yyyy-MM-dd} close={Close} prev={Prev} ratio={Ratio:F3}",
                    symbol, cur.Date, cur.Close, prev.Close, cur.Close / prev.Close);
                continue;
            }

            kept.Add(cur);
        }

        return kept;
    }

    private static bool IsExtremeMove(decimal from, decimal to)
    {
        if (from <= 0) return false;
        var ratio = to / from;
        return ratio < (1m - MaxDayMoveRatio) || ratio > (1m + MaxDayMoveRatio * 2);
    }
}
