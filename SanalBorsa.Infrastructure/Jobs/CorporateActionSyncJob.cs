using Hangfire;
using MediatR;
using Microsoft.Extensions.Logging;
using SanalBorsa.Application.Common;
using SanalBorsa.Application.Common.Services;
using SanalBorsa.Application.Stocks.Commands.SyncBistAdjustedCloses;
using SanalBorsa.Application.Stocks.Commands.SyncCorporateActions;
using SanalBorsa.Domain.Entities;
using SanalBorsa.Domain.Interfaces;

namespace SanalBorsa.Infrastructure.Jobs;

/// <summary>
/// Nightly 18:35 Turkey — incremental KAP check for new corporate actions
/// (bedelsiz / bedelli+rüçhan / nakit temettü) after the latest DB date.
/// Full historical bootstrap uses POST …/corporate-actions/sync?full=true (İş Yatırım).
///
/// AdjustedClose tam geçmişi ancak o hissede YENİ bir kurumsal olay eklendiğinde değişir
/// (yeni bar'lar zaten <c>AdjustedClose = Close</c> placeholder'ıyla giriyor, bkz.
/// SyncBistDailyPricesCommandHandler). Bu yüzden tam yenileme artık tüm hisselerde değil,
/// sadece bu koşuda gerçekten yeni olay eklenen sembollerde tetikleniyor — 645 hisse yerine
/// günde birkaç istek, TradingView'e gereksiz yük/rate-limit riskini önlüyor.
/// Hangfire recurring job; kayıt: <see cref="RecurringJobRegistrar"/>.
/// </summary>
[DisableConcurrentExecution(timeoutInSeconds: 600)]
[AutomaticRetry(Attempts = 3)]
public sealed class CorporateActionSyncJob
{
    private readonly IMediator _mediator;
    private readonly IBackgroundJobClient _jobs;
    private readonly IUnitOfWork _uow;
    private readonly RawPriceActionConsistencyService _rawAuditor;
    private readonly ILogger<CorporateActionSyncJob> _logger;

    public CorporateActionSyncJob(
        IMediator mediator,
        IBackgroundJobClient jobs,
        IUnitOfWork uow,
        RawPriceActionConsistencyService rawAuditor,
        ILogger<CorporateActionSyncJob> logger)
    {
        _mediator = mediator;
        _jobs = jobs;
        _uow = uow;
        _rawAuditor = rawAuditor;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("CorporateActionSyncJob (KAP) started at {Time}", DateTimeOffset.UtcNow);

        try
        {
            var result = await _mediator.Send(new SyncCorporateActionsCommand(FullResync: false), ct);

            _logger.LogInformation(
                "CorporateActionSyncJob (KAP) completed — Processed: {Processed}, Skipped: {Skipped}, Added: {Added}, Failed: {Failed}, Affected: {Affected}",
                result.StocksProcessed, result.StocksSkipped, result.ActionsAdded, result.Failed, result.AffectedSymbols.Count);

            await RefreshAdjustedClosesAsync(result.AffectedSymbols, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CorporateActionSyncJob (KAP) failed");
            throw;
        }
    }

    private async Task RefreshAdjustedClosesAsync(IReadOnlyList<string> symbols, CancellationToken ct)
    {
        if (symbols.Count == 0)
        {
            _logger.LogInformation("CorporateActionSyncJob — bugün yeni kurumsal olay yok, AdjustedClose yenilemesi atlandı");
            return;
        }

        _logger.LogInformation(
            "CorporateActionSyncJob — {Count} hissede yeni olay bulundu, AdjustedClose yenileniyor: {Symbols}",
            symbols.Count, string.Join(", ", symbols));

        foreach (var symbol in symbols)
        {
            try
            {
                var adj = await _mediator.Send(new SyncBistAdjustedClosesCommand(Symbol: symbol), ct);
                _logger.LogInformation(
                    "CorporateActionSyncJob — {Symbol} AdjustedClose yenilendi — rows={Rows} failed={Failed}",
                    symbol, adj.RowsUpdated, adj.Failed);

                if (adj.Suspicious > 0)
                {
                    _jobs.Schedule<AdjustedCloseRetryJob>(
                        j => j.RetryAsync(MarketType.Bist, symbol, 1, CancellationToken.None),
                        AnomalyRetryPolicy.Phase1Delay);
                    _logger.LogWarning(
                        "CorporateActionSyncJob — {Symbol} taze veri ham fiyatla çelişiyordu, yazılmadı, alım/satım kapatıldı — retry zinciri başlatıldı.",
                        symbol);
                }

                await AuditRawConsistencyAsync(symbol, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CorporateActionSyncJob — {Symbol} AdjustedClose yenileme hatası", symbol);
            }
        }
    }

    /// <summary>
    /// Ham fiyatın kendi kurumsal olay kayıtlarımızla tutarlı olup olmadığını kontrol eder (bkz.
    /// RawPriceActionConsistencyService, proje sohbeti — THYAO'da bu şekilde gerçek bir split-oranı
    /// tutarsızlığı ve kayıtsız bir sıçrama bulunmuştu). Sadece loglar — retry/kapatma tetiklemez,
    /// çünkü bu bütün geçmişi değerlendiren bir denetim, "taze bir bar" değil.
    /// </summary>
    private async Task AuditRawConsistencyAsync(string symbol, CancellationToken ct)
    {
        var stock = await _uow.Stocks.GetBySymbolAsync(symbol, ct);
        if (stock is null) return;

        var prices = await _uow.PriceHistories.GetByStockIdAsync(stock.Id, ct: ct);
        var actions = await _uow.CorporateActions.GetByStockIdAsync(stock.Id, ct);
        var result = _rawAuditor.Audit(symbol, prices, actions);

        if (!result.HasIssues) return;

        if (result.SplitMismatches.Count > 0)
        {
            var s = result.SplitMismatches[0];
            _logger.LogWarning(
                "CorporateActionSyncJob — {Symbol} ham fiyat/split tutarsızlığı: {Count} split'te beklenen oran gerçekleşmemiş. Örnek: {Date:yyyy-MM-dd} beklenen×{Expected:0.##} gerçek×{Actual:0.##}",
                symbol, result.SplitMismatches.Count, s.ActionDate, s.ExpectedRatio, s.ActualRatio);
        }

        if (result.AdjustedContinuityMismatches.Count > 0)
        {
            var s = result.AdjustedContinuityMismatches[0];
            _logger.LogWarning(
                "CorporateActionSyncJob — {Symbol} düzeltilmiş fiyat split'te pürüzsüz kalmamış (TV'nin kendi düzeltmesi split'i yutmamış olabilir): {Count} split. Örnek: {Date:yyyy-MM-dd} önce={Before} sonra={After} (oran×{Ratio:0.##})",
                symbol, result.AdjustedContinuityMismatches.Count, s.ActionDate, s.AdjustedBefore, s.AdjustedAfter, s.ActualRatio);
        }

        if (result.UnexplainedJumps.Count > 0)
        {
            var s = result.UnexplainedJumps[0];
            _logger.LogWarning(
                "CorporateActionSyncJob — {Symbol} ham fiyatta kayıtlı olaysız açıklanamaz sıçrama: {Count} gün. Örnek: {Date:yyyy-MM-dd} {Prev}→{Close} ({Pct:0.0}%)",
                symbol, result.UnexplainedJumps.Count, s.Date, s.PrevClose, s.Close, s.PctChange);
        }
    }
}
