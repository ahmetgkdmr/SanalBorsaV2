using Hangfire;
using Microsoft.Extensions.Logging;
using SanalBorsa.Application.Common.Interfaces;
using SanalBorsa.Application.Common.Services;
using SanalBorsa.Domain.Entities;
using SanalBorsa.Domain.Interfaces;

namespace SanalBorsa.Infrastructure.Jobs;

/// <summary>
/// Bir günde %20+ sıçrayan/düşen bar tespit edildiğinde o gün için önceki kapanış yazılır
/// (bkz. SanalBorsa.Application.Common.Services.PriceAnomalyGuard) ve bu job 6 saat sonra
/// tetiklenip aynı sembol/tarihi (hissenin piyasasına göre doğru kaynaktan) tekrar çeker:
/// - hâlâ aynı şekilde anormalse: dokunmaz, önceki günün kapanışı kalıcı olarak yazılı kalır.
/// - farklı/normal bir değer geldiyse: doğru bar'ı yazar.
/// Piyasa-bağımsız: hem BIST hem ABD hisseleri artık TradingView'den (ham, adjustment=none)
/// tekrar çekilir — ABD tarafı da BIST gibi ham fiyat kaynağına geçti (bkz. SyncUsDailyPricesCommandHandler).
/// </summary>
[AutomaticRetry(Attempts = 2)]
public sealed class PriceAnomalyRecheckJob
{
    private const decimal AnomalyLowerRatio = 0.8m;
    private const decimal AnomalyUpperRatio = 1.2m;

    private readonly IUnitOfWork _uow;
    private readonly IBistRawPriceService _bistPrices;
    private readonly ITradingViewHistoryService _tv;
    private readonly ILogger<PriceAnomalyRecheckJob> _logger;

    public PriceAnomalyRecheckJob(
        IUnitOfWork uow,
        IBistRawPriceService bistPrices,
        ITradingViewHistoryService tv,
        ILogger<PriceAnomalyRecheckJob> logger)
    {
        _uow = uow;
        _bistPrices = bistPrices;
        _tv = tv;
        _logger = logger;
    }

    public async Task RecheckAsync(string symbol, DateTime date, decimal previousClose, CancellationToken ct = default)
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
                "Fiyat anomalisi tekrar kontrol: {Symbol} {Date:yyyy-MM-dd} için kaynaktan bar gelmedi — önceki gün değeri korunuyor",
                symbol, date);
            return;
        }

        var ratio = previousClose > 0 ? bar.Close / previousClose : 1m;
        var stillAnomalous = ratio < AnomalyLowerRatio || ratio > AnomalyUpperRatio;

        if (stillAnomalous)
        {
            _logger.LogWarning(
                "Fiyat anomalisi 6 saat sonra da doğrulandı: {Symbol} {Date:yyyy-MM-dd} close={Close} (prev={Prev}) — önceki günün kapanışı kalıcı olarak korunuyor",
                symbol, date, bar.Close, previousClose);
            return;
        }

        bar.StockId = stock.Id;
        bar.CreatedAt = DateTime.UtcNow;
        if (bar.AdjustedClose <= 0)
            bar.AdjustedClose = bar.Close;

        await _uow.PriceHistories.DeleteByStockIdAndDateRangeAsync(stock.Id, date.Date, date.Date, ct);
        await _uow.PriceHistories.BulkInsertAsync([bar], ct);

        _logger.LogInformation(
            "Fiyat anomalisi düzeldi: {Symbol} {Date:yyyy-MM-dd} → {Close} (önceki placeholder yerine yazıldı)",
            symbol, date, bar.Close);
    }
}
