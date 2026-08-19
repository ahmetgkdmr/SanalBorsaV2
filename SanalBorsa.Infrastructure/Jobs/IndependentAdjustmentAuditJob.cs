using Hangfire;
using MediatR;
using Microsoft.Extensions.Logging;
using SanalBorsa.Application.Common.Services;
using SanalBorsa.Application.Stocks.Commands.SyncBistAdjustedCloses;
using SanalBorsa.Application.Stocks.Commands.SyncBistDailyPrices;
using SanalBorsa.Domain.Entities;
using SanalBorsa.Domain.Interfaces;

namespace SanalBorsa.Infrastructure.Jobs;

/// <summary>
/// Her BIST hissesi için en eski günden bugüne, SADECE bizim veritabanımızdaki ham fiyat +
/// kurumsal olay kayıtlarını uygulayarak kendi düzeltilmiş fiyat serimizi baştan hesaplar ve
/// TradingView'in AdjustedClose'uyla gün gün karşılaştırır (bkz. IndependentAdjustmentAuditService).
/// Yahoo'ya hiç ihtiyaç yok — proje sohbeti: Yahoo'nun BIST'teki fiyat karşılaştırması vendor-farkı
/// gürültüsünden (agresif bedelsiz kültürü) dolayı güvenilmez çıktı, bunun yerine TradingView'e
/// güvenip kendi kayıtlarımızın iç tutarlılığını test ediyoruz.
/// %1'den fazla sapan hisse TradingView'den yeniden çekilir (transient bir TV hatasıysa düzelebilir);
/// hâlâ sapıyorsa MANUEL İNCELEME için loglanır — ya TV'nin kendi hesabı ya da bizim kurumsal olay
/// kayıtlarımız (eksik/yanlış bir olay) sorumlu olabilir, ikisi de ayrıca kontrol edilmeli.
/// Manuel/tek seferlik tetiklenir — bkz. StocksController "independent-adjustment-audit" endpoint'i.
/// </summary>
[DisableConcurrentExecution(timeoutInSeconds: 4 * 3600)]
public sealed class IndependentAdjustmentAuditJob
{
    private const int DelayMs = 150;

    private readonly IUnitOfWork _uow;
    private readonly IMediator _mediator;
    private readonly IndependentAdjustmentAuditService _auditor;
    private readonly ILogger<IndependentAdjustmentAuditJob> _logger;

    public IndependentAdjustmentAuditJob(
        IUnitOfWork uow,
        IMediator mediator,
        IndependentAdjustmentAuditService auditor,
        ILogger<IndependentAdjustmentAuditJob> logger)
    {
        _uow = uow;
        _mediator = mediator;
        _auditor = auditor;
        _logger = logger;
    }

    public async Task RunAsync(string? symbol, CancellationToken ct = default)
    {
        _logger.LogInformation("IndependentAdjustmentAuditJob started at {Time}", DateTimeOffset.UtcNow);

        var stocks = (await _uow.Stocks.GetAllActiveAsync(ct, MarketType.Bist))
            .Where(s => s.MarketType == MarketType.Bist)
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

            IndependentAdjustmentResult result;
            try
            {
                result = await AuditAsync(stock, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "IndependentAdjustmentAuditJob: {Symbol} denetim hatası", stock.Symbol);
                await DelayAsync(audited, stocks.Count, ct);
                continue;
            }

            if (result.HasIssues)
            {
                flagged++;
                LogFindings(stock.Symbol, result, resynced: false);

                try
                {
                    await _mediator.Send(new SyncBistDailyPricesCommand(true, stock.Symbol, null), ct);
                    await _mediator.Send(new SyncBistAdjustedClosesCommand(false, stock.Symbol, null, 0), ct);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "IndependentAdjustmentAuditJob: {Symbol} TradingView yeniden çekme hatası", stock.Symbol);
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
                        "IndependentAdjustmentAuditJob: {Symbol} — TradingView'den tekrar çekilince sorun düzeldi.",
                        stock.Symbol);
                }
            }

            if (audited % 20 == 0 || audited == stocks.Count)
            {
                _logger.LogInformation(
                    "IndependentAdjustmentAuditJob progress: {Done}/{Total} — flagged={Flagged} fixed={Fixed} stillBad={Bad}",
                    audited, stocks.Count, flagged, resyncedFixed, stillMismatched);
            }

            await DelayAsync(audited, stocks.Count, ct);
        }

        _logger.LogInformation(
            "IndependentAdjustmentAuditJob completed — audited={Audited} flagged={Flagged} fixed={Fixed} stillBad={Bad}",
            audited, flagged, resyncedFixed, stillMismatched);
    }

    private async Task<IndependentAdjustmentResult> AuditAsync(Stock stock, CancellationToken ct)
    {
        var prices = await _uow.PriceHistories.GetByStockIdAsync(stock.Id, ct: ct);
        var actions = await _uow.CorporateActions.GetByStockIdAsync(stock.Id, ct);
        return _auditor.Audit(stock.Symbol, prices, actions);
    }

    private void LogFindings(string symbol, IndependentAdjustmentResult result, bool resynced)
    {
        var prefix = resynced
            ? $"IndependentAdjustmentAuditJob: {symbol} — TradingView'den TEKRAR çekildikten SONRA da sapma sürüyor — MANUEL İNCELEME GEREKİYOR (TV'nin kendi hesabı VEYA bizim kurumsal olay kayıtlarımız eksik/yanlış olabilir)."
            : $"IndependentAdjustmentAuditJob: {symbol} — sapma bulundu.";

        var s = result.Mismatches[0];
        _logger.LogWarning(
            "{Prefix} {Count}/{Total} günde >%5 sapma. Örnek: {Date:yyyy-MM-dd} biz(hesaplanan)={Computed} TV(gerçek)={Actual} (%{Pct:0.0})",
            prefix, result.Mismatches.Count, result.DaysCompared, s.Date, s.Computed, s.Actual, s.RatioDiff * 100m);
    }

    private Task DelayAsync(int done, int total, CancellationToken ct)
        => DelayMs > 0 && done < total ? Task.Delay(DelayMs, ct) : Task.CompletedTask;
}
