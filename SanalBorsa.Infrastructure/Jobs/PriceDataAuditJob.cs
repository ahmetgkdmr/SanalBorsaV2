using Hangfire;
using MediatR;
using Microsoft.Extensions.Logging;
using SanalBorsa.Application.Common.Services;
using SanalBorsa.Application.Stocks.Commands.SyncBistAdjustedCloses;
using SanalBorsa.Application.Stocks.Commands.SyncBistDailyPrices;
using SanalBorsa.Application.Stocks.Commands.SyncUsAdjustedCloses;
using SanalBorsa.Application.Stocks.Commands.SyncUsDailyPrices;
using SanalBorsa.Domain.Entities;
using SanalBorsa.Domain.Interfaces;

namespace SanalBorsa.Infrastructure.Jobs;

/// <summary>
/// Tüm BIST/ABD fiyat geçmişini (ham + AdjustedClose) TradingView'e güvenerek, dış bir kaynağa
/// (Yahoo) hiç ihtiyaç duymadan KENDİ kurumsal olay kayıtlarımıza karşı denetler (bkz.
/// RawPriceActionConsistencyService). Bkz. proje sohbeti: Yahoo'nun fiyat karşılaştırması hem
/// "ham" için (Yahoo'nun close'u zaten split-düzeltmeli geliyor) hem "düzeltilmiş" için (BIST'in
/// agresif bedelsiz kültürü yüzünden iki bağımsız temettü/split-bileşikleme hesabı doğal olarak
/// ıraksıyor — 100 hissenin 74'ü yanlış alarm) güvenilmez çıktı; TradingView'i tek kaynak kabul
/// edip sadece KENDİ verimizin iç tutarlılığını kontrol etmek çok daha temiz sinyal veriyor
/// (THYAO'daki gerçek split-oranı tutarsızlığı gibi).
/// Sorun bulunan hisse TradingView'den yeniden çekilir; hâlâ sapıyorsa manuel inceleme için loglanır.
/// Manuel/tek seferlik tetiklenir (recurring cron DEĞİL) — bkz. StocksController/UsStocksController
/// "price-audit" endpoint'leri. Yüzlerce hisse × tam geçmiş olduğu için uzun sürer.
/// </summary>
[DisableConcurrentExecution(timeoutInSeconds: 4 * 3600)]
public sealed class PriceDataAuditJob
{
    private const int DelayMs = 300;

    private readonly IUnitOfWork _uow;
    private readonly IMediator _mediator;
    private readonly RawPriceActionConsistencyService _rawAuditor;
    private readonly ILogger<PriceDataAuditJob> _logger;

    public PriceDataAuditJob(
        IUnitOfWork uow,
        IMediator mediator,
        RawPriceActionConsistencyService rawAuditor,
        ILogger<PriceDataAuditJob> logger)
    {
        _uow = uow;
        _mediator = mediator;
        _rawAuditor = rawAuditor;
        _logger = logger;
    }

    public async Task RunAsync(MarketType market, CancellationToken ct = default)
        => await RunAsync(market, null, ct);

    /// <param name="symbol">Verilirse sadece bu tek sembol denetlenir — tanı/test amaçlı.</param>
    public async Task RunAsync(MarketType market, string? symbol, CancellationToken ct = default)
    {
        _logger.LogInformation("PriceDataAuditJob started for {Market} at {Time}", market, DateTimeOffset.UtcNow);

        var stocks = (await _uow.Stocks.GetAllActiveAsync(ct, market))
            .Where(s => s.MarketType == market)
            .Where(s => symbol is null || s.Symbol.Equals(symbol, StringComparison.OrdinalIgnoreCase))
            .OrderBy(s => s.Symbol)
            .ToList();

        var audited = 0;
        var flagged = 0;
        var resyncedFixed = 0;
        var stillMismatched = 0;

        foreach (var stock in stocks)
        {
            ct.ThrowIfCancellationRequested();
            audited++;

            RawPriceAuditResult result;
            try
            {
                result = await AuditAsync(stock, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "PriceDataAuditJob: {Symbol} denetim hatası", stock.Symbol);
                await DelayAsync(audited, stocks.Count, ct);
                continue;
            }

            if (result.HasIssues)
            {
                flagged++;
                LogFindings(stock.Symbol, result, resynced: false);

                try
                {
                    await ResyncFromTradingViewAsync(market, stock.Symbol, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "PriceDataAuditJob: {Symbol} TradingView yeniden çekme hatası", stock.Symbol);
                }
                _uow.ClearChanges();

                var recheck = await AuditAsync(stock, ct);
                if (recheck.HasIssues)
                {
                    stillMismatched++;
                    LogFindings(stock.Symbol, recheck, resynced: true);
                }
                else
                {
                    resyncedFixed++;
                    _logger.LogInformation(
                        "PriceDataAuditJob: {Symbol} — TradingView'den tekrar çekilince sorun düzeldi.",
                        stock.Symbol);
                }
            }

            if (audited % 50 == 0 || audited == stocks.Count)
            {
                _logger.LogInformation(
                    "PriceDataAuditJob {Market} progress: {Done}/{Total} — flagged={Flagged} fixed={Fixed} stillBad={Bad}",
                    market, audited, stocks.Count, flagged, resyncedFixed, stillMismatched);
            }

            await DelayAsync(audited, stocks.Count, ct);
        }

        _logger.LogInformation(
            "PriceDataAuditJob {Market} completed — audited={Audited} flagged={Flagged} fixed={Fixed} stillBad={Bad}",
            market, audited, flagged, resyncedFixed, stillMismatched);
    }

