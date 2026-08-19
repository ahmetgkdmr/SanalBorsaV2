using Hangfire;
using Microsoft.Extensions.Logging;
using SanalBorsa.Application.Common;
using SanalBorsa.Application.Common.Interfaces;
using SanalBorsa.Application.Common.Services;
using SanalBorsa.Domain.Entities;
using SanalBorsa.Domain.Interfaces;

namespace SanalBorsa.Infrastructure.Jobs;

/// <summary>
/// Bir günde %20+ sıçrayan/düşen bar tespit edildiğinde o gün için önceki kapanış yazılır ve hisse
/// alım/satıma kapatılır (bkz. SanalBorsa.Application.Common.Services.PriceAnomalyGuard). Bu job
/// <see cref="AnomalyRetryPolicy"/> zamanlamasıyla (önce 5dk×50, sonra 15dk×20) tetiklenip aynı
/// sembol/tarihi (hissenin piyasasına göre doğru kaynaktan) tekrar çeker:
/// - düzeldiyse: doğru bar'ı yazar, alım/satımı tekrar açar, döngü biter.
/// - hâlâ anormalse ve politika tükenmediyse: bir sonraki denemeyi zamanlar.
/// - politika tükendiyse (~9 saat sonra): pes edilir, önceki günün kapanışı ve kapalı durum
///   bir sonraki BAŞARILI (şüphesiz) senkrona kadar kalıcı kalır.
/// Piyasa-bağımsız: hem BIST hem ABD hisseleri TradingView'den (ham, adjustment=none) tekrar çekilir.
/// </summary>
[AutomaticRetry(Attempts = 2)]
public sealed class PriceAnomalyRecheckJob
{
    private const decimal AnomalyLowerRatio = 0.8m;
    private const decimal AnomalyUpperRatio = 1.2m;

    private readonly IUnitOfWork _uow;
    private readonly IBistRawPriceService _bistPrices;
    private readonly ITradingViewHistoryService _tv;
    private readonly IPriceAnomalyScheduler _anomalyScheduler;
    private readonly ILogger<PriceAnomalyRecheckJob> _logger;

    public PriceAnomalyRecheckJob(
        IUnitOfWork uow,
        IBistRawPriceService bistPrices,
        ITradingViewHistoryService tv,
        IPriceAnomalyScheduler anomalyScheduler,
        ILogger<PriceAnomalyRecheckJob> logger)
    {
        _uow = uow;
        _bistPrices = bistPrices;
        _tv = tv;
        _anomalyScheduler = anomalyScheduler;
        _logger = logger;
    }

    public async Task RecheckAsync(
        string symbol, DateTime date, decimal previousClose, int attempt, CancellationToken ct = default)
    {
        var stock = await _uow.Stocks.GetBySymbolAsync(symbol, ct);
        if (stock is null)
        {
            _logger.LogWarning("Fiyat anomalisi tekrar kontrol: {Symbol} bulunamadı", symbol);
            return;
        }

        var bars = stock.MarketType == MarketType.UsStocks
            ? await _tv.GetDailyBarsByTvSymbolAsync(UsExchangeResolver.ToTvSymbol(stock.Exchange, stock.Symbol), date.Date, date.Date, ct)
            : await _bistPrices.GetDailyBarsAsync(symbol, date.Date, date.Date, ct);

        var bar = bars.FirstOrDefault(b => b.Date.Date == date.Date);
        if (bar is null)
        {
            _logger.LogWarning(
                "Fiyat anomalisi tekrar kontrol ({Attempt}): {Symbol} {Date:yyyy-MM-dd} için kaynaktan bar gelmedi",
                attempt, symbol, date);
            ScheduleNextOrGiveUp(symbol, date, previousClose, attempt);
            return;
        }

        var ratio = previousClose > 0 ? bar.Close / previousClose : 1m;
        var stillAnomalous = ratio < AnomalyLowerRatio || ratio > AnomalyUpperRatio;

        if (stillAnomalous)
        {
            _logger.LogWarning(
                "Fiyat anomalisi deneme {Attempt}/{Max} sonrası da doğrulandı: {Symbol} {Date:yyyy-MM-dd} close={Close} (prev={Prev})",
                attempt, AnomalyRetryPolicy.MaxAttempts, symbol, date, bar.Close, previousClose);
            ScheduleNextOrGiveUp(symbol, date, previousClose, attempt);
            return;
        }

        bar.StockId = stock.Id;
        bar.CreatedAt = DateTime.UtcNow;
        if (bar.AdjustedClose <= 0)
            bar.AdjustedClose = bar.Close;

        await _uow.PriceHistories.DeleteByStockIdAndDateRangeAsync(stock.Id, date.Date, date.Date, ct);
        await _uow.PriceHistories.BulkInsertAsync([bar], ct);

        if (stock.TradingHaltReason is not null)
        {
            stock.TradingHaltReason = null;
            _uow.Stocks.Update(stock);
            await _uow.SaveChangesAsync(ct);
        }

        _logger.LogInformation(
            "Fiyat anomalisi düzeldi (deneme {Attempt}): {Symbol} {Date:yyyy-MM-dd} → {Close} (önceki placeholder yerine yazıldı, alım/satım tekrar açıldı)",
            attempt, symbol, date, bar.Close);
    }

    private void ScheduleNextOrGiveUp(string symbol, DateTime date, decimal previousClose, int attempt)
    {
        if (AnomalyRetryPolicy.ShouldGiveUp(attempt))
        {
            _logger.LogWarning(
                "Fiyat anomalisi {MaxAttempts} denemeden sonra hâlâ düzelmedi — pes edildi: {Symbol} {Date:yyyy-MM-dd}. " +
                "Alım/satım bir sonraki başarılı senkrona kadar kapalı kalacak.",
                AnomalyRetryPolicy.MaxAttempts, symbol, date);
            return;
        }

        _anomalyScheduler.ScheduleRecheck(
            symbol, date, previousClose, AnomalyRetryPolicy.NextDelay(attempt), attempt + 1);
    }
}
