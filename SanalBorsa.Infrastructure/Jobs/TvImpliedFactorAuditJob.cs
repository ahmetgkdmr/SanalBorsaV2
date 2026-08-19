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
/// Her BIST hissesi için TradingView'in KENDİ implied factor'ündeki (AdjustedClose/Close) basamak
/// atlamalarını bulup kayıtlı büyük kurumsal olaylarımızla (BonusIssue/RightsIssue) eşleştirir —
/// bkz. TvImpliedFactorAuditService. Sınır günü TAHMİN EDİLMEZ, doğrudan TV'nin kendi verisinden
/// okunur (proje sohbeti: IndependentAdjustmentAuditService'in sınır-tahmini yaklaşımı RALYH'te
/// çözülemeyen bir kaymaya takıldı, bu servis o sorunu kökten ortadan kaldırıyor).
/// Sorun bulunan hisse TradingView'den yeniden çekilir; hâlâ sapıyorsa MANUEL İNCELEME için loglanır.
/// Manuel/tek seferlik tetiklenir — bkz. StocksController "tv-implied-factor-audit" endpoint'i.
/// </summary>
[DisableConcurrentExecution(timeoutInSeconds: 4 * 3600)]
public sealed class TvImpliedFactorAuditJob
{
    private const int DelayMs = 150;

    private readonly IUnitOfWork _uow;
    private readonly IMediator _mediator;
    private readonly TvImpliedFactorAuditService _auditor;
    private readonly ILogger<TvImpliedFactorAuditJob> _logger;

    public TvImpliedFactorAuditJob(
        IUnitOfWork uow,
        IMediator mediator,
        TvImpliedFactorAuditService auditor,
        ILogger<TvImpliedFactorAuditJob> logger)
    {
        _uow = uow;
        _mediator = mediator;
        _auditor = auditor;
        _logger = logger;
    }

    public async Task RunAsync(string? symbol, CancellationToken ct = default)
    {
        _logger.LogInformation("TvImpliedFactorAuditJob started at {Time}", DateTimeOffset.UtcNow);

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

            ImpliedFactorAuditResult result;
            try
            {
                result = await AuditAsync(stock, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "TvImpliedFactorAuditJob: {Symbol} denetim hatası", stock.Symbol);
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
                    _logger.LogError(ex, "TvImpliedFactorAuditJob: {Symbol} TradingView yeniden çekme hatası", stock.Symbol);
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
                        "TvImpliedFactorAuditJob: {Symbol} — TradingView'den tekrar çekilince sorun düzeldi.",
                        stock.Symbol);
                }
            }

            if (audited % 50 == 0 || audited == stocks.Count)
            {
                _logger.LogInformation(
                    "TvImpliedFactorAuditJob progress: {Done}/{Total} — flagged={Flagged} fixed={Fixed} stillBad={Bad}",
                    audited, stocks.Count, flagged, resyncedFixed, stillMismatched);
            }

            await DelayAsync(audited, stocks.Count, ct);
        }

        _logger.LogInformation(
            "TvImpliedFactorAuditJob completed — audited={Audited} flagged={Flagged} fixed={Fixed} stillBad={Bad}",
            audited, flagged, resyncedFixed, stillMismatched);
    }

    private async Task<ImpliedFactorAuditResult> AuditAsync(Stock stock, CancellationToken ct)
    {
        var prices = await _uow.PriceHistories.GetByStockIdAsync(stock.Id, ct: ct);
        var actions = await _uow.CorporateActions.GetByStockIdAsync(stock.Id, ct);
        return _auditor.Audit(stock.Symbol, prices, actions);
    }

    private void LogFindings(string symbol, ImpliedFactorAuditResult result, bool resynced)
    {
        var prefix = resynced
            ? $"TvImpliedFactorAuditJob: {symbol} — TradingView'den TEKRAR çekildikten SONRA da sorun sürüyor — MANUEL İNCELEME GEREKİYOR."
            : $"TvImpliedFactorAuditJob: {symbol} — sorun bulundu.";

        if (result.MissingSteps.Count > 0)
        {
            var s = result.MissingSteps[0];
            var rawInfo = s.RawRatio is null
                ? "ham fiyatta o civarda büyük bir sıçrama yok"
                : s.RawMatchesAction switch
                {
                    true => $"HAM FİYAT KAYITLA TUTARLI (oran×{s.RawRatio:0.##}) — sorun sadece TV'nin AdjustedClose'unda, düzeltilebilir",
                    false => $"HAM FİYAT DA KAYITLA TUTARSIZ (oran×{s.RawRatio:0.##}) — TV'nin ham verisi şüpheli, düzeltilemez",
                    null => $"ham fiyat oranı×{s.RawRatio:0.##} (RightsIssue — kayıtlı değer güvenilmediği için karşılaştırılamadı)"
                };
            _logger.LogWarning(
                "{Prefix} Kayıtlı {Count} büyük olayın TV'nin düzeltmesinde karşılığı bulunamadı (TV o olayı hiç uygulamamış olabilir). Örnek: {Date:yyyy-MM-dd} {Type} (Value={Value}) — {RawInfo}",
                prefix, result.MissingSteps.Count, s.ActionDate, s.ActionType, s.Value, rawInfo);
        }

        if (result.MagnitudeMismatches.Count > 0)
        {
            var s = result.MagnitudeMismatches[0];
            _logger.LogWarning(
                "{Prefix} {Count} olayda TV'nin uyguladığı basamak büyüklüğü beklenenden farklı. Örnek: {Date:yyyy-MM-dd} (TV basamağı {StepDate:yyyy-MM-dd}) beklenen×{Expected:0.##} TV×{Actual:0.##}",
                prefix, result.MagnitudeMismatches.Count, s.ActionDate, s.StepDate, s.ExpectedRatio, s.ActualRatio);
        }

        if (result.UnexplainedSteps.Count > 0)
        {
            var s = result.UnexplainedSteps[0];
            var rawInfo = s.RawRatio is null
                ? "ham fiyatta o civarda büyük bir sıçrama yok — muhtemelen TV'nin tekil hesaplama hatası"
                : $"HAM FİYAT DA sıçramış (oran×{s.RawRatio:0.##}) — muhtemelen kaydımızda eksik bir olay var";
            _logger.LogWarning(
                "{Prefix} TV'nin düzeltmesinde {Count} gün, hiçbir kayıtlı olayla eşleşmeyen basamak var. Örnek: {Date:yyyy-MM-dd} oran×{Ratio:0.##} — {RawInfo}",
                prefix, result.UnexplainedSteps.Count, s.Date, s.Ratio, rawInfo);
        }
    }

    private Task DelayAsync(int done, int total, CancellationToken ct)
        => DelayMs > 0 && done < total ? Task.Delay(DelayMs, ct) : Task.CompletedTask;
}