    private async Task<RawPriceAuditResult> AuditAsync(Stock stock, CancellationToken ct)
    {
        var prices = await _uow.PriceHistories.GetByStockIdAsync(stock.Id, ct: ct);
        var actions = await _uow.CorporateActions.GetByStockIdAsync(stock.Id, ct);
        return _rawAuditor.Audit(stock.Symbol, prices, actions);
    }

    private void LogFindings(string symbol, RawPriceAuditResult result, bool resynced)
    {
        var prefix = resynced
            ? $"PriceDataAuditJob: {symbol} — TradingView'den TEKRAR çekildikten SONRA da sorun sürüyor — MANUEL İNCELEME GEREKİYOR."
            : $"PriceDataAuditJob: {symbol} — sorun bulundu.";

        if (result.SplitMismatches.Count > 0)
        {
            var s = result.SplitMismatches[0];
            _logger.LogWarning(
                "{Prefix} Ham fiyat/split tutarsızlığı: {Count} split'te beklenen oran gerçekleşmemiş. Örnek: {Date:yyyy-MM-dd} beklenen×{Expected:0.##} gerçek×{Actual:0.##} (önce={Before} sonra={After})",
                prefix, result.SplitMismatches.Count, s.ActionDate, s.ExpectedRatio, s.ActualRatio, s.PriceBefore, s.PriceAfter);
        }

        if (result.AdjustedContinuityMismatches.Count > 0)
        {
            var s = result.AdjustedContinuityMismatches[0];
            _logger.LogWarning(
                "{Prefix} Düzeltilmiş fiyat split'te pürüzsüz kalmamış (TV'nin kendi düzeltmesi split'i yutmamış olabilir): {Count} split. Örnek: {Date:yyyy-MM-dd} önce={Before} sonra={After} (oran×{Ratio:0.##})",
                prefix, result.AdjustedContinuityMismatches.Count, s.ActionDate, s.AdjustedBefore, s.AdjustedAfter, s.ActualRatio);
        }

        if (result.UnexplainedJumps.Count > 0)
        {
            var s = result.UnexplainedJumps[0];
            _logger.LogWarning(
                "{Prefix} Ham fiyat: {Count} günde kayıtlı olaysız açıklanamaz sıçrama. Örnek: {Date:yyyy-MM-dd} {Prev}→{Close} ({Pct:0.0}%)",
                prefix, result.UnexplainedJumps.Count, s.Date, s.PrevClose, s.Close, s.PctChange);
        }
    }

    private Task DelayAsync(int done, int total, CancellationToken ct)
        => DelayMs > 0 && done < total ? Task.Delay(DelayMs, ct) : Task.CompletedTask;

    private async Task ResyncFromTradingViewAsync(MarketType market, string symbol, CancellationToken ct)
    {
        if (market == MarketType.Bist)
        {
            await _mediator.Send(new SyncBistDailyPricesCommand(true, symbol, null), ct);
            await _mediator.Send(new SyncBistAdjustedClosesCommand(false, symbol, null, 0), ct);
        }
        else
        {
            await _mediator.Send(new SyncUsDailyPricesCommand(true, symbol, null), ct);
            await _mediator.Send(new SyncUsAdjustedClosesCommand(symbol, null, 0), ct);
        }
    }
}
